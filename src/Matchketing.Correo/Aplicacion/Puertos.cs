using Matchketing.Correo.Dominio;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Correo.Aplicacion;

public interface IRepositorioCorreo
{
    Task<Plantilla?> PlantillaAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Plantilla>> PlantillasAsync(CancellationToken ct = default);

    void Anadir(Plantilla plantilla);

    void Quitar(Plantilla plantilla);

    Task<Dominio.Correo?> PorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Por el token del píxel. **Sin filtro de empresa**: la petición del píxel no trae sesión.</summary>
    Task<Dominio.Correo?> PorTokenAsync(string token, CancellationToken ct = default);

    Task<IReadOnlyList<Dominio.Correo>> PendientesAsync(DateTimeOffset hasta, int tope, CancellationToken ct = default);

    Task<IReadOnlyList<Dominio.Correo>> DeContactoAsync(Guid contactoId, int cuantos, CancellationToken ct = default);

    void AnadirCorreo(Dominio.Correo correo);
}

/// <summary>
/// Si a esta persona se le puede escribir, y por qué no si no se puede.
///
/// Es el puerto que ata este módulo al de cumplimiento sin referenciarlo. Devuelve `Resultado` y no un
/// booleano a propósito: quien no puede enviar necesita saber si es porque se dio de baja, porque no
/// hay consentimiento comercial o porque el contacto no existe, y cada caso se arregla de otra forma.
/// </summary>
public interface IPermisoDeEnvio
{
    Task<Resultado> PuedeEscribirAsync(Guid contactoId, ParaQue paraQue, CancellationToken ct = default);
}

/// <summary>
/// El enlace de baja de un contacto, o nulo si no se puede construir.
///
/// Otro puerto que ata este módulo al de cumplimiento sin referenciarlo: el enlace lo firma
/// Cumplimiento con su secreto, y la implementación vive en la API, que conoce los dos.
///
/// Existe porque **los correos comerciales salían sin ninguna forma de darse de baja**. La maquinaria
/// estaba entera —el enlace firmado, la ruta pública, la pantalla— y no llegaba al único sitio donde
/// hace falta: dentro del correo. Se veía en la ficha del contacto, para copiarlo a mano.
/// </summary>
public interface IEnlaceDeBaja
{
    string? De(Guid contactoId);
}

/// <summary>Lo que hace falta saber del contacto y de quien escribe para rellenar una plantilla.</summary>
public interface IConsultaDatosDelEnvio
{
    Task<DatosDelEnvio?> DeAsync(Guid contactoId, Guid usuarioId, CancellationToken ct = default);
}

/// <summary>
/// Entrega el correo al servidor SMTP. Lo implementa la infraestructura porque habla con la red, y
/// **solo** por eso: qué se manda, a quién y con qué permiso se decide en el dominio.
/// </summary>
public interface IEnviaCorreo
{
    Task<ResultadoEnvioCorreo> EnviarAsync(
        Dominio.Correo correo, string? urlPixel, string? urlBaja, CancellationToken ct = default);
}

/// <summary>
/// Apunta en la cronología del contacto. Lo implementa la API, que es la única capa que conoce a todos.
/// </summary>
public interface IApuntaEnCronologia
{
    Task ApuntarCorreoAsync(Guid contactoId, string texto, CancellationToken ct = default);

    Task ApuntarAperturaAsync(Guid contactoId, string texto, CancellationToken ct = default);
}

/// <summary>
/// Cómo fue el envío. <paramref name="Definitivo"/> distingue «vuelve a intentarlo» de «no insistas»:
/// un buzón que no existe (5xx del SMTP) no se arregla reintentando, y hacerlo cuatro veces es la
/// forma más rápida de que el servidor de correo empiece a mirarnos mal.
/// </summary>
public sealed record ResultadoEnvioCorreo(bool Salio, string? Fallo, bool Definitivo);
