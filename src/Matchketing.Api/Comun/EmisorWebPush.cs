using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Matchketing.Avisos.Aplicacion;
using Matchketing.Avisos.Dominio;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Api.Comun;

/// <summary>
/// Manda el aviso al servicio de push por HTTP. Hace muy poco a propósito: el cifrado y el token
/// vienen del dominio, ya probados; esto pone tres cabeceras y traduce el código de respuesta.
///
/// La traducción del código es lo delicado. Un 410 tratado como fallo pasajero deja reintentando para
/// siempre contra un móvil que ya no existe, y eso es lo que hace que un servicio de push empiece a
/// limitar todo lo que mandas; un 503 tratado como suscripción muerta borra los avisos de alguien
/// porque el servicio tuvo un mal minuto. Está cubierto código por código en
/// <c>PruebasEmisorWebPush</c>, contra un servicio de push de mentira que se queda con la petición
/// entera: cabeceras, token VAPID verificado con su clave pública, y forma del cuerpo cifrado.
/// </summary>
public sealed class EmisorWebPush(
    HttpClient http, ClavesVapid claves, IReloj reloj, ILogger<EmisorWebPush> registro) : IEmisorAvisos
{
    /// <summary>
    /// Cuánto guarda el servicio el aviso si el móvil está apagado. Cuatro horas: el aviso del viernes
    /// a las seis sirve el viernes por la noche y ya no sirve el lunes, cuando la pila es otra.
    /// </summary>
    private const int SegundosDeVida = 4 * 60 * 60;

    public async Task<ResultadoEnvio> EnviarAsync(SuscripcionAviso suscripcion, Aviso aviso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(suscripcion);
        ArgumentNullException.ThrowIfNull(aviso);

        var cifrado = CifradoWebPush.Cifrar(JsonSerializer.Serialize(aviso), suscripcion.ClavePublica, suscripcion.Secreto);
        if (cifrado.Fallido)
        {
            // Las claves se validan al suscribirse, así que llegar aquí significa que la fila está
            // corrupta: no se reintenta, se tira la suscripción.
            registro.LogWarning("Aviso no cifrable para {Endpoint}: {Codigo}", suscripcion.Endpoint, cifrado.Error!.Codigo);
            return ResultadoEnvio.SuscripcionMuerta;
        }

        var endpoint = new Uri(suscripcion.Endpoint);
        var peticion = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(cifrado.Valor),
        };

        peticion.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        peticion.Content.Headers.ContentEncoding.Add("aes128gcm");
        peticion.Headers.TryAddWithoutValidation("Authorization", $"vapid t={claves.Token(endpoint, reloj.AhoraUtc)}, k={claves.Publica}");
        peticion.Headers.TryAddWithoutValidation("TTL", SegundosDeVida.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // `high` hace que el móvil lo muestre aunque esté ahorrando batería. Es un aviso a la semana:
        // si algo merece despertar la pantalla, es esto.
        peticion.Headers.TryAddWithoutValidation("Urgency", "high");

        try
        {
            using var respuesta = await http.SendAsync(peticion, ct).ConfigureAwait(false);

            return respuesta.StatusCode switch
            {
                // 201 es lo normal; algunos servicios devuelven 200 o 202.
                HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.NoContent =>
                    ResultadoEnvio.Entregado,

                // La suscripción ya no existe: el móvil se cambió, el navegador la revocó, la persona
                // desinstaló. Insistir contra endpoints muertos es lo que hace que un servicio de push
                // empiece a limitar todo lo que mandas.
                HttpStatusCode.NotFound or HttpStatusCode.Gone => ResultadoEnvio.SuscripcionMuerta,

                HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError => ResultadoEnvio.FalloPasajero,

                _ => Rechazado(respuesta.StatusCode, suscripcion.Endpoint),
            };
        }
        catch (HttpRequestException ex)
        {
            registro.LogWarning(ex, "Aviso no entregado a {Endpoint}: fallo de red.", suscripcion.Endpoint);
            return ResultadoEnvio.FalloPasajero;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            registro.LogWarning("Aviso no entregado a {Endpoint}: se agotó el tiempo.", suscripcion.Endpoint);
            return ResultadoEnvio.FalloPasajero;
        }
    }

    private ResultadoEnvio Rechazado(HttpStatusCode codigo, string endpoint)
    {
        // 401 y 403 son casi siempre el token VAPID: la clave cambiada, la audiencia con la ruta
        // dentro, o la firma en DER. Se registra con el código porque el servicio no explica nada más.
        registro.LogError("Aviso rechazado con {Codigo} en {Endpoint}. Revisa las claves VAPID.", (int)codigo, endpoint);
        return ResultadoEnvio.Rechazado;
    }
}
