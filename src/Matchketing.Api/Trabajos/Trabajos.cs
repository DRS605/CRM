using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Matchketing.Cumplimiento.Aplicacion;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Match.Aplicacion;
using Matchketing.Organizacion.Aplicacion;
using Matchketing.Persistencia;

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
