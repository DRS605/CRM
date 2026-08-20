using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Matchketing.Api.Comun;
using Matchketing.Avisos.Aplicacion;
using Matchketing.Avisos.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// El último tramo de Web Push: lo que sale de aquí **por HTTP** hacia el servicio de push.
///
/// Se prueba contra un servicio de push de mentira que se queda con la petición entera, porque contra
/// uno de verdad no se puede: haría falta una suscripción real de un navegador real. Lo que sí se
/// puede comprobar es todo lo que depende de nosotros, que es donde están los fallos que cuestan un
/// viernes entero: las tres cabeceras que exige el protocolo, el token VAPID firmado de forma que el
/// servicio lo acepte, la forma del cuerpo cifrado, y —sobre todo— qué se hace con cada código de
/// respuesta. Confundir un 410 con un 500 significa reintentar para siempre contra un móvil que ya no
/// existe; confundirlos al revés significa borrar la suscripción de alguien porque el servicio tuvo un
/// mal minuto.
/// </summary>
public sealed class PruebasEmisorWebPush
{
    private static readonly DateTimeOffset Viernes = new(2026, 8, 21, 16, 0, 0, TimeSpan.Zero);

    /// <summary>Una suscripción con claves válidas de verdad, como las que da un navegador.</summary>
    private static SuscripcionAviso Suscripcion(string endpoint = "https://fcm.googleapis.com/fcm/send/aBcD-123")
    {
        using var navegador = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var punto = navegador.PublicKey.ExportParameters().Q;

        var p256dh = new byte[65];
        p256dh[0] = 0x04;
        punto.X!.CopyTo(p256dh, 1 + (32 - punto.X!.Length));
        punto.Y!.CopyTo(p256dh, 33 + (32 - punto.Y!.Length));

        var r = SuscripcionAviso.Crear(
            Guid.NewGuid(), Guid.NewGuid(), endpoint,
            Base64Url.Codificar(p256dh), Base64Url.Codificar(RandomNumberGenerator.GetBytes(16)),
            new RelojDeMentira(Viernes));

        r.Exito.Should().BeTrue(r.Fallido ? r.Error!.Codigo : null);
        return r.Valor;
    }

    private static (EmisorWebPush Emisor, ServicioDePushDeMentira Servicio, ClavesVapid Claves) Montar(
        HttpStatusCode respuesta = HttpStatusCode.Created)
    {
        var claves = ClavesVapid.Generar("mailto:avisos@matchketing.es");
        var servicio = new ServicioDePushDeMentira(respuesta);
        var http = new HttpClient(servicio);
        return (new EmisorWebPush(http, claves, new RelojDeMentira(Viernes), NullLogger<EmisorWebPush>.Instance), servicio, claves);
    }

    [Fact]
    public async Task La_peticion_lleva_lo_que_exige_el_protocolo()
    {
        var (emisor, servicio, claves) = Montar();

        var r = await emisor.EnviarAsync(Suscripcion(), new Aviso("match.keting", "11 decisiones.", "/repaso", 11));

        r.Should().Be(ResultadoEnvio.Entregado);

        servicio.Metodo.Should().Be(HttpMethod.Post);
        servicio.Url.Should().Be("https://fcm.googleapis.com/fcm/send/aBcD-123");

        // El cuerpo va cifrado con aes128gcm, y el servicio lo reenvía tal cual: sin esta cabecera el
        // navegador no sabe cómo descifrarlo y descarta el aviso sin decir nada.
        servicio.CodificacionContenido.Should().Equal("aes128gcm");
        servicio.TipoContenido.Should().Be("application/octet-stream");

        // Cuatro horas: el aviso del viernes a las seis no sirve el lunes.
        servicio.Cabecera("TTL").Should().Be("14400");
        servicio.Cabecera("Urgency").Should().Be("high");

        // El esquema es `vapid`, con el token y la clave pública en el mismo valor.
        var autorizacion = servicio.Cabecera("Authorization");
        autorizacion.Should().StartWith("vapid t=");
        autorizacion.Should().Contain($", k={claves.Publica}");
    }

    [Fact]
    public async Task El_token_vapid_lo_puede_verificar_el_servicio_de_push()
    {
        var (emisor, servicio, claves) = Montar();

        await emisor.EnviarAsync(Suscripcion(), new Aviso("match.keting", "11 decisiones.", "/repaso", 11));

        var token = servicio.Cabecera("Authorization")!.Split("vapid t=")[1].Split(',')[0];
        var partes = token.Split('.');
        partes.Should().HaveCount(3);

        // Esto es exactamente lo que hace el servicio de push: rehacer la firma con la clave pública
        // que viene en `k=`. Si el algoritmo, el formato de la firma (P1363, no DER) o la audiencia no
        // cuadran, aquí se cae —y en producción se recibiría un 401 sin más explicación—.
        using var ec = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64Url.Descodificar(claves.Publica)![1..33],
                Y = Base64Url.Descodificar(claves.Publica)![33..],
            },
        });

        var firmado = Encoding.ASCII.GetBytes($"{partes[0]}.{partes[1]}");
        ec.VerifyData(firmado, Base64Url.Descodificar(partes[2])!, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation).Should().BeTrue();

        using var cabecera = JsonDocument.Parse(Base64Url.Descodificar(partes[0])!);
        cabecera.RootElement.GetProperty("alg").GetString().Should().Be("ES256");

        using var cuerpo = JsonDocument.Parse(Base64Url.Descodificar(partes[1])!);

        // La audiencia es **el origen, sin la ruta**. Meter la ruta dentro es el error clásico y se
        // paga con un 401 que no dice por qué.
        cuerpo.RootElement.GetProperty("aud").GetString().Should().Be("https://fcm.googleapis.com");
        cuerpo.RootElement.GetProperty("sub").GetString().Should().Be("mailto:avisos@matchketing.es");
        cuerpo.RootElement.GetProperty("exp").GetInt64().Should().BeGreaterThan(Viernes.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task El_cuerpo_tiene_la_forma_de_la_rfc_8188_y_no_lleva_el_texto_en_claro()
    {
        var (emisor, servicio, _) = Montar();

        await emisor.EnviarAsync(Suscripcion(), new Aviso("match.keting", "11 decisiones.", "/repaso", 11));

        var cuerpo = servicio.Cuerpo!;

        // Cabecera de la RFC 8188: 16 de sal, 4 del tamaño de registro, 1 de longitud de clave, y la
        // clave pública efímera de 65.
        cuerpo.Length.Should().BeGreaterThan(86);
        cuerpo[16..20].Should().Equal(0x00, 0x00, 0x10, 0x00); // 4096, en orden de red.
        cuerpo[20].Should().Be(65);
        cuerpo[21].Should().Be(0x04); // Punto sin comprimir.

        // Y lo que importa de todo esto: el aviso no viaja legible. El servicio de push es un tercero.
        Encoding.UTF8.GetString(cuerpo).Should().NotContain("decisiones");
        Encoding.UTF8.GetString(cuerpo).Should().NotContain("repaso");
    }

    [Theory]
    // 201 es lo normal; hay servicios que contestan 200, 202 o 204.
    [InlineData(HttpStatusCode.Created, ResultadoEnvio.Entregado)]
    [InlineData(HttpStatusCode.OK, ResultadoEnvio.Entregado)]
    [InlineData(HttpStatusCode.Accepted, ResultadoEnvio.Entregado)]
    [InlineData(HttpStatusCode.NoContent, ResultadoEnvio.Entregado)]
    // El aparato ya no está: hay que borrar la suscripción, no reintentarla.
    [InlineData(HttpStatusCode.NotFound, ResultadoEnvio.SuscripcionMuerta)]
    [InlineData(HttpStatusCode.Gone, ResultadoEnvio.SuscripcionMuerta)]
    // El servicio tuvo un mal minuto: se reintenta la semana que viene, no se borra a nadie.
    [InlineData(HttpStatusCode.TooManyRequests, ResultadoEnvio.FalloPasajero)]
    [InlineData(HttpStatusCode.InternalServerError, ResultadoEnvio.FalloPasajero)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ResultadoEnvio.FalloPasajero)]
    // Somos nosotros: el token, la carga, la cabecera. Ni se reintenta ni se borra; se registra.
    [InlineData(HttpStatusCode.BadRequest, ResultadoEnvio.Rechazado)]
    [InlineData(HttpStatusCode.Unauthorized, ResultadoEnvio.Rechazado)]
    [InlineData(HttpStatusCode.Forbidden, ResultadoEnvio.Rechazado)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ResultadoEnvio.Rechazado)]
    public async Task Cada_codigo_de_respuesta_se_traduce_a_lo_que_toca(HttpStatusCode codigo, ResultadoEnvio esperado)
    {
        var (emisor, _, _) = Montar(codigo);

        var r = await emisor.EnviarAsync(Suscripcion(), new Aviso("match.keting", "11 decisiones.", "/repaso", 11));

        r.Should().Be(esperado);
    }

    [Fact]
    public async Task Un_fallo_de_red_es_pasajero_y_no_se_lleva_la_suscripcion_por_delante()
    {
        var claves = ClavesVapid.Generar("mailto:avisos@matchketing.es");
        var http = new HttpClient(new ServicioDePushDeMentira(new HttpRequestException("sin ruta al servidor")));
        var emisor = new EmisorWebPush(http, claves, new RelojDeMentira(Viernes), NullLogger<EmisorWebPush>.Instance);

        var r = await emisor.EnviarAsync(Suscripcion(), new Aviso("match.keting", "11 decisiones.", "/repaso", 11));

        r.Should().Be(ResultadoEnvio.FalloPasajero);
    }

    [Fact]
    public async Task Si_se_agota_el_tiempo_tambien_es_pasajero()
    {
        var claves = ClavesVapid.Generar("mailto:avisos@matchketing.es");
        var http = new HttpClient(new ServicioDePushDeMentira(new TaskCanceledException("se agotó")));
        var emisor = new EmisorWebPush(http, claves, new RelojDeMentira(Viernes), NullLogger<EmisorWebPush>.Instance);

        var r = await emisor.EnviarAsync(Suscripcion(), new Aviso("match.keting", "11 decisiones.", "/repaso", 11));

        r.Should().Be(ResultadoEnvio.FalloPasajero);
    }

    [Fact]
    public async Task Cada_aviso_se_cifra_distinto_aunque_el_texto_sea_el_mismo()
    {
        var suscripcion = Suscripcion();
        var aviso = new Aviso("match.keting", "11 decisiones.", "/repaso", 11);

        var (uno, servicioUno, _) = Montar();
        var (otro, servicioOtro, _) = Montar();

        await uno.EnviarAsync(suscripcion, aviso);
        await otro.EnviarAsync(suscripcion, aviso);

        // Sal y clave efímera nuevas en cada envío. Repetirlas con la misma clave sería reutilizar el
        // nonce de AES-GCM, que es la forma conocida de romperlo.
        servicioUno.Cuerpo![..16].Should().NotEqual(servicioOtro.Cuerpo![..16]);
        servicioUno.Cuerpo![21..86].Should().NotEqual(servicioOtro.Cuerpo![21..86]);
    }
}

/// <summary>Un servicio de push que no manda nada: se queda con la petición y contesta lo que se le diga.</summary>
internal sealed class ServicioDePushDeMentira : HttpMessageHandler
{
    private readonly HttpStatusCode _codigo;
    private readonly Exception? _revienta;

    public ServicioDePushDeMentira(HttpStatusCode codigo) => _codigo = codigo;

    public ServicioDePushDeMentira(Exception revienta)
    {
        _codigo = HttpStatusCode.Created;
        _revienta = revienta;
    }

    public HttpMethod? Metodo { get; private set; }

    public string? Url { get; private set; }

    public byte[]? Cuerpo { get; private set; }

    public string? TipoContenido { get; private set; }

    public IReadOnlyList<string> CodificacionContenido { get; private set; } = [];

    private HttpRequestHeaders? Cabeceras { get; set; }

    public string? Cabecera(string nombre) =>
        Cabeceras is not null && Cabeceras.TryGetValues(nombre, out var valores) ? string.Join(", ", valores) : null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peticion);

        Metodo = peticion.Method;
        Url = peticion.RequestUri?.ToString();
        Cabeceras = peticion.Headers;
        Cuerpo = peticion.Content is null ? null : await peticion.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        TipoContenido = peticion.Content?.Headers.ContentType?.MediaType;
        CodificacionContenido = peticion.Content?.Headers.ContentEncoding.ToList() ?? [];

        if (_revienta is not null)
        {
            throw _revienta;
        }

        return new HttpResponseMessage(_codigo);
    }
}

internal sealed class RelojDeMentira(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; } = ahora;
}
