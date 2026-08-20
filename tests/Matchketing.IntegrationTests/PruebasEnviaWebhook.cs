using System.Globalization;
using System.Net;
using System.Text;
using FluentAssertions;
using Matchketing.Api.Comun;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Webhooks.Aplicacion;
using Matchketing.Webhooks.Dominio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// El tramo HTTP de los webhooks, contra un receptor de mentira que se queda con la petición entera.
///
/// Lo que se comprueba aquí es lo que decide si la integración de alguien funciona o no: que la firma
/// se pueda verificar **como la va a verificar quien la reciba**, que las cabeceras estén, y que cada
/// código de respuesta se traduzca a lo que toca. Esa traducción es la que más silenciosamente se
/// rompe: un 404 tratado como definitivo pierde el evento de un servicio que estaba a medio desplegar.
/// </summary>
public sealed class PruebasEnviaWebhook
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static SuscripcionWebhook Suscripcion() =>
        SuscripcionWebhook.Crear(
            Guid.NewGuid(), "https://erp.ejemplo.es/hooks/mk", "Pedidos al ERP",
            [TipoEvento.OportunidadGanada], new RelojDeMentira(Ahora)).Valor;

    private static Entrega Entrega(string cuerpo = """{"id":"x","tipo":"oportunidad.ganada"}""") =>
        Matchketing.Webhooks.Dominio.Entrega.Crear(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoEvento.OportunidadGanada, cuerpo,
            new RelojDeMentira(Ahora));

    private static (EnviaWebhook Emisor, ReceptorDeMentira Receptor) Montar(HttpStatusCode codigo = HttpStatusCode.OK)
    {
        var receptor = new ReceptorDeMentira(codigo);
        return (new EnviaWebhook(new HttpClient(receptor), new RelojDeMentira(Ahora), NullLogger<EnviaWebhook>.Instance),
            receptor);
    }

    [Fact]
    public async Task Quien_recibe_puede_verificar_la_firma_de_lo_que_le_llega()
    {
        var (emisor, receptor) = Montar();
        var suscripcion = Suscripcion();
        var entrega = Entrega();

        (await emisor.EnviarAsync(suscripcion, entrega)).Salio.Should().BeTrue();

        // Esto es literalmente lo que hará el servidor del cliente: coger la cabecera, el cuerpo y su
        // secreto, y rehacer la firma. Si esta prueba pasa, la integración de alguien funciona.
        FirmaWebhook.Comprobar(
            receptor.Cabecera(FirmaWebhook.Cabecera), receptor.Cuerpo!, suscripcion.Secreto, Ahora)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Con_el_secreto_de_otro_no_se_verifica()
    {
        var (emisor, receptor) = Montar();

        await emisor.EnviarAsync(Suscripcion(), Entrega());

        FirmaWebhook.Comprobar(
            receptor.Cabecera(FirmaWebhook.Cabecera), receptor.Cuerpo!, FirmaWebhook.SecretoNuevo(), Ahora)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Van_las_cabeceras_que_permiten_encaminar_y_descartar_repetidos()
    {
        var (emisor, receptor) = Montar();
        var entrega = Entrega();

        await emisor.EnviarAsync(Suscripcion(), entrega);

        receptor.Metodo.Should().Be(HttpMethod.Post);
        receptor.TipoContenido.Should().Be("application/json");

        // El tipo y el identificador también en cabeceras, no solo en el cuerpo: así se puede encaminar
        // o descartar un repetido sin analizar el JSON.
        receptor.Cabecera("X-Matchketing-Evento").Should().Be("oportunidad.ganada");
        receptor.Cabecera("X-Matchketing-Entrega").Should().Be(entrega.Id.ToString());
        receptor.Cabecera("X-Matchketing-Intento").Should().Be("1");
    }

    [Fact]
    public async Task El_numero_de_intento_va_subiendo()
    {
        var reloj = new RelojDeMentira(Ahora);
        var (emisor, receptor) = Montar();
        var entrega = Entrega();
        entrega.NoSalio(500, "error", reloj);
        entrega.NoSalio(500, "error", reloj);

        await emisor.EnviarAsync(Suscripcion(), entrega);

        // Le sirve a quien recibe para distinguir «me lo mandan por primera vez» de «esto es un
        // reintento y quizá ya lo procesé».
        receptor.Cabecera("X-Matchketing-Intento").Should().Be("3");
    }

    [Fact]
    public async Task El_cuerpo_llega_tal_cual_se_congelo()
    {
        var (emisor, receptor) = Montar();
        var cuerpo = """{"id":"x","tipo":"oportunidad.ganada","datos":{"importe":42000.50}}""";

        await emisor.EnviarAsync(Suscripcion(), Entrega(cuerpo));

        // Byte a byte. Si el emisor volviera a serializar el objeto, la firma dejaría de cuadrar por un
        // espacio o por el orden de las claves, y el fallo sería imposible de encontrar.
        receptor.Cuerpo.Should().Be(cuerpo);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.Accepted, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    // Todo lo que no sea 2xx se reintenta, **incluido el 404**. Es la diferencia con los avisos push:
    // allí un 404 significa que el móvil ya no existe; aquí, casi siempre, que el servicio del otro
    // lado está a medio desplegar y vuelve en dos minutos.
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.BadGateway, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task Solo_un_2xx_cuenta_como_entregado(HttpStatusCode codigo, bool esperado)
    {
        var (emisor, _) = Montar(codigo);

        var r = await emisor.EnviarAsync(Suscripcion(), Entrega());

        r.Salio.Should().Be(esperado);
        r.Codigo.Should().Be((int)codigo);
    }

    [Fact]
    public async Task Un_rechazo_se_explica_en_castellano_y_con_el_codigo()
    {
        var (emisor, _) = Montar(HttpStatusCode.BadGateway);

        var r = await emisor.EnviarAsync(Suscripcion(), Entrega());

        // Va a una pantalla que mira quien montó la integración: «502 puerta de enlace incorrecta» le
        // dice qué mirar; «502» le dice que busque en internet.
        r.Fallo.Should().Contain("502").And.Contain("puerta de enlace");
    }

    [Fact]
    public async Task Un_401_apunta_a_la_firma_porque_es_lo_que_es_casi_siempre()
    {
        var (emisor, _) = Montar(HttpStatusCode.Unauthorized);

        var r = await emisor.EnviarAsync(Suscripcion(), Entrega());

        r.Fallo.Should().Contain("firma");
    }

    [Fact]
    public async Task No_se_guarda_nada_del_cuerpo_de_la_respuesta()
    {
        var receptor = new ReceptorDeMentira(HttpStatusCode.InternalServerError)
        {
            Devuelve = "Traceback: SELECT * FROM usuarios WHERE token='abc123'",
        };
        var emisor = new EnviaWebhook(
            new HttpClient(receptor), new RelojDeMentira(Ahora), NullLogger<EnviaWebhook>.Instance);

        var r = await emisor.EnviarAsync(Suscripcion(), Entrega());

        // El error de un servidor ajeno puede traer dentro cualquier cosa —una traza, una consulta, una
        // credencial— y acabaría en nuestra tabla y en nuestra pantalla sin que nadie lo decidiera.
        r.Fallo.Should().NotContain("token").And.NotContain("SELECT").And.NotContain("abc123");
    }

    [Fact]
    public async Task Un_fallo_de_red_no_trae_codigo_pero_si_explicacion()
    {
        var emisor = new EnviaWebhook(
            new HttpClient(new ReceptorDeMentira(new HttpRequestException("sin ruta al servidor"))),
            new RelojDeMentira(Ahora), NullLogger<EnviaWebhook>.Instance);

        var r = await emisor.EnviarAsync(Suscripcion(), Entrega());

        r.Salio.Should().BeFalse();
        r.Codigo.Should().BeNull();
        r.Fallo.Should().Contain("conectar");
    }

    [Fact]
    public async Task Si_no_contesta_a_tiempo_se_dice_cuanto_se_esperó()
    {
        var emisor = new EnviaWebhook(
            new HttpClient(new ReceptorDeMentira(new TaskCanceledException("agotado"))),
            new RelojDeMentira(Ahora), NullLogger<EnviaWebhook>.Instance);

        var r = await emisor.EnviarAsync(Suscripcion(), Entrega());

        r.Salio.Should().BeFalse();
        r.Fallo.Should().Contain(EnviaWebhook.Espera.TotalSeconds.ToString("0", CultureInfo.InvariantCulture));
    }
}

/// <summary>Un receptor de webhooks que no existe: se queda con la petición y contesta lo que se le diga.</summary>
internal sealed class ReceptorDeMentira : HttpMessageHandler
{
    private readonly HttpStatusCode codigo;
    private readonly Exception? revienta;

    public ReceptorDeMentira(HttpStatusCode codigo) => this.codigo = codigo;

    public ReceptorDeMentira(Exception revienta)
    {
        codigo = HttpStatusCode.OK;
        this.revienta = revienta;
    }

    public string Devuelve { get; init; } = string.Empty;

    public HttpMethod? Metodo { get; private set; }

    public string? Cuerpo { get; private set; }

    public string? TipoContenido { get; private set; }

    private HttpRequestMessage? Peticion { get; set; }

    public string? Cabecera(string nombre)
    {
        if (Peticion is null)
        {
            return null;
        }

        return Peticion.Headers.TryGetValues(nombre, out var valores) ? string.Join(", ", valores) : null;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(peticion);

        Metodo = peticion.Method;
        Peticion = peticion;
        TipoContenido = peticion.Content?.Headers.ContentType?.MediaType;
        Cuerpo = peticion.Content is null ? null : await peticion.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (revienta is not null)
        {
            throw revienta;
        }

        return new HttpResponseMessage(codigo) { Content = new StringContent(Devuelve, Encoding.UTF8, "text/plain") };
    }
}
