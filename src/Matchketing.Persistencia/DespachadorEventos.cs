using System.Text.Json;
using System.Text.Json.Serialization;
using Matchketing.Automatizacion.Aplicacion;
using Matchketing.Automatizacion.Dominio;
using Matchketing.Contactos.Dominio;
using Matchketing.Embudo.Dominio;
using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Webhooks.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia;

/// <summary>
/// Convierte los eventos de dominio en entregas de webhook, dentro de la misma transacción.
///
/// Los eventos de dominio existían desde el primer módulo y **no los consumía nadie**: los agregados
/// los iban acumulando y EF los ignoraba. Este es su primer consumidor, y engancharse aquí en vez de
/// en cada endpoint tiene una consecuencia concreta: una oportunidad ganada desde el repaso emite
/// igual que una ganada desde el tablero, sin que el repaso sepa que existen los webhooks. Colgarlo de
/// los endpoints habría dejado fuera la mitad de los caminos, y nadie lo habría notado hasta que un
/// cliente preguntara por qué a veces no llega.
///
/// Va **antes** de guardar y en la misma llamada, así que las filas de entrega y el cambio de negocio
/// entran o no entran juntos. Es el patrón del buzón de salida; el porqué está en
/// <see cref="Entrega"/>.
/// </summary>
internal static class DespachadorEventos
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Despacha los webhooks y **devuelve** lo que tendrían que ejecutar las reglas, sin ejecutarlo.
    ///
    /// Los webhooks se resuelven aquí, antes de guardar, porque sus filas de entrega tienen que entrar en
    /// el mismo `SaveChanges`. Las reglas no: ver <see cref="AutomatizarAsync"/>.
    /// </summary>
    public static async Task<IReadOnlyList<Ocurrencia>> DespacharAsync(
        ContextoMatchketing bd, IReloj reloj, CancellationToken ct)
    {
        // Los eventos se recogen y se limpian **siempre**, incluso si no hay ningún webhook escuchando.
        // Si no, se quedarían pegados al agregado y el siguiente guardado los volvería a emitir.
        var raices = bd.ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<RaizAgregado<Guid>>()
            .Where(r => r.Eventos.Count > 0)
            .ToList();

        if (raices.Count == 0)
        {
            return [];
        }

        var eventos = raices.SelectMany(r => r.Eventos).ToList();
        foreach (var raiz in raices)
        {
            raiz.LimpiarEventos();
        }

        var traducidos = eventos.Select(Traducir).Where(x => x is not null).Select(x => x!.Value).ToList();
        if (traducidos.Count == 0)
        {
            return [];
        }

        var ocurrencias = traducidos
            .Select(t => Ocurrir(t.Tipo, t.Datos))
            .Where(o => o is not null)
            .Select(o => o!)
            .ToList();

        // Una sola consulta para todos los tipos de esta transacción, y solo si hay algo que emitir.
        // El coste tiene que ser cero cuando no hay webhooks, que es el caso de casi todo el mundo.
        var suscripciones = await bd.Webhooks.Where(s => s.Activa).ToListAsync(ct).ConfigureAwait(false);
        if (suscripciones.Count == 0)
        {
            return ocurrencias;
        }

        var ahora = reloj.AhoraUtc;

        foreach (var (tipo, empresaId, datos) in traducidos)
        {
            foreach (var suscripcion in suscripciones.Where(s => s.EmpresaId == empresaId && s.Escucha(tipo)))
            {
                var id = Guid.NewGuid();
                var cuerpo = JsonSerializer.Serialize(
                    new { id, tipo = TiposEvento.Texto(tipo), ocurridoEn = ahora, empresaId, datos },
                    Json);

                bd.EntregasWebhook.Add(Entrega.Crear(id, empresaId, suscripcion.Id, tipo, cuerpo, reloj));
            }
        }

        return ocurrencias;
    }

    /// <summary>
    /// Ejecuta las reglas que apliquen, **después de haber guardado**, y descarta los eventos que generen
    /// las acciones.
    ///
    /// Lo de «después de haber guardado» costó encontrarlo y es la razón de que esto no vaya donde van los
    /// webhooks. Las acciones no son escrituras sueltas: apuntar una nota, asignar un comercial o encolar
    /// un correo pasan por sus servicios, y esos servicios **cargan el contacto de la base** para
    /// comprobar que existe y si se le puede escribir. Con el contacto todavía sin guardar, esas tres
    /// acciones fallaban en silencio y solo funcionaba la de crear una tarea, que es la única que no
    /// consulta nada. Y solo con los disparadores de contacto: con los de oportunidad iba todo, porque
    /// esas filas ya estaban guardadas. Un fallo así no da ningún error: la regla dice que actuó y en su
    /// registro pone «no se pudo», que es exactamente lo que hay que leer para darse cuenta.
    ///
    /// El precio es un segundo `SaveChanges`, y por eso quien llama envuelve los dos en una transacción:
    /// el cambio de negocio y lo que provoca entran o no entran juntos.
    ///
    /// Lo segundo —descartar los eventos de las acciones— evita que dos reglas se peloteen un evento
    /// entre ellas para siempre, y que se queden pegados al agregado para que los despache el
    /// `SaveChanges` de otra petición. Es seguro **porque ninguna acción toca el embudo**: las cuatro
    /// crean trabajo, dejan constancia o mandan algo que pasa por el permiso. Si algún día se añade una
    /// que sí lo toque, esta decisión hay que revisarla.
    /// </summary>
    public static async Task AutomatizarAsync(
        ContextoMatchketing bd, IServiceProvider servicios,
        IReadOnlyList<Ocurrencia> ocurrencias, CancellationToken ct)
    {
        if (servicios.GetService(typeof(ServicioAutomatizacion)) is not ServicioAutomatizacion automatizacion)
        {
            return;
        }

        await automatizacion.DispararAsync(ocurrencias, ct).ConfigureAwait(false);

        foreach (var raiz in bd.ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<RaizAgregado<Guid>>()
            .Where(r => r.Eventos.Count > 0))
        {
            raiz.LimpiarEventos();
        }
    }

    /// <summary>
    /// Del evento público al disparador de una regla. Es una traducción y no el mismo tipo porque un
    /// webhook y una regla no tienen por qué escuchar siempre lo mismo: hoy coinciden, y está bien que
    /// coincidan, pero atarlos con un `enum` compartido haría que añadir un evento público obligara a
    /// añadir un disparador.
    /// </summary>
    private static Ocurrencia? Ocurrir(TipoEvento tipo, object datos)
    {
        var lector = datos.GetType();
        Guid? Leer(string nombre) => lector.GetProperty(nombre)?.GetValue(datos) as Guid?;

        var contactoId = Leer("contactoId");

        return tipo switch
        {
            TipoEvento.LeadCreado when contactoId is { } c => new Ocurrencia(Disparador.LeadCreado, c, c),
            TipoEvento.ContactoBaja when contactoId is { } c => new Ocurrencia(Disparador.ContactoBaja, c, c),

            TipoEvento.OportunidadGanada when Leer("oportunidadId") is { } o =>
                new Ocurrencia(Disparador.OportunidadGanada, o, contactoId),
            TipoEvento.OportunidadPerdida when Leer("oportunidadId") is { } o =>
                new Ocurrencia(Disparador.OportunidadPerdida, o, contactoId),
            TipoEvento.OportunidadMovida when Leer("oportunidadId") is { } o =>
                new Ocurrencia(Disparador.OportunidadMovida, o, contactoId),

            _ => null,
        };
    }

    /// <summary>
    /// Del evento de dominio al evento público. La mayoría de los eventos **no** se traducen, y eso es
    /// lo normal: el catálogo público son cinco cosas y el dominio emite muchas más.
    ///
    /// Lo que va en `datos` sigue la regla del módulo: qué ha pasado y a quién apunta, sin teléfonos,
    /// sin correos y sin texto libre. La única excepción es la baja, y está razonada donde se define.
    /// </summary>
    private static (TipoEvento Tipo, Guid EmpresaId, object Datos)? Traducir(IEventoDominio evento) => evento switch
    {
        // Todo contacto nace como lead (ver `Contacto.Crear`), así que «contacto creado» y «lead
        // creado» son lo mismo visto desde fuera. El nombre público es el de fuera.
        ContactoCreado c => (TipoEvento.LeadCreado, c.EmpresaId, new
        {
            contactoId = c.ContactoId,
            origen = c.Origen,
        }),

        ContactoDadoDeBaja c => (TipoEvento.ContactoBaja, c.EmpresaId, new
        {
            contactoId = c.ContactoId,
            email = c.Email,
        }),

        OportunidadMovida o => (TipoEvento.OportunidadMovida, o.EmpresaId, new
        {
            oportunidadId = o.OportunidadId,
            contactoId = o.ContactoId,
            etapaId = o.EtapaId,
            etapaAnteriorId = o.EtapaAnteriorId,
        }),

        OportunidadGanada o => (TipoEvento.OportunidadGanada, o.EmpresaId, new
        {
            oportunidadId = o.OportunidadId,
            contactoId = o.ContactoId,
            importe = o.Importe,
        }),

        OportunidadPerdida o => (TipoEvento.OportunidadPerdida, o.EmpresaId, new
        {
            oportunidadId = o.OportunidadId,
            contactoId = o.ContactoId,
            motivo = o.Motivo.ToString(),
        }),

        _ => null,
    };
}
