using System.Net.Mail;
using System.Text;
using Matchketing.Correo.Aplicacion;

namespace Matchketing.Api.Comun;

/// <summary>Lo que hace falta para hablar con un servidor de correo.</summary>
public sealed record AjustesSmtp(
    string? Servidor, int Puerto, string? Usuario, string? Contrasena, string? Remitente, string? NombreRemitente, bool Ssl)
{
    /// <summary>Sin servidor no se manda nada, y hay que poder arrancar igual para no tirar la aplicación.</summary>
    public bool Configurado => !string.IsNullOrWhiteSpace(Servidor) && !string.IsNullOrWhiteSpace(Remitente);
}

/// <summary>
/// Entrega el correo al servidor SMTP de la empresa.
///
/// Hace poco a propósito: qué se manda, a quién y con qué permiso ya está decidido y probado en el
/// dominio. Aquí se decide una sola cosa importante, y es **si un fallo se reintenta o no**. Un buzón
/// que no existe devuelve un 5xx y no se arregla insistiendo; insistir cuatro veces contra direcciones
/// inexistentes es la forma conocida de que un servidor de correo empiece a marcar todo lo que mandas
/// como no deseado. Un 4xx, en cambio, es «ahora no puedo» y sí se reintenta.
///
/// El correo va en **texto plano**. Ver la nota de `Correo`: un mensaje de un comercial a un cliente es
/// un mensaje de una persona a otra, y los que llegan maquetados se leen como publicidad.
///
/// El píxel de apertura es la única excepción, y va como una parte alternativa en HTML. Si la empresa
/// no tiene el seguimiento activado, `urlPixel` llega nulo y entonces el correo es **solo** texto
/// plano: sin parte HTML, sin imagen y sin nada que cargar.
/// </summary>
public sealed class EnviaCorreoSmtp(AjustesSmtp ajustes, ILogger<EnviaCorreoSmtp> registro) : IEnviaCorreo
{
    public async Task<ResultadoEnvioCorreo> EnviarAsync(
        Matchketing.Correo.Dominio.Correo correo, string? urlPixel, string? urlBaja,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(correo);

        if (!ajustes.Configurado)
        {
            // Definitivo: reintentarlo cada minuto hasta agotarlo no arregla una configuración que
            // falta, y así el fallo se ve en la pantalla con su motivo en vez de acumular intentos.
            return new ResultadoEnvioCorreo(false, "No hay servidor de correo configurado.", true);
        }

        // El cuerpo lleva la línea de baja **dentro del texto** cuando toca. No es solo cortesía: la
        // cabecera `List-Unsubscribe` la leen los programas de correo, no las personas, y quien recibe
        // el mensaje tiene derecho a ver cómo se sale sin buscar un botón escondido en su cliente.
        var cuerpo = urlBaja is null
            ? correo.Cuerpo
            : correo.Cuerpo + "\n\n--\nSi no quieres recibir más correos como este, dilo aquí: " + urlBaja;

        using var mensaje = new MailMessage
        {
            From = new MailAddress(ajustes.Remitente!, ajustes.NombreRemitente ?? ajustes.Remitente),
            Subject = correo.Asunto,
            SubjectEncoding = Encoding.UTF8,
            Body = cuerpo,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };

        mensaje.To.Add(new MailAddress(correo.Para));

        if (urlBaja is not null)
        {
            // Las dos cabeceras van juntas o no valen. `List-Unsubscribe` sola es de los noventa y hoy
            // no basta: desde 2024, Gmail y Yahoo exigen a quien manda envíos masivos una baja **de un
            // clic**, y eso es lo que declara `List-Unsubscribe-Post`. Sin ellas, una campaña legítima
            // acaba en la carpeta de no deseados, que es la forma más cara de aprender esto.
            mensaje.Headers.Add("List-Unsubscribe", "<" + urlBaja + ">");
            mensaje.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        if (urlPixel is not null)
        {
            mensaje.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                Html(cuerpo, urlPixel), Encoding.UTF8, "text/html"));
        }

        try
        {
            using var cliente = new SmtpClient(ajustes.Servidor, ajustes.Puerto)
            {
                EnableSsl = ajustes.Ssl,
                Credentials = string.IsNullOrWhiteSpace(ajustes.Usuario)
                    ? null
                    : new System.Net.NetworkCredential(ajustes.Usuario, ajustes.Contrasena),
            };

            await cliente.SendMailAsync(mensaje, ct).ConfigureAwait(false);
            return new ResultadoEnvioCorreo(true, null, false);
        }
        catch (SmtpFailedRecipientException ex)
        {
            // El servidor ha dicho que ese destinatario no vale. Es lo más definitivo que hay.
            registro.LogWarning("Correo rechazado para {Destino}: {Estado}.", correo.Para, ex.StatusCode);
            return new ResultadoEnvioCorreo(false, $"El servidor rechazó la dirección ({ex.StatusCode}).", true);
        }
        catch (SmtpException ex)
        {
            var definitivo = EsDefinitivo(ex.StatusCode);
            registro.LogWarning("Correo no entregado a {Destino}: {Estado}.", correo.Para, ex.StatusCode);
            return new ResultadoEnvioCorreo(false, $"Error del servidor de correo ({ex.StatusCode}).", definitivo);
        }
        catch (InvalidOperationException ex)
        {
            // Configuración imposible: puerto absurdo, credenciales mal formadas. No se reintenta.
            return new ResultadoEnvioCorreo(false, $"Configuración de correo inválida: {ex.Message}", true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ResultadoEnvioCorreo(false, "El servidor de correo no contestó a tiempo.", false);
        }
    }

    /// <summary>
    /// Qué códigos no merecen reintento. La regla es la de siempre en SMTP: 5xx es «no» y 4xx es «no
    /// ahora». `MailboxBusy` o `InsufficientStorage` se van a arreglar solos; `MailboxUnavailable` no.
    /// </summary>
    private static bool EsDefinitivo(SmtpStatusCode codigo) => codigo switch
    {
        SmtpStatusCode.MailboxUnavailable => true,
        SmtpStatusCode.MailboxNameNotAllowed => true,
        SmtpStatusCode.UserNotLocalTryAlternatePath => true,
        SmtpStatusCode.CommandNotImplemented => true,
        SmtpStatusCode.SyntaxError => true,
        SmtpStatusCode.ClientNotPermitted => true,
        _ => false,
    };

    /// <summary>
    /// La parte HTML: el mismo texto y el píxel. Nada más.
    ///
    /// El texto se escapa entero, porque el cuerpo lo ha escrito una persona y puede llevar un `&` o un
    /// `<`. Y el píxel va con `alt` vacío y `aria-hidden`, para que un lector de pantalla no anuncie una
    /// imagen que no significa nada.
    /// </summary>
    private static string Html(string cuerpo, string urlPixel)
    {
        var escapado = System.Net.WebUtility.HtmlEncode(cuerpo).Replace("\n", "<br>\n", StringComparison.Ordinal);

        return $"""
            <html><body style="font:14px/1.55 -apple-system,Segoe UI,Roboto,sans-serif;color:#231016">
            <p>{escapado}</p>
            <img src="{System.Net.WebUtility.HtmlEncode(urlPixel)}" width="1" height="1" alt="" aria-hidden="true" style="display:block">
            </body></html>
            """;
    }
}
