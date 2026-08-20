using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Matchketing.Avisos.Aplicacion;
using Matchketing.Cumplimiento.Aplicacion;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Match.Aplicacion;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Organizacion.Aplicacion;
using Matchketing.Persistencia;
using Matchketing.Webhooks.Aplicacion;

namespace Matchketing.Api.Trabajos;

/// <summary>
/// Recalcula el Match de todos los contactos, todas las noches.
///
/// Hace falta porque el **Momento decae con el tiempo** y el tiempo pasa sin que nadie pulse nada. La
/// puntuación se recalcula al instante cuando llega una señal, pero un contacto del que hace tres
/// semanas que no se sabe nada no genera ninguna señal: su Momento tiene que bajar solo. Sin este
/// barrido, la lista de Hoy acabaría encabezada por gente que estuvo muy caliente en marzo.
/// </summary>
public sealed class TrabajoBarridoMatch(IServiceProvider servicios, ILogger<TrabajoBarridoMatch> logger)
    : TrabajoPeriodico(servicios, logger)
{
    protected override string Nombre => "Barrido de Match";

    protected override TimeSpan Cada => TimeSpan.FromHours(24);

    protected override async Task<string?> ParaEmpresaAsync(IServiceProvider ambito, Guid empresaId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ambito);

        var match = ambito.GetRequiredService<ServicioMatch>();
        var unidad = ambito.GetRequiredService<IUnidadDeTrabajo>();

        var cuantos = await match.RecalcularTodosAsync(ct).ConfigureAwait(false);
        await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

        return cuantos == 0 ? null : $"{cuantos} contactos repuntuados.";
    }
}

/// <summary>
/// Rebote de leads sin atender: si nadie ha hecho nada con un lead en las horas laborables que
/// configure la empresa, se le busca otro comercial.
///
/// Es la regla que da sentido al reparto. Repartir bien y luego dejar que un lead se enfríe en la
/// bandeja de alguien que está de vacaciones es igual de malo que repartir a voleo, con el agravante
/// de que parece que el sistema se ocupa. Rebota **una sola vez** por lead: a la segunda el problema
/// no es de quién lo tiene.
/// </summary>
public sealed class TrabajoReboteLeads(IServiceProvider servicios, ILogger<TrabajoReboteLeads> logger)
    : TrabajoPeriodico(servicios, logger)
{
    protected override string Nombre => "Rebote de leads";

    /// <summary>
    /// Cada media hora, no una vez al día: un plazo de cuatro horas laborables que se comprobase de
    /// madrugada se convertiría en un plazo de un día.
    /// </summary>
    protected override TimeSpan Cada => TimeSpan.FromMinutes(30);

    protected override async Task<string?> ParaEmpresaAsync(IServiceProvider ambito, Guid empresaId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ambito);

        var empresas = ambito.GetRequiredService<ServicioEmpresas>();
        var empresa = await empresas.ObtenerAsync(empresaId, ct).ConfigureAwait(false);
        if (empresa.Fallido)
        {
            return null;
        }

        var match = ambito.GetRequiredService<ServicioMatch>();
        var contactos = ambito.GetRequiredService<ServicioContactos>();
        var unidad = ambito.GetRequiredService<IUnidadDeTrabajo>();

        var vencidos = await match.LeadsVencidosAsync(empresa.Valor.HorasRebote, ct).ConfigureAwait(false);
        var rebotados = 0;

        foreach (var lead in vencidos)
        {
            var propuesta = await match.ProponerComercialAsync(lead.ContactoId, ct, lead.PropietarioId).ConfigureAwait(false);

            // Si no hay ningún otro comercial, el lead se queda donde está. Devolverlo a quien no lo
            // atendió no arreglaría nada, y dejarlo sin dueño sería peor todavía.
            if (propuesta.Fallido)
            {
                continue;
            }

            await contactos.AsignarPropietarioAsync(lead.ContactoId, propuesta.Valor.UsuarioId, ct).ConfigureAwait(false);
            await contactos.RegistrarActividadAsync(
                lead.ContactoId, TipoActividad.Rebote, SentidoActividad.Interna,
                $"Sin atender en {empresa.Valor.HorasRebote} h laborables. Pasa a {propuesta.Valor.Nombre}: {string.Join(", ", propuesta.Valor.Motivos)}.",
                null, ct).ConfigureAwait(false);

            rebotados++;
        }

        if (rebotados > 0)
        {
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
        }

        return rebotados == 0 ? null : $"{rebotados} leads rebotados.";
    }
}

/// <summary>
/// Retención: borra cada noche los leads que ya han cumplido su plazo de conservación.
///
/// Es el único trabajo que **borra datos** sin que nadie lo pida, y por eso es el que más cuidado
/// lleva: va en una transacción con su apunte de auditoría, y el apunte solo se escribe si hubo algo
/// que borrar. Un registro con una línea diaria de «se han borrado 0 leads» sería un registro que
/// nadie lee.
/// </summary>
public sealed class TrabajoRetencion(IServiceProvider servicios, ILogger<TrabajoRetencion> logger)
    : TrabajoPeriodico(servicios, logger)
{
    protected override string Nombre => "Retención de leads";

    protected override TimeSpan Cada => TimeSpan.FromHours(24);

    /// <summary>Diez minutos después de arrancar: nunca compite con el barrido de Match.</summary>
    protected override TimeSpan Espera => TimeSpan.FromMinutes(10);

    protected override async Task<string?> ParaEmpresaAsync(IServiceProvider ambito, Guid empresaId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ambito);

        var cumplimiento = ambito.GetRequiredService<ServicioCumplimiento>();
        var bd = ambito.GetRequiredService<ContextoMatchketing>();
        var unidad = ambito.GetRequiredService<IUnidadDeTrabajo>();

        await using var transaccion = await bd.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var r = await cumplimiento.AplicarRetencionAsync(empresaId, ct).ConfigureAwait(false);
        if (r.Fallido)
        {
            return null;
        }

        await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
        await transaccion.CommitAsync(ct).ConfigureAwait(false);

        return r.Valor.LeadsBorrados == 0
            ? null
            : $"{r.Valor.LeadsBorrados} leads borrados por antigüedad ({r.Valor.Meses} meses), {r.Valor.FilasBorradas} filas.";
    }
}

/// <summary>
/// El empujón del viernes por la tarde: manda un aviso a quien tenga decisiones pendientes en el
/// repaso.
///
/// Es la última pieza de la tesis del repaso. El repaso hace que cerrar la semana cueste dos minutos;
/// esto hace que uno **se acuerde**. Sin el aviso, el repaso lo hace quien ya era ordenado, que es
/// justo quien menos lo necesitaba.
///
/// Se comprueba cada media hora y solo actúa en la ventana del viernes por la tarde. La idempotencia
/// no la da el reloj sino <c>SuscripcionAviso.UltimoAvisoEn</c>: si el trabajo corre dos veces —dos
/// instancias, un reintento— no llegan dos avisos.
/// </summary>
public sealed class TrabajoAvisoRepaso(IServiceProvider servicios, ILogger<TrabajoAvisoRepaso> logger)
    : TrabajoPeriodico(servicios, logger)
{
    /// <summary>Viernes a las 18:00, hora de España. Ver <see cref="HorasLaborables"/>.</summary>
    private const int HoraDelAviso = 18;

    protected override string Nombre => "Aviso del repaso";

    protected override TimeSpan Cada => TimeSpan.FromMinutes(30);

    protected override TimeSpan Espera => TimeSpan.FromMinutes(5);

    protected override async Task<string?> ParaEmpresaAsync(IServiceProvider ambito, Guid empresaId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ambito);

        if (!EsLaHora(ambito.GetRequiredService<IReloj>().AhoraUtc))
        {
            return null;
        }

        var avisos = ambito.GetRequiredService<ServicioAvisos>();
        var unidad = ambito.GetRequiredService<IUnidadDeTrabajo>();

        var resumen = await avisos.AvisarDelRepasoAsync(ct).ConfigureAwait(false);
        if (resumen.Enviados + resumen.Borrados == 0)
        {
            return resumen.Fallidos == 0 ? null : $"{resumen.Fallidos} avisos no salieron; se reintentan.";
        }

        await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

        return $"{resumen.Enviados} avisos enviados" +
            (resumen.Borrados > 0 ? $", {resumen.Borrados} suscripciones caducadas borradas" : string.Empty) +
            (resumen.Fallidos > 0 ? $", {resumen.Fallidos} fallidos" : string.Empty) + ".";
    }

    /// <summary>
    /// Viernes entre las 18:00 y las 18:59, en hora local española. La ventana es de una hora porque el
    /// trabajo se comprueba cada treinta minutos: con una ventana más estrecha, una pasada que llegue
    /// tarde se salta la semana entera.
    /// </summary>
    private static bool EsLaHora(DateTimeOffset ahoraUtc)
    {
        // `HorasLaborables` ya sabe convertir a hora española y aguanta que falte la base de zonas.
        var local = HorasLaborables.EnHoraLocal(ahoraUtc);
        return local.DayOfWeek == DayOfWeek.Friday && local.Hour == HoraDelAviso;
    }
}

/// <summary>
/// Vacía el buzón de salida de los webhooks.
///
/// Es el otro extremo del patrón: el cambio de negocio escribe la fila en su misma transacción y se
/// va; esto la manda. Corre **cada minuto** porque un webhook es una integración y la gente espera
/// que llegue «ya»: media hora de retraso en un «oportunidad ganada» convierte un enlace con el ERP
/// en algo que nadie usa.
///
/// Un minuto suena a mucho para un trabajo periódico, pero la pasada es baratísima cuando no hay nada:
/// una consulta por el índice <c>ix_entrega_pendientes</c> que devuelve cero filas. Lo caro es
/// entregar, y eso solo pasa cuando hay algo que entregar.
/// </summary>
public sealed class TrabajoEntregaWebhooks(IServiceProvider servicios, ILogger<TrabajoEntregaWebhooks> logger)
    : TrabajoPeriodico(servicios, logger)
{
    protected override string Nombre => "Entrega de webhooks";

    protected override TimeSpan Cada => TimeSpan.FromMinutes(1);

    /// <summary>Medio minuto: que la aplicación acabe de arrancar antes de empezar a mandar.</summary>
    protected override TimeSpan Espera => TimeSpan.FromSeconds(30);

    protected override async Task<string?> ParaEmpresaAsync(IServiceProvider ambito, Guid empresaId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ambito);

        var webhooks = ambito.GetRequiredService<ServicioWebhooks>();
        var unidad = ambito.GetRequiredService<IUnidadDeTrabajo>();

        var r = await webhooks.EntregarPendientesAsync(ct).ConfigureAwait(false);
        if (r.Entregadas + r.Reintentar + r.Agotadas == 0)
        {
            return null;
        }

        // Se guarda **siempre** que se haya intentado algo, incluidos los fallos: el número de intentos
        // y el próximo turno viven en la fila, así que si esto no se guardara, la siguiente pasada
        // volvería a intentar lo mismo desde cero y no se agotaría nunca.
        await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

        return $"{r.Entregadas} entregados" +
            (r.Reintentar > 0 ? $", {r.Reintentar} para reintentar" : string.Empty) +
            (r.Agotadas > 0 ? $", {r.Agotadas} agotados" : string.Empty) +
            (r.Apagadas > 0 ? $", {r.Apagadas} webhooks apagados por fallar demasiado" : string.Empty) + ".";
    }
}
