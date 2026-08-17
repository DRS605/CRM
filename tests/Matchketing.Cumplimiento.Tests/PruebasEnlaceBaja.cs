using FluentAssertions;
using Matchketing.Cumplimiento.Dominio;
using Xunit;

namespace Matchketing.Cumplimiento.Tests;

public sealed class PruebasEnlaceBaja
{
    private const string Secreto = "secreto-de-pruebas-largo-y-aburrido";
    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Contacto = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void El_token_dice_a_quien_senala()
    {
        var token = EnlaceBaja.Firmar(Empresa, Contacto, Secreto);

        var comprobado = EnlaceBaja.Comprobar(token, Secreto);

        comprobado.Exito.Should().BeTrue();
        comprobado.Valor.EmpresaId.Should().Be(Empresa);
        comprobado.Valor.ContactoId.Should().Be(Contacto);
    }

    [Fact]
    public void Con_otro_secreto_no_vale()
    {
        var token = EnlaceBaja.Firmar(Empresa, Contacto, Secreto);

        EnlaceBaja.Comprobar(token, "otro-secreto-distinto").Fallido.Should().BeTrue();
    }

    [Fact]
    public void Cambiar_la_carga_invalida_la_firma()
    {
        // Lo que se defiende: que nadie pueda dar de baja a un contacto ajeno cambiando un carácter
        // del enlace que le llegó a él.
        var token = EnlaceBaja.Firmar(Empresa, Contacto, Secreto);
        var partes = token.Split('.');
        var manipulado = (partes[0][0] == 'A' ? 'B' : 'A') + partes[0][1..] + "." + partes[1];

        EnlaceBaja.Comprobar(manipulado, Secreto).Error!.Codigo.Should().Be("baja.enlace_invalido");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("      ")]
    [InlineData("sin-punto")]
    [InlineData("demasiadas.partes.aqui")]
    [InlineData("no-es-base64!!.tampoco-esto!!")]
    [InlineData("QQ.QQ")]
    public void Un_token_mal_formado_falla_igual_que_uno_falso(string? token)
    {
        // Mismo código y mismo mensaje para los dos casos: distinguirlos solo ayudaría a quien esté
        // probando firmas a saber cuándo va por buen camino.
        EnlaceBaja.Comprobar(token, Secreto).Error!.Codigo.Should().Be("baja.enlace_invalido");
    }

    [Fact]
    public void El_token_es_estable_para_el_mismo_contacto()
    {
        // Importa de verdad: el mismo enlace tiene que valer en el correo de hoy y en el de dentro de
        // dos años, y tiene que poder pulsarse dos veces sin sorpresas.
        EnlaceBaja.Firmar(Empresa, Contacto, Secreto)
            .Should().Be(EnlaceBaja.Firmar(Empresa, Contacto, Secreto));
    }

    [Fact]
    public void Cada_contacto_tiene_su_token()
    {
        EnlaceBaja.Firmar(Empresa, Contacto, Secreto)
            .Should().NotBe(EnlaceBaja.Firmar(Empresa, Guid.NewGuid(), Secreto));
    }

    [Fact]
    public void El_token_cabe_en_una_url_sin_escapar_nada()
    {
        var token = EnlaceBaja.Firmar(Empresa, Contacto, Secreto);

        token.Should().MatchRegex("^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+$");
        Uri.EscapeDataString(token).Should().Be(token);
    }
}
