using System.Globalization;
using Matchketing.Automatizacion.Aplicacion;
using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Matchketing.Correo.Aplicacion;
using Matchketing.Correo.Dominio;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Tareas.Aplicacion;
using Matchketing.Tareas.Dominio;

namespace Matchketing.Api.Comun;

/// <summary>
/// Lo que una regla puede hacer, atado a los módulos que lo hacen. Vive en la API porque es la única capa
/// que los conoce a todos, igual que <c>AccionesRepaso</c>.
///
/// Cada método devuelve **lo que hizo, ya escrito**, o nulo si no pudo. Ese texto va al registro de
/// ejecuciones y es lo que se lee en la pantalla: si devolviera un booleano habría que redactarlo en el
/// módulo de automatización, que no sabe lo que pasó de verdad al otro lado.
///
/// Y todo lo que se apunta en la cronología dice **que lo hizo una regla**. Un comercial que se encuentra
/// una tarea que no creó tiene que poder averiguar de dónde salió; una automatización que no se distingue
/// de una persona es una automatización que se acaba apagando por si acaso.
/// </summary>
public sealed class AccionesAutomatizacion(
    ServicioTareas tareas,
    ServicioContactos contactos,
    ServicioCorreo correo,
    IReloj reloj) : IAccionesAutomatizacion
{
    public Task<string?> CrearTareaAsync(Guid contactoId, string titulo, int dias, CancellationToken ct = default)
    {
        var vence = DateOnly.FromDateTime(HorasLaborables.EnHoraLocal(reloj.AhoraUtc).DateTime).AddDays(dias);

        // `OrigenTarea.Automatica`, igual que las que crea el repaso: es lo que permite distinguir en la
        // pantalla una tarea que se puso una persona de una que puso el sistema.
        var r = tareas.Crear(titulo, contactoId, null, vence, OrigenTarea.Automatica);

        return Task.FromResult<string?>(r.Fallido ? null : $"tarea «{titulo}» para el {vence:dd/MM}");
    }

    public async Task<string?> AsignarAsync(Guid contactoId, Guid usuarioId, CancellationToken ct = default)
    {
        var r = await contactos.AsignarPropietarioAsync(contactoId, usuarioId, ct).ConfigureAwait(false);
        return r.Fallido ? null : "asignado a un comercial";
    }

    /// <summary>
    /// Encola un correo con esa plantilla, **por el mismo camino que un correo a mano**: `ServicioCorreo`
    /// comprueba el permiso y lo comprobará otra vez justo antes de que salga.
    ///
    /// Devolver nulo aquí es un caso normal, no un fallo: si esa persona no ha dado su consentimiento, el
    /// correo no sale, y lo correcto es que no salga. Una automatización no es una excusa para saltarse el
    /// RGPD. El motivo exacto queda escrito en el registro de la regla.
    /// </summary>
    public async Task<string?> MandarCorreoAsync(Guid contactoId, Guid plantillaId, CancellationToken ct = default)
    {
        var r = await correo.EnviarAsync(
            contactoId, plantillaId, null, null, ParaQue.AtenderSolicitud, ct).ConfigureAwait(false);

        return r.Fallido ? null : "correo encolado";
    }

    public async Task<string?> ApuntarNotaAsync(Guid contactoId, string texto, CancellationToken ct = default)
    {
        var r = await contactos.RegistrarActividadAsync(
            contactoId, TipoActividad.Sistema, SentidoActividad.Interna,
            $"Regla automática: {texto}", null, ct).ConfigureAwait(false);

        return r.Fallido ? null : "nota apuntada";
    }
}
