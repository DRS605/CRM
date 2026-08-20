using System.Text.Json;
using System.Text.Json.Serialization;
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

    public static async Task DespacharAsync(ContextoMatchketing bd, IReloj reloj, CancellationToken ct)
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
            return;
        }

        var eventos = raices.SelectMany(r => r.Eventos).ToList();
        foreach (var raiz in raices)
        {
            raiz.LimpiarEventos();
        }

        var traducidos = eventos.Select(Traducir).Where(x => x is not null).Select(x => x!.Value).ToList();
        if (traducidos.Count == 0)
        {
            return;
        }

        // Una sola consulta para todos los tipos de esta transacción, y solo si hay algo que emitir.
        // El coste tiene que ser cero cuando no hay webhooks, que es el caso de casi todo el mundo.
        var suscripciones = await bd.Webhooks.Where(s => s.Activa).ToListAsync(ct).ConfigureAwait(false);
        if (suscripciones.Count == 0)
        {
            return;
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
