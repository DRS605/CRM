using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Avisos.Dominio;

/// <summary>
/// El permiso de un navegador concreto para recibir avisos, con las claves para cifrárselos.
///
/// Es **por aparato, no por persona**: el mismo comercial en el móvil y en el portátil son dos
/// suscripciones, y eso está bien —quiere el aviso donde esté—. La identidad es el `endpoint`, una URL
/// que da el servicio de push y que es única por aparato y por navegador.
///
/// Caducan solas y sin avisar: el navegador las renueva cuando le apetece, la gente cambia de móvil, y
/// un servicio de push responde 404 o 410 cuando una suscripción ya no vale. Cuando eso pasa hay que
/// **borrarla**, no reintentarla: seguir mandando a un endpoint muerto es la forma de que un servicio
/// de push empiece a mirarte mal.
/// </summary>
public sealed class SuscripcionAviso : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaEndpoint = 600;

    private SuscripcionAviso(Guid id)
        : base(id, Guid.Empty)
    {
        Endpoint = null!;
        ClavePublica = null!;
        Secreto = null!;
    }

    private SuscripcionAviso(Guid id, Guid empresaId, Guid usuarioId, string endpoint, string clavePublica, string secreto, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        UsuarioId = usuarioId;
        Endpoint = endpoint;
        ClavePublica = clavePublica;
        Secreto = secreto;
        CreadoEn = ahora;
    }

    public Guid UsuarioId { get; private set; }

    /// <summary>La URL del servicio de push. Identifica el aparato.</summary>
    public string Endpoint { get; private set; }

    /// <summary>El `p256dh` de la suscripción: la clave pública del navegador.</summary>
    public string ClavePublica { get; private set; }

    /// <summary>El `auth` de la suscripción: 16 bytes que actúan de sal.</summary>
    public string Secreto { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    /// <summary>Cuándo se le mandó el último aviso. Evita repetir el del viernes dos veces.</summary>
    public DateTimeOffset? UltimoAvisoEn { get; private set; }

    public static Resultado<SuscripcionAviso> Crear(
        Guid empresaId, Guid usuarioId, string? endpoint, string? clavePublica, string? secreto, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        // Solo https. Un endpoint http sería un aviso viajando en claro por ahí, y ningún servicio de
        // push de verdad usa http; si llega uno, viene de algo que no queremos.
        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps)
        {
            return Resultado.Fallo<SuscripcionAviso>(Error.Validacion(
                "suscripcion.endpoint_invalido", "El endpoint de la suscripción tiene que ser una dirección https."));
        }

        if (endpoint.Length > LongitudMaximaEndpoint)
        {
            return Resultado.Fallo<SuscripcionAviso>(Error.Validacion(
                "suscripcion.endpoint_largo", "El endpoint de la suscripción es demasiado largo."));
        }

        // Las claves se validan **al suscribirse**, no al mandar el primer aviso. Si no, una
        // suscripción rota se guardaría bien y fallaría en silencio el viernes por la tarde, que es
        // justo cuando nadie va a mirar los registros.
        if (Base64Url.Descodificar(clavePublica) is not { Length: 65 } punto || punto[0] != 0x04)
        {
            return Resultado.Fallo<SuscripcionAviso>(Error.Validacion(
                "suscripcion.p256dh_invalida", "La clave del navegador no tiene el formato esperado."));
        }

        if (Base64Url.Descodificar(secreto) is not { Length: 16 })
        {
            return Resultado.Fallo<SuscripcionAviso>(Error.Validacion(
                "suscripcion.auth_invalida", "El secreto de la suscripción no tiene el formato esperado."));
        }

        return Resultado.Ok(new SuscripcionAviso(
            Guid.NewGuid(), empresaId, usuarioId, endpoint, clavePublica!, secreto!, reloj.AhoraUtc));
    }

    /// <summary>Renueva las claves de un endpoint que ya conocíamos. El navegador las rota por su cuenta.</summary>
    public Resultado Renovar(string? clavePublica, string? secreto)
    {
        if (Base64Url.Descodificar(clavePublica) is not { Length: 65 } punto || punto[0] != 0x04)
        {
            return Resultado.Fallo(Error.Validacion(
                "suscripcion.p256dh_invalida", "La clave del navegador no tiene el formato esperado."));
        }

        if (Base64Url.Descodificar(secreto) is not { Length: 16 })
        {
            return Resultado.Fallo(Error.Validacion(
                "suscripcion.auth_invalida", "El secreto de la suscripción no tiene el formato esperado."));
        }

        ClavePublica = clavePublica!;
        Secreto = secreto!;
        return Resultado.Ok();
    }

    public void Avisada(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        UltimoAvisoEn = reloj.AhoraUtc;
    }

    /// <summary>
    /// ¿Le toca el aviso semanal? Se mira contra el último que se le mandó, no contra un calendario:
    /// así, si el trabajo se ejecuta dos veces —dos instancias, un reintento— no le llegan dos avisos.
    /// </summary>
    public bool LeTocaAviso(DateTimeOffset ahora, int diasDeGracia = 3) =>
        UltimoAvisoEn is null || UltimoAvisoEn.Value.AddDays(diasDeGracia) <= ahora;
}
