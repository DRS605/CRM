using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Matchketing.Avisos.Dominio;
using Matchketing.Nucleo.Comun;
using Xunit;

namespace Matchketing.Avisos.Tests;

/// <summary>
/// El token con el que este servidor se presenta al servicio de push (RFC 8292).
///
/// Todo lo que se comprueba aquí produce, si falla, **un 401 del servicio de push sin explicación**.
/// No hay ningún mensaje de error que diga «la firma iba en DER» o «la audiencia llevaba la ruta».
/// </summary>
public sealed class PruebasVapid
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 21, 16, 0, 0, TimeSpan.Zero);
    private static readonly Uri Endpoint = new("https://fcm.googleapis.com/fcm/send/abc123?token=xyz");

    private static ClavesVapid Claves() => ClavesVapid.Generar("mailto:avisos@matchketing.es");

    [Fact]
    public void Las_claves_generadas_tienen_el_formato_que_espera_el_navegador()
    {
        var claves = Claves();

        var publica = Base64Url.Descodificar(claves.Publica)!;
        publica.Should().HaveCount(65);
        publica[0].Should().Be(0x04, "el navegador solo acepta el punto sin comprimir");
        Base64Url.Descodificar(claves.Privada).Should().HaveCount(32);

        // base64url y nada más: la clave viaja en una URL y en JavaScript.
        claves.Publica.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Un_par_generado_se_puede_volver_a_cargar()
    {
        var claves = Claves();

        ClavesVapid.De(claves.Publica, claves.Privada, claves.Sujeto).Exito.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "vapid.publica_invalida")]
    [InlineData("QQ", "vapid.publica_invalida")]
    public void Una_clave_publica_que_no_vale_se_rechaza(string? publica, string codigo)
    {
        ClavesVapid.De(publica, Claves().Privada, "mailto:a@b.es").Error!.Codigo.Should().Be(codigo);
    }

    [Fact]
    public void Una_clave_privada_que_no_mide_treinta_y_dos_bytes_se_rechaza()
    {
        ClavesVapid.De(Claves().Publica, "QQ", "mailto:a@b.es").Error!.Codigo.Should().Be("vapid.privada_invalida");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("avisos@matchketing.es")]
    [InlineData("http://matchketing.es")]
    public void El_sujeto_tiene_que_ser_un_mailto_o_una_direccion_https(string? sujeto)
    {
        var claves = Claves();

        ClavesVapid.De(claves.Publica, claves.Privada, sujeto).Error!.Codigo.Should().Be("vapid.sujeto_invalido");
    }

    [Fact]
    public void El_token_tiene_tres_partes_y_dice_que_es_ES256()
    {
        var partes = Claves().Token(Endpoint, Ahora).Split('.');

        partes.Should().HaveCount(3);

        var cabecera = JsonDocument.Parse(Base64Url.Descodificar(partes[0])!).RootElement;
        cabecera.GetProperty("typ").GetString().Should().Be("JWT");
        cabecera.GetProperty("alg").GetString().Should().Be("ES256");
    }

    [Fact]
    public void La_audiencia_es_solo_el_origen_del_endpoint()
    {
        // **El error más fácil de cometer**: el endpoint es una URL larga con ruta y parámetros, y el
        // RFC exige solo esquema y host. Con la ruta dentro, el servicio devuelve 401.
        var token = Claves().Token(Endpoint, Ahora);
        var cuerpo = JsonDocument.Parse(Base64Url.Descodificar(token.Split('.')[1])!).RootElement;

        cuerpo.GetProperty("aud").GetString().Should().Be("https://fcm.googleapis.com");
        cuerpo.GetProperty("sub").GetString().Should().Be("mailto:avisos@matchketing.es");
    }

    [Fact]
    public void Caduca_a_las_doce_horas_y_no_a_las_veinticuatro()
    {
        // El máximo que admite el RFC es un día; la mitad deja margen si el reloj del servidor baila.
        var token = Claves().Token(Endpoint, Ahora);
        var cuerpo = JsonDocument.Parse(Base64Url.Descodificar(token.Split('.')[1])!).RootElement;

        cuerpo.GetProperty("exp").GetInt64().Should().Be(Ahora.AddHours(12).ToUnixTimeSeconds());
    }

    [Fact]
    public void La_firma_va_en_crudo_de_sesenta_y_cuatro_bytes_y_no_en_DER()
    {
        // Un JWT ES256 exige R||S en 64 bytes fijos. .NET firma en DER por defecto, que aquí no vale.
        var partes = Claves().Token(Endpoint, Ahora).Split('.');

        Base64Url.Descodificar(partes[2]).Should().HaveCount(64);
    }

    [Fact]
    public void La_firma_la_verifica_la_clave_publica_del_propio_token()
    {
        // Comprobación independiente: se reconstruye la clave solo con la parte pública que se le da al
        // navegador, y con ella se verifica lo que firmó la privada.
        var claves = Claves();
        var token = claves.Token(Endpoint, Ahora);
        var partes = token.Split('.');

        var punto = Base64Url.Descodificar(claves.Publica)!;
        using var soloPublica = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = punto[1..33], Y = punto[33..65] },
        });

        soloPublica.VerifyData(
            Encoding.UTF8.GetBytes($"{partes[0]}.{partes[1]}"),
            Base64Url.Descodificar(partes[2])!,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            .Should().BeTrue();
    }

    [Fact]
    public void Cada_endpoint_lleva_su_propio_token()
    {
        // La audiencia va dentro de la firma, así que un token no se puede reutilizar con otro servicio.
        var claves = Claves();

        claves.Token(Endpoint, Ahora)
            .Should().NotBe(claves.Token(new Uri("https://updates.push.services.mozilla.com/wpush/v2/abc"), Ahora));
    }
}
