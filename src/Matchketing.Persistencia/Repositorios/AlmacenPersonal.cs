using Matchketing.Contactos.Dominio;
using Matchketing.Cumplimiento.Aplicacion;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

/// <summary>
/// La única clase del sistema que conoce **todas** las tablas donde puede haber datos de una
/// persona. Está aquí a propósito: los derechos de acceso, portabilidad y supresión del RGPD cruzan
/// los siete módulos, y hacer que Cumplimiento los referenciase para llegar a ellos habría roto la
/// arquitectura para siempre. Cumplimiento declara el puerto; esto lo resuelve.
///
/// Todo pasa por el filtro global de empresa, así que ninguna operación puede tocar datos de otra ni
/// por error: las consultas siguen filtrándose aunque aquí no se vea un `EmpresaId` escrito.
/// </summary>
public sealed class AlmacenPersonal(ContextoMatchketing bd, IContextoEmpresa contexto, IReloj reloj) : IAlmacenPersonal
{
    public Task<bool> ExisteContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        bd.Contactos.AnyAsync(c => c.Id == contactoId, ct);

    public async Task<bool?> EstaDeBajaAsync(Guid contactoId, CancellationToken ct = default)
    {
        var estados = await bd.Contactos
            .Where(c => c.Id == contactoId)
            .Select(c => (EstadoContacto?)c.Estado)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return estados is { } estado ? estado == EstadoContacto.Baja : null;
    }

    public async Task<bool> DarDeBajaContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        var contacto = await bd.Contactos.FirstOrDefaultAsync(c => c.Id == contactoId, ct).ConfigureAwait(false);
        if (contacto is null)
        {
            return false;
        }

        contacto.DarDeBaja(reloj);
        return true;
    }

    public Task<string?> NombreEmpresaAsync(CancellationToken ct = default) =>
        bd.Empresas.Where(e => e.Id == contexto.EmpresaId).Select(e => e.Nombre).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Derecho de acceso y portabilidad. Se entrega **todo** lo que hay de la persona, incluidas las
    /// cosas que uno preferiría no mostrar: la puntuación que le hemos puesto, los motivos con los
    /// que la calculamos y las notas internas que ha escrito el comercial. Son sus datos; que
    /// resulten incómodos no los convierte en nuestros.
    /// </summary>
    public async Task<object?> ReunirContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        var contacto = await bd.Contactos
            .Where(c => c.Id == contactoId)
            .Select(c => new
            {
                c.Id, c.Nombre, c.Email, c.Telefono, c.Cargo, c.Origen,
                estado = c.Estado.ToString(), c.Activo, c.CreadoEn, c.ActualizadoEn,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (contacto is null)
        {
            return null;
        }

        var actividades = await bd.Actividades
            .Where(a => a.ContactoId == contactoId)
            .OrderBy(a => a.OcurridaEn)
            .Select(a => new { tipo = a.Tipo.ToString(), sentido = a.Sentido.ToString(), a.Cuerpo, resultado = a.Resultado == null ? null : a.Resultado.ToString(), a.OcurridaEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var oportunidades = await bd.Oportunidades
            .Where(o => o.ContactoId == contactoId)
            .OrderBy(o => o.CreadoEn)
            .Select(o => new { o.Titulo, o.Importe, estado = o.Estado.ToString(), motivo = o.Motivo == null ? null : o.Motivo.ToString(), o.DetalleMotivo, o.CreadoEn, o.CerradaEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var tareas = await bd.Tareas
            .Where(t => t.ContactoId == contactoId)
            .OrderBy(t => t.CreadoEn)
            .Select(t => new { t.Titulo, t.VenceEl, estado = t.Estado.ToString(), t.VecesAplazada, t.CreadoEn, t.CerradaEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var senales = await bd.Senales
            .Where(s => s.ContactoId == contactoId)
            .OrderBy(s => s.OcurridaEn)
            .Select(s => new { tipo = s.Tipo.ToString(), s.OcurridaEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var puntuacion = await bd.Puntuaciones
            .Where(p => p.ContactoId == contactoId)
            .Select(p => new { p.Match, p.Encaje, p.Momento, p.Motivos, p.CalculadaEn })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var envios = await bd.Envios
            .Where(e => e.ContactoId == contactoId)
            .OrderBy(e => e.RecibidoEn)
            .Select(e => new { e.Datos, e.Ip, e.Agente, e.RecibidoEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var permisos = await bd.Consentimientos
            .Where(c => c.ContactoId == contactoId)
            .OrderBy(c => c.OtorgadoEn)
            .Select(c => new { finalidad = c.Finalidad.ToString(), baseLegal = c.Base.ToString(), c.Canal, c.TextoAceptado, c.Ip, c.Agente, c.OtorgadoEn, c.RetiradoEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // **Los correos que se le mandaron, con su texto.** También son datos suyos, y probablemente los
        // que más le interese ver a quien ejerce el derecho de acceso: no «se le escribió tres veces»,
        // sino qué decían esos tres correos. Faltaban, igual que faltaban en la supresión.
        //
        // Se dice si lo abrió, porque también es un dato que guardamos de él, y con la misma cautela con
        // que se dice en la pantalla: pedir el píxel no es lo mismo que leerlo.
        var correos = await bd.Mensajes
            .Where(m => m.ContactoId == contactoId)
            .OrderBy(m => m.CreadoEn)
            .Select(m => new
            {
                m.Para, m.Asunto, m.Cuerpo, estado = m.Estado.ToString(),
                m.CreadoEn, m.EnviadoEn, m.Aperturas, m.PrimeraAperturaEn,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new
        {
            generado = reloj.AhoraUtc,
            aviso = "Copia de todos los datos personales que match.keting guarda de esta persona.",
            contacto,
            consentimientos = permisos,
            cronologia = actividades,
            correos,
            oportunidades,
            tareas,
            senales,
            puntuacion,
            enviosDeFormulario = envios,
        };
    }

    public async Task<object> ReunirEmpresaAsync(CancellationToken ct = default)
    {
        var empresa = await bd.Empresas
            .Where(e => e.Id == contexto.EmpresaId)
            .Select(e => new { e.Id, e.Nombre, e.Nif, e.Provincia, e.PesoEncaje, e.HorasRebote, e.MesesRetencionLeads, e.CreadoEn })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var contactos = await bd.Contactos
            .OrderBy(c => c.CreadoEn)
            .Select(c => new { c.Id, c.Nombre, c.Email, c.Telefono, c.Cargo, c.Origen, estado = c.Estado.ToString(), c.Activo, c.CuentaId, c.CreadoEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var cuentas = await bd.Cuentas
            .OrderBy(c => c.Nombre)
            .Select(c => new { c.Id, c.Nombre, c.Nif, c.Sector, c.Provincia, c.Tamano, c.Web, c.Activa })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var actividades = await bd.Actividades
            .OrderBy(a => a.OcurridaEn)
            .Select(a => new { a.ContactoId, tipo = a.Tipo.ToString(), sentido = a.Sentido.ToString(), a.Cuerpo, a.OcurridaEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var oportunidades = await bd.Oportunidades
            .OrderBy(o => o.CreadoEn)
            .Select(o => new { o.Id, o.ContactoId, o.Titulo, o.Importe, estado = o.Estado.ToString(), motivo = o.Motivo == null ? null : o.Motivo.ToString(), o.CreadoEn, o.CerradaEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var tareas = await bd.Tareas
            .OrderBy(t => t.CreadoEn)
            .Select(t => new { t.ContactoId, t.Titulo, t.VenceEl, estado = t.Estado.ToString(), t.CreadoEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var formularios = await bd.Formularios
            .OrderBy(f => f.Nombre)
            .Select(f => new { f.Id, f.Nombre, f.Clave, f.TextoConsentimiento, f.Origen, f.Activo })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var permisos = await bd.Consentimientos
            .OrderBy(c => c.OtorgadoEn)
            .Select(c => new { c.ContactoId, finalidad = c.Finalidad.ToString(), baseLegal = c.Base.ToString(), c.Canal, c.OtorgadoEn, c.RetiradoEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new
        {
            generado = reloj.AhoraUtc,
            aviso = "Copia completa de los datos de la empresa en match.keting.",
            empresa,
            cuentas,
            contactos,
            consentimientos = permisos,
            cronologia = actividades,
            oportunidades,
            tareas,
            formularios,
        };
    }

    /// <summary>
    /// Supresión real. Se borra en orden de dependencia —los pasos de etapa antes que las
    /// oportunidades— y el contacto al final, para no dejar huérfanos si algo falla a mitad.
    ///
    /// El envío de formulario se borra entero, no se le quita el <c>contacto_id</c>: dentro lleva el
    /// nombre, el correo y el mensaje que escribió la persona, así que desvincularlo dejaría el dato
    /// personal donde estaba y solo habría escondido a quién pertenece.
    /// </summary>
    public async Task<RecuentoBorrado> BorrarContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        var oportunidades = await bd.Oportunidades.Where(o => o.ContactoId == contactoId).Select(o => o.Id).ToListAsync(ct).ConfigureAwait(false);

        // Los pasos de etapa no llevan empresa (cuelgan de la oportunidad), así que se filtran por
        // los identificadores que ya salieron de una consulta filtrada por empresa.
        await bd.PasosEtapa.Where(p => oportunidades.Contains(p.OportunidadId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var tareas = await bd.Tareas.Where(t => t.ContactoId == contactoId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var borradasOportunidades = await bd.Oportunidades.Where(o => o.ContactoId == contactoId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var actividades = await bd.Actividades.Where(a => a.ContactoId == contactoId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var senales = await bd.Senales.Where(s => s.ContactoId == contactoId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var puntuaciones = await bd.Puntuaciones.Where(p => p.ContactoId == contactoId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var envios = await bd.Envios.Where(e => e.ContactoId == contactoId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var permisos = await bd.Consentimientos.Where(c => c.ContactoId == contactoId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        // **Los correos que se le mandaron.** Es lo más personal que guarda el sistema de alguien: su
        // dirección, el asunto y el texto completo de cada mensaje. Estuvo fuera de la supresión varios
        // módulos, y eso convertía «borrar es borrar» en una frase falsa.
        var correos = await bd.Mensajes.Where(m => m.ContactoId == contactoId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        // Su fila en cada campaña. No lleva nombre ni correo —eso se decidió así al construirlo— pero sí
        // su identificador, y una lista de identificadores de gente borrada sigue siendo una lista.
        var deCampania = await bd.EnviosCampania.Where(e => e.ContactoId == contactoId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        // Las reglas que actuaron sobre él. `SujetoId` también, porque en las reglas de contacto el
        // sujeto **es** el contacto: filtrar solo por `ContactoId` dejaba la mitad de las filas.
        var ejecuciones = await bd.Ejecuciones
            .Where(e => e.ContactoId == contactoId || e.SujetoId == contactoId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        // Los cuerpos de webhook que lo mencionan. Aquí no hay columna por la que filtrar: el
        // identificador va **dentro del JSON** que se envió. Se busca por texto, que es un recorrido de
        // tabla, y se acepta: una supresión ocurre una vez por persona y como mucho unos cientos de veces
        // al año; dejar ahí el correo de alguien que pidió que lo borraran, no se acepta.
        //
        // Lo que ya salió hacia el sistema de terceros no se puede recoger —eso está dicho en la
        // documentación del módulo— pero nuestra copia sí, y es nuestra.
        var texto = contactoId.ToString();
        var entregas = await bd.EntregasWebhook
            .Where(e => e.Cuerpo.Contains(texto))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        // Sus preguntas aparcadas del repaso. La clave es «tipo:identificador», así que se busca por el
        // final: sin esto quedaría una fila diciendo que alguien decidió no llamar a un contacto que ya
        // no existe, y al volver a entrar un contacto con el mismo identificador —imposible, pero— la
        // pregunta llegaría ya aparcada.
        var aparcadas = await bd.Pospuestas
            .Where(p => p.Clave.EndsWith(texto))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        // Quien se hubiera fusionado dentro de este contacto deja de apuntar a un fantasma.
        await bd.Contactos.Where(c => c.FusionadoEnId == contactoId).ExecuteUpdateAsync(
            s => s.SetProperty(c => c.FusionadoEnId, (Guid?)null), ct).ConfigureAwait(false);

        var contactos = await bd.Contactos.Where(c => c.Id == contactoId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        return new RecuentoBorrado(
            contactos, actividades, borradasOportunidades, tareas, senales, puntuaciones, envios, permisos,
            correos, deCampania, ejecuciones, entregas, aparcadas);
    }

    /// <summary>
    /// Borra la empresa entera, **incluida su auditoría**: el registro es parte de sus datos y no
    /// tendría sentido conservar los apuntes de una empresa que ya no existe. Quien llama escribe
    /// después el último apunte, que es lo único que sobrevive.
    /// </summary>
    public async Task<RecuentoBorrado> BorrarEmpresaAsync(CancellationToken ct = default)
    {
        await bd.PasosEtapa.Where(p => bd.Oportunidades.Any(o => o.Id == p.OportunidadId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var tareas = await bd.Tareas.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var oportunidades = await bd.Oportunidades.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Etapas.Where(e => bd.Embudos.Any(x => x.Id == e.EmbudoId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Embudos.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var actividades = await bd.Actividades.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var senales = await bd.Senales.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var puntuaciones = await bd.Puntuaciones.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var envios = await bd.Envios.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Formularios.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var permisos = await bd.Consentimientos.ExecuteDeleteAsync(ct).ConfigureAwait(false);

        // Todo lo que fueron añadiendo los módulos posteriores. Sin esto, «borrar la empresa» dejaba en
        // la base sus correos, sus campañas, sus reglas y sus objetivos: invisibles por la RLS —nadie
        // vuelve a entrar en esa empresa— pero ahí, que es exactamente lo que se prometió que no pasaría.
        var correos = await bd.Mensajes.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Plantillas.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var deCampania = await bd.EnviosCampania.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Campanias.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Segmentos.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        var ejecuciones = await bd.Ejecuciones.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Reglas.ExecuteDeleteAsync(ct).ConfigureAwait(false);

        // Las entregas cuelgan de la suscripción y no llevan empresa, así que se filtran por las
        // suscripciones de esta —que sí la llevan— antes de borrarlas.
        var entregas = await bd.EntregasWebhook
            .Where(e => bd.Webhooks.Any(w => w.Id == e.SuscripcionId))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
        await bd.Webhooks.ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var aparcadas = await bd.Pospuestas.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Suscripciones.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Objetivos.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Invitaciones.ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var contactos = await bd.Contactos.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Cuentas.ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.RegistrosAuditoria.ExecuteDeleteAsync(ct).ConfigureAwait(false);

        // Las membresías son la puerta de entrada: sin ellas nadie puede volver a elegir esta
        // empresa. Los usuarios no se borran, porque son globales y pueden trabajar en otras.
        await bd.Membresias.Where(m => m.EmpresaId == contexto.EmpresaId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await bd.Empresas.Where(e => e.Id == contexto.EmpresaId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        return new RecuentoBorrado(
            contactos, actividades, oportunidades, tareas, senales, puntuaciones, envios, permisos,
            correos, deCampania, ejecuciones, entregas, aparcadas);
    }

    /// <summary>
    /// Leads sin futuro: siguen siendo lead, no tienen ninguna oportunidad (ni abierta ni cerrada) y
    /// no se les ha tocado desde el límite. Se mira la última actividad, no la fecha de alta: un lead
    /// de hace tres años al que se llamó el mes pasado se está trabajando.
    ///
    /// Los que pidieron la baja **también** entran, y con más razón: conservar durante dos años el
    /// teléfono de alguien que dijo que no quería saber nada es exactamente lo que no toca.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> LeadsCaducadosAsync(DateTimeOffset limite, CancellationToken ct = default) =>
        await bd.Contactos
            .Where(c => c.Estado != EstadoContacto.Cliente)
            .Where(c => !bd.Oportunidades.Any(o => o.ContactoId == c.Id))
            .Where(c => c.ActualizadoEn < limite)
            .Where(c => !bd.Actividades.Any(a => a.ContactoId == c.Id && a.OcurridaEn >= limite))
            .Select(c => c.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
