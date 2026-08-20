using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Matchketing.Webhooks.Dominio;
using Xunit;

namespace Matchketing.Webhooks.Tests;

/// <summary>
/// La firma es lo único que separa «un webhook nuestro» de «un POST de cualquiera». Si esto está mal,
/// todo lo demás del módulo sobra: quien reciba el webhook creerá que viene de nosotros y hará lo que
/// diga —emitir un pedido, por ejemplo—.
/// </summary>
public sealed class PruebasFirma
{
    private const string Secreto = "whsec_1234567890abcdef";
    private const string Cuerpo = """{"id":"a","tipo":"oportunidad.ganada","datos":{"importe":42000}}""";

    private static readonly DateTimeOffset Ahora = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Quien_recibe_puede_comprobar_lo_que_firmamos()
    {
        var cabecera = FirmaWebhook.Cabeza(Cuerpo, Secreto, Ahora);

        FirmaWebhook.Comprobar(cabecera, Cuerpo, Secreto, Ahora).Should().BeTrue();
    }

    [Fact]
    public void La_cabecera_tiene_el_formato_que_espera_quien_la_lee()
    {
        var cabecera = FirmaWebhook.Cabeza(Cuerpo, Secreto, Ahora);

        // `t=<segundos>,v1=<hex>`. Se copia el formato que ya usa medio internet para que quien lo
        // reciba lo reconozca sin leerse nuestra documentación.
        cabecera.Should().StartWith("t=1787227200,v1=");
        cabecera.Split("v1=")[1].Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Tocar_un_solo_byte_del_cuerpo_invalida_la_firma()
    {
        var cabecera = FirmaWebhook.Cabeza(Cuerpo, Secreto, Ahora);

        // 42000 € pasan a 420000 €. Es el ataque que importa: no cambiar el evento, cambiar la cifra.
        var manipulado = Cuerpo.Replace("42000", "420000", StringComparison.Ordinal);

        FirmaWebhook.Comprobar(cabecera, manipulado, Secreto, Ahora).Should().BeFalse();
    }

    [Fact]
    public void Con_otro_secreto_no_cuadra()
    {
        var cabecera = FirmaWebhook.Cabeza(Cuerpo, Secreto, Ahora);

        FirmaWebhook.Comprobar(cabecera, Cuerpo, "whsec_otro", Ahora).Should().BeFalse();
    }

    [Fact]
    public void Cambiar_la_marca_de_tiempo_invalida_la_firma()
    {
        var cabecera = FirmaWebhook.Cabeza(Cuerpo, Secreto, Ahora);
        var firma = cabecera.Split("v1=")[1];

        // Esto es el motivo de meter la marca de tiempo **dentro** de lo firmado. Si solo se firmara el
        // cuerpo, bastaría con reescribir la `t` para que una entrega vieja pasara por nueva.
        var conOtraHora = $"t={Ahora.AddMinutes(1).ToUnixTimeSeconds()},v1={firma}";

        FirmaWebhook.Comprobar(conOtraHora, Cuerpo, Secreto, Ahora.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void Una_entrega_de_hace_una_hora_no_vale_aunque_este_bien_firmada()
    {
        var cabecera = FirmaWebhook.Cabeza(Cuerpo, Secreto, Ahora);

        // Reenviar una entrega interceptada es el ataque más fácil que hay, porque no hace falta
        // romper nada: basta con guardarla y repetirla. La ventana la corta.
        FirmaWebhook.Comprobar(cabecera, Cuerpo, Secreto, Ahora.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void Dentro_de_la_tolerancia_si_vale()
    {
        var cabecera = FirmaWebhook.Cabeza(Cuerpo, Secreto, Ahora);

        // Dos relojes nunca están exactamente iguales, y rechazar por dos segundos de desfase sería
        // un módulo que no funciona en ningún sitio real.
        FirmaWebhook.Comprobar(cabecera, Cuerpo, Secreto, Ahora.AddMinutes(4)).Should().BeTrue();
        FirmaWebhook.Comprobar(cabecera, Cuerpo, Secreto, Ahora.AddMinutes(-4)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("basura")]
    [InlineData("t=1787227200")]                       // sin firma
    [InlineData("v1=abcd")]                            // sin marca de tiempo
    [InlineData("t=no-es-un-numero,v1=abcd")]
    [InlineData("=,=")]
    public void Una_cabecera_que_no_se_entiende_es_un_no(string? cabecera)
    {
        // Nunca una excepción: quien comprueba esto lo hace con lo que le manden, y lo que le manden
        // puede ser cualquier cosa.
        FirmaWebhook.Comprobar(cabecera, Cuerpo, Secreto, Ahora).Should().BeFalse();
    }

    [Fact]
    public void La_firma_se_lee_igual_en_mayusculas()
    {
        var cabecera = FirmaWebhook.Cabeza(Cuerpo, Secreto, Ahora).ToUpperInvariant()
            .Replace("T=", "t=", StringComparison.Ordinal)
            .Replace("V1=", "v1=", StringComparison.Ordinal);

        // Hay bibliotecas de HMAC que devuelven el hexadecimal en mayúsculas. Rechazar por eso sería
        // una tarde perdida de quien monta la integración, y no protege de nada.
        FirmaWebhook.Comprobar(cabecera, Cuerpo, Secreto, Ahora).Should().BeTrue();
    }

    [Fact]
    public void Un_secreto_nuevo_se_reconoce_y_no_se_repite()
    {
        var uno = FirmaWebhook.SecretoNuevo();
        var otro = FirmaWebhook.SecretoNuevo();

        uno.Should().StartWith("whsec_", "el prefijo hace que se reconozca de un vistazo en un registro");
        uno.Should().HaveLength(6 + 64, "32 bytes en hexadecimal");
        uno.Should().NotBe(otro);
    }

    [Fact]
    public void Lo_firmado_es_la_marca_de_tiempo_el_punto_y_el_cuerpo()
    {
        var cabecera = FirmaWebhook.Cabeza(Cuerpo, Secreto, Ahora);

        // Se rehace la firma desde cero, como la rehará quien reciba esto leyendo la documentación. Si
        // algún día se cambia lo que se firma, esta prueba se cae y hay que cambiar la documentación
        // con ella: es un contrato público, no un detalle interno.
        var esperada = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secreto),
            Encoding.UTF8.GetBytes($"{Ahora.ToUnixTimeSeconds()}.{Cuerpo}"))).ToLowerInvariant();

        cabecera.Should().EndWith("v1=" + esperada);
    }
}
