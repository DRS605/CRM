using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Matchketing.Embudo.Aplicacion;
using Matchketing.Embudo.Dominio;
using Matchketing.Repaso.Aplicacion;
using Matchketing.Tareas.Aplicacion;
using Matchketing.Tareas.Dominio;

namespace Matchketing.Api.Comun;

/// <summary>
/// Traduce las respuestas del repaso a llamadas a los servicios de los otros módulos.
///
/// Vive en la API y no en la persistencia porque es la única capa que conoce a los cinco módulos que
/// el repaso orquesta. Es **pura delegación, sin decisiones**: qué hacer con cada respuesta se decide
/// en <c>ServicioRepaso</c>, que por eso se puede probar entero sin base de datos. Si algún día
/// aparece un `if` aquí, está en el sitio equivocado.
///
/// Ninguno de estos métodos guarda: el guardado lo hace el endpoint, en una sola transacción con el
/// apunte de que la pregunta queda aparcada. Así una respuesta nunca deja la mitad hecha.
/// </summary>
public sealed class AccionesRepaso(
    ServicioTareas tareas,
    ServicioContactos contactos,
    ServicioEmbudo embudo) : IAccionesRepaso
{
    public async Task<bool> CompletarTareaAsync(Guid tareaId, CancellationToken ct = default) =>
        (await tareas.CompletarAsync(tareaId, ct).ConfigureAwait(false)).Exito;

    public async Task<bool> AplazarTareaAsync(Guid tareaId, DateOnly nuevaFecha, CancellationToken ct = default) =>
        (await tareas.AplazarAsync(tareaId, nuevaFecha, ct).ConfigureAwait(false)).Exito;

    public async Task<bool> DescartarTareaAsync(Guid tareaId, CancellationToken ct = default) =>
        (await tareas.DescartarAsync(tareaId, ct).ConfigureAwait(false)).Exito;

    public async Task<bool> RegistrarLlamadaAsync(Guid contactoId, ResultadoDeLlamada resultado, CancellationToken ct = default)
    {
        var traducido = resultado switch
        {
            ResultadoDeLlamada.Contactado => ResultadoLlamada.Contactado,
            ResultadoDeLlamada.NoContesta => ResultadoLlamada.NoContesta,
            _ => ResultadoLlamada.NoInteresa,
        };

        var r = await contactos.RegistrarLlamadaAsync(contactoId, traducido, null, ct).ConfigureAwait(false);
        if (r.Fallido)
        {
            return false;
        }

        // Mismo comportamiento que el botón de llamar de la ficha: si no contesta, queda el
        // recordatorio de volver a intentarlo. Que el camino corto haga menos que el largo sería la
        // forma más rápida de que nadie se fíe del camino corto.
        if (resultado == ResultadoDeLlamada.NoContesta)
        {
            await tareas.CrearSeguimientoLlamadaAsync(contactoId, ct).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<bool> DescartarContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        (await contactos.CambiarEstadoAsync(contactoId, EstadoContacto.Perdido, ct).ConfigureAwait(false)).Exito;

    public Task<bool> CrearTareaAsync(Guid contactoId, string titulo, DateOnly venceEl, CancellationToken ct = default) =>
        Task.FromResult(tareas.Crear(titulo, contactoId, null, venceEl, OrigenTarea.Automatica).Exito);

    public async Task<decimal?> GanarOportunidadAsync(Guid oportunidadId, CancellationToken ct = default)
    {
        var r = await embudo.GanarAsync(oportunidadId, ct).ConfigureAwait(false);
        return r.Exito ? r.Valor.Importe : null;
    }

    public async Task<bool> PerderOportunidadAsync(Guid oportunidadId, int motivo, CancellationToken ct = default) =>
        (await embudo.PerderAsync(oportunidadId, (MotivoPerdida)motivo, null, ct).ConfigureAwait(false)).Exito;

    public async Task<bool> MoverCierreAsync(Guid oportunidadId, DateOnly nuevaFecha, CancellationToken ct = default) =>
        (await embudo.MoverPrevistaCierreAsync(oportunidadId, nuevaFecha, ct).ConfigureAwait(false)).Exito;
}
