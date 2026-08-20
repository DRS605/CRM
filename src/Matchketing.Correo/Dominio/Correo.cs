using System.Security.Cryptography;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Correo.Dominio;

public enum EstadoCorreo
{
    /// <summary>Escrito y en el buzón de salida. Todavía no ha salido.</summary>
    Encolado = 1,

    Enviado = 2,

    /// <summary>Se agotaron los intentos, o el servidor lo rechazó de forma definitiva.</summary>
    Fallido = 3,

    /// <summary>
    /// No se envió porque cuando le llegó el turno ya no se podía. La comprobación de permiso se repite
    /// **justo antes de salir**, no solo al encolarlo. Ver <see cref="Cancelar"/>.
    /// </summary>
    Cancelado = 4,
}

/// <summary>
/// Un correo concreto a una persona concreta: el buzón de salida.
///
/// Es una fila y no una llamada SMTP en el momento de pulsar «enviar», por lo mismo que en los
/// webhooks: un servidor de correo lento dejaría al comercial mirando una rueda, y un fallo de red
/// perdería el correo sin rastro. Con la fila, el envío se reintenta y **queda constancia de lo que se
/// mandó**, que en un correo comercial es la mitad del valor: la cronología del contacto tiene que
/// poder enseñar el texto exacto que se le envió.
///
/// Hay una diferencia con los webhooks que importa, y es el motivo del estado
/// <see cref="EstadoCorreo.Cancelado"/>: entre encolar y enviar pueden pasar minutos, y en esos minutos
/// alguien puede darse de baja. Un webhook que sale tarde no molesta a nadie; un correo comercial a
/// quien acaba de pedir que no le escriban es una infracción. Así que el permiso se comprueba **dos
/// veces**, y la segunda es la que manda.
///
/// Texto plano, sin HTML. No es una limitación pendiente de resolver: un correo de un comercial a un
/// cliente es un correo de una persona a otra, y los que llegan maquetados con cabecera y botones se
/// leen como publicidad porque lo son. Además evita de golpe todo el trabajo de sanear HTML.
/// </summary>
public sealed class Correo : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaAsunto = 300;

    /// <summary>
    /// Las esperas entre intentos. Más cortas que las de un webhook: un correo que sale seis horas
    /// tarde ya no sirve de nada porque la conversación siguió por otro lado, mientras que un webhook
    /// que llega tarde sigue siendo útil. Cuatro intentos en algo más de veinte minutos, y si no ha
    /// salido, se dice.
    /// </summary>
    private static readonly TimeSpan[] Esperas =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    public static int IntentosMaximos => Esperas.Length + 1;

    private Correo(Guid id)
        : base(id, Guid.Empty)
    {
        Para = null!;
        Asunto = null!;
        Cuerpo = null!;
        TokenApertura = null!;
    }

    private Correo(
        Guid id, Guid empresaId, Guid contactoId, Guid usuarioId, string para,
        string asunto, string cuerpo, ParaQue paraQue, Guid? plantillaId, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        ContactoId = contactoId;
        UsuarioId = usuarioId;
        Para = para;
        Asunto = asunto;
        Cuerpo = cuerpo;
        ParaQue = paraQue;
        PlantillaId = plantillaId;
        CreadoEn = ahora;
        ProximoIntentoEn = ahora;
        Estado = EstadoCorreo.Encolado;

        // El token: **la empresa y 16 bytes al azar**, pegados y sin separador.
        //
        // Los 16 bytes al azar son para que sea imposible de adivinar: con un token corto o secuencial,
        // cualquiera podría recorrerlos y marcar como abiertos correos que nadie ha abierto.
        //
        // Y la empresa va dentro porque **la petición del píxel llega sin sesión** —la hace el cliente
        // de correo de la persona— y sin saber la empresa la RLS de PostgreSQL no deja ver ninguna fila:
        // la apertura no se apuntaría nunca. Es el mismo truco que el enlace de baja, y por el mismo
        // motivo. No es un secreto: identificar la empresa no autoriza nada por sí solo, y la parte que
        // autoriza siguen siendo los 16 bytes.
        TokenApertura = Base64Url.Codificar(empresaId.ToByteArray()) + Base64Url.Codificar(RandomNumberGenerator.GetBytes(16));
    }

    public Guid ContactoId { get; private set; }

    /// <summary>Quién lo manda. El correo sale en su nombre, así que su cronología es la suya.</summary>
    public Guid UsuarioId { get; private set; }

    /// <summary>La dirección, congelada al encolar: si el contacto cambia de correo, este ya salió.</summary>
    public string Para { get; private set; }

    public string Asunto { get; private set; }

    public string Cuerpo { get; private set; }

    public ParaQue ParaQue { get; private set; }

    /// <summary>De qué plantilla salió, si salió de una. Nulo si se escribió a mano.</summary>
    public Guid? PlantillaId { get; private set; }

    public EstadoCorreo Estado { get; private set; }

    public int Intentos { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public DateTimeOffset? ProximoIntentoEn { get; private set; }

    public DateTimeOffset? EnviadoEn { get; private set; }

    public string? UltimoFallo { get; private set; }

    /// <summary>Lo que identifica el píxel. Nunca se enseña en ninguna pantalla.</summary>
    public string TokenApertura { get; private set; }

    public DateTimeOffset? PrimeraAperturaEn { get; private set; }

    public DateTimeOffset? UltimaAperturaEn { get; private set; }

    /// <summary>
    /// Cuántas veces se ha pedido el píxel. **No es cuántas veces lo ha leído**: hay clientes de correo
    /// que precargan las imágenes y otros que no las cargan nunca. Ver la nota de la pantalla.
    /// </summary>
    public int Aperturas { get; private set; }

    /// <summary>
    /// La empresa que va dentro de un token de apertura, o nulo si el token no tiene forma de token.
    ///
    /// Se usa antes de tocar la base: el endpoint del píxel fija con esto la empresa activa, y solo
    /// entonces busca la fila. Así la RLS se aplica igual que en cualquier otra petición, y un token de
    /// una empresa no puede leer nada de otra.
    /// </summary>
    public static Guid? EmpresaDelToken(string? token)
    {
        // 22 caracteres son 16 bytes en base64url sin relleno: los del Guid. Lo que sigue es el azar.
        const int LargoGuid = 22;

        if (token is null || token.Length <= LargoGuid)
        {
            return null;
        }

        var bytes = Base64Url.Descodificar(token[..LargoGuid]);
        return bytes is { Length: 16 } ? new Guid(bytes) : null;
    }

    public static Resultado<Correo> Crear(
        Guid empresaId, Guid contactoId, Guid usuarioId, string? para, string? asunto, string? cuerpo,
        ParaQue paraQue, Guid? plantillaId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (Email.Crear(para) is not { Exito: true } direccion)
        {
            return Resultado.Fallo<Correo>(Error.Validacion(
                "correo.destino_invalido", "Ese contacto no tiene una dirección de correo válida."));
        }

        if (string.IsNullOrWhiteSpace(asunto) || asunto.Length > LongitudMaximaAsunto)
        {
            return Resultado.Fallo<Correo>(Error.Validacion(
                "correo.asunto_invalido", "El asunto es obligatorio."));
        }

        if (string.IsNullOrWhiteSpace(cuerpo))
        {
            return Resultado.Fallo<Correo>(Error.Validacion(
                "correo.cuerpo_vacio", "No se manda un correo vacío."));
        }

        return Resultado.Ok(new Correo(
            Guid.NewGuid(), empresaId, contactoId, usuarioId, direccion.Valor.Valor,
            asunto.Trim(), cuerpo.Trim(), paraQue, plantillaId, reloj.AhoraUtc));
    }

    public bool LeToca(DateTimeOffset ahora) =>
        Estado == EstadoCorreo.Encolado && ProximoIntentoEn is { } cuando && cuando <= ahora;

    public void Salio(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        Intentos++;
        Estado = EstadoCorreo.Enviado;
        EnviadoEn = reloj.AhoraUtc;
        ProximoIntentoEn = null;
        UltimoFallo = null;
    }

    /// <summary>No salió. Devuelve cierto si ya no se va a volver a intentar.</summary>
    public bool NoSalio(string porque, bool definitivo, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        Intentos++;
        UltimoFallo = porque;

        // Una dirección que no existe no se arregla reintentando. Insistir cuatro veces contra un
        // buzón inexistente es la forma más rápida de que el servidor de correo empiece a mirarnos mal.
        if (definitivo || Intentos >= IntentosMaximos)
        {
            Estado = EstadoCorreo.Fallido;
            ProximoIntentoEn = null;
            return true;
        }

        ProximoIntentoEn = reloj.AhoraUtc.Add(Esperas[Intentos - 1]);
        return false;
    }

    /// <summary>
    /// No se manda porque cuando le llegó el turno ya no se podía: se dio de baja, le retiraron el
    /// consentimiento, o le borraron el correo de la ficha. **No es un fallo**, es lo correcto, y se
    /// distingue del fallo para que no aparezca en la pantalla como si hubiera que reintentarlo.
    /// </summary>
    public void Cancelar(string porque, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        Estado = EstadoCorreo.Cancelado;
        ProximoIntentoEn = null;
        UltimoFallo = porque;
    }

    /// <summary>
    /// Alguien ha pedido el píxel. Devuelve cierto si es la primera vez, que es lo único que merece
    /// apuntarse en la cronología: cinco líneas de «ha abierto el correo» no dicen más que una.
    /// </summary>
    public bool Abierto(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        // Solo cuenta si el correo llegó a salir. Un píxel pedido de un correo que nunca se envió es un
        // token que alguien está probando a mano.
        if (Estado != EstadoCorreo.Enviado)
        {
            return false;
        }

        Aperturas++;
        UltimaAperturaEn = reloj.AhoraUtc;

        if (PrimeraAperturaEn is not null)
        {
            return false;
        }

        PrimeraAperturaEn = reloj.AhoraUtc;
        return true;
    }
}
