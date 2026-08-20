using System.Globalization;
using System.Net;
using System.Text;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Webhooks.Aplicacion;
using Matchketing.Webhooks.Dominio;

namespace Matchketing.Api.Comun;

/// <summary>
/// Hace el POST del webhook. Hace poco a propósito: la firma y los reintentos están en el dominio.
///
/// Lo que sí decide aquí es **qué cuenta como entregado**, y eso importa más de lo que parece: un 2xx
/// es un sí, y todo lo demás es un no que se reintenta. En particular un **404 se reintenta**, al
/// contrario que en los avisos push. Es la diferencia entre los dos: en push, un 404 significa que el
/// móvil ya no existe y no va a volver; en un webhook, casi siempre significa que el servicio del otro
/// lado está a medio desplegar y estará de vuelta en dos minutos.
///
/// Tampoco se sigue una redirección. Un 301 hacia otro dominio convertiría nuestra petición firmada en
/// una petición a un sitio que el cliente no configuró, y con el cuerpo entero dentro. Si la URL
/// cambió, se cambia en Ajustes.
/// </summary>
public sealed class EnviaWebhook(HttpClient http, IReloj reloj, ILogger<EnviaWebhook> registro) : IEnviaWebhook
{
    /// <summary>
    /// Lo que se espera a que el otro lado conteste. Diez segundos: quien recibe un webhook tiene que
    /// contestar rápido y hacer el trabajo después, y si tarda más de esto es que está haciendo el
    /// trabajo dentro de la petición, que es su problema y no debe ser el nuestro.
    /// </summary>
    public static TimeSpan Espera => TimeSpan.FromSeconds(10);

    public async Task<ResultadoEntrega> EnviarAsync(
        SuscripcionWebhook suscripcion, Entrega entrega, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(suscripcion);
        ArgumentNullException.ThrowIfNull(entrega);

        var peticion = new HttpRequestMessage(HttpMethod.Post, new Uri(suscripcion.Url))
        {
            Content = new StringContent(entrega.Cuerpo, Encoding.UTF8, "application/json"),
        };

        peticion.Headers.TryAddWithoutValidation(
            FirmaWebhook.Cabecera, FirmaWebhook.Cabeza(entrega.Cuerpo, suscripcion.Secreto, reloj.AhoraUtc));

        // El tipo y el identificador también en cabeceras, no solo en el cuerpo: así se puede encaminar
        // o descartar un repetido sin tener que analizar el JSON.
        peticion.Headers.TryAddWithoutValidation("X-Matchketing-Evento", TiposEvento.Texto(entrega.Tipo));
        peticion.Headers.TryAddWithoutValidation("X-Matchketing-Entrega", entrega.Id.ToString());
        peticion.Headers.TryAddWithoutValidation(
            "X-Matchketing-Intento", (entrega.Intentos + 1).ToString(CultureInfo.InvariantCulture));

        try
        {
            using var respuesta = await http.SendAsync(peticion, ct).ConfigureAwait(false);
            var codigo = (int)respuesta.StatusCode;

            if (respuesta.IsSuccessStatusCode)
            {
                return new ResultadoEntrega(true, codigo, null);
            }

            // El cuerpo de la respuesta **no** se guarda. Un error de un servidor ajeno puede traer
            // dentro cualquier cosa —una traza, una consulta SQL, una cabecera de autenticación— y
            // acabaría en nuestra tabla y en nuestra pantalla sin que nadie lo hubiera decidido.
            registro.LogWarning(
                "Webhook {Evento} rechazado con {Codigo} en {Suscripcion}.",
                TiposEvento.Texto(entrega.Tipo), codigo, suscripcion.Id);

            return new ResultadoEntrega(false, codigo, $"El servidor contestó {codigo} {Motivo(respuesta.StatusCode)}.");
        }
        catch (HttpRequestException ex)
        {
            return new ResultadoEntrega(false, null, $"No se pudo conectar: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ResultadoEntrega(false, null, $"No contestó en {Espera.TotalSeconds:0} segundos.");
        }
    }

    /// <summary>
    /// Una palabra para el código, en castellano. Va a una pantalla que mira quien montó la
    /// integración: «502 puerta de enlace incorrecta» le dice más que «502».
    /// </summary>
    private static string Motivo(HttpStatusCode codigo) => codigo switch
    {
        HttpStatusCode.BadRequest => "petición incorrecta",
        HttpStatusCode.Unauthorized => "no autorizado: revisa cómo comprueba la firma",
        HttpStatusCode.Forbidden => "prohibido",
        HttpStatusCode.NotFound => "no encontrado: ¿es esa la ruta?",
        HttpStatusCode.RequestTimeout => "tiempo agotado",
        HttpStatusCode.TooManyRequests => "demasiadas peticiones",
        HttpStatusCode.InternalServerError => "error del servidor",
        HttpStatusCode.BadGateway => "puerta de enlace incorrecta",
        HttpStatusCode.ServiceUnavailable => "servicio no disponible",
        HttpStatusCode.GatewayTimeout => "la puerta de enlace no contestó",
        _ => "sin más detalle",
    };
}
