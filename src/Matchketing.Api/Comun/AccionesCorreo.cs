using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Matchketing.Correo.Aplicacion;
using Matchketing.Correo.Dominio;
using Matchketing.Cumplimiento.Aplicacion;
using Matchketing.Cumplimiento.Dominio;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Api.Comun;

/// <summary>
/// Ata el módulo de correo con los de cumplimiento y contactos, sin que ninguno de ellos se conozca.
///
/// Vive en la API porque es la única capa que los conoce a todos, igual que `AccionesRepaso`, y es
/// **pura delegación sin decisiones**: si algún día aparece un `if` aquí, está en el sitio equivocado.
/// La regla de si se puede escribir a alguien la decide `ServicioCumplimiento`, que es donde está
/// probada; la de qué se escribe, `ServicioCorreo`.
/// </summary>
public sealed class PermisoDeEnvio(ServicioCumplimiento cumplimiento) : IPermisoDeEnvio
{
    public Task<Resultado> PuedeEscribirAsync(Guid contactoId, ParaQue paraQue, CancellationToken ct = default) =>
        cumplimiento.PuedeEnviarAsync(contactoId, Traducir(paraQue), ct);

    /// <summary>
    /// Los dos enumerados dicen lo mismo y están duplicados a propósito: si el módulo de correo usara
    /// `FinalidadConsentimiento`, tendría que referenciar al de cumplimiento y se rompería la regla de
    /// que ningún módulo de negocio referencia a otro. La traducción es el precio, y son cuatro líneas.
    /// </summary>
    private static FinalidadConsentimiento Traducir(ParaQue paraQue) => paraQue switch
    {
        ParaQue.Comercial => FinalidadConsentimiento.Comercial,
        _ => FinalidadConsentimiento.AtenderSolicitud,
    };
}

public sealed class ApuntaEnCronologia(ServicioContactos contactos) : IApuntaEnCronologia
{
    public Task ApuntarCorreoAsync(Guid contactoId, string texto, CancellationToken ct = default) =>
        contactos.RegistrarActividadAsync(
            contactoId, TipoActividad.Correo, SentidoActividad.Saliente, texto, null, ct);

    /// <summary>
    /// La apertura va como **entrante y con su propio tipo**.
    ///
    /// Entrante porque es algo que ha hecho la otra persona, no nosotros, y así cuenta como señal de
    /// interés para el Match. Y con tipo propio porque **abrir no es contestar**: si se apuntara como un
    /// correo entrante, el repaso dejaría de preguntar por alguien justo cuando más hay que llamarle.
    /// </summary>
    public Task ApuntarAperturaAsync(Guid contactoId, string texto, CancellationToken ct = default) =>
        contactos.RegistrarActividadAsync(
            contactoId, TipoActividad.AperturaCorreo, SentidoActividad.Entrante, texto, null, ct);
}
