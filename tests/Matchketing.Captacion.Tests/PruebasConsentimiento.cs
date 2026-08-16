using FluentAssertions;
using Matchketing.Cumplimiento.Dominio;
using Matchketing.Nucleo.Tiempo;
using Xunit;

namespace Matchketing.Captacion.Tests;

public sealed class PruebasConsentimiento
{
    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Contacto = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static IReloj Reloj() => new RelojFijo(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

    private static Consentimiento Otorgar() => Consentimiento.Otorgar(
        Empresa, Contacto, FinalidadConsentimiento.AtenderSolicitud, BaseLegal.Consentimiento,
        "formulario web", "Acepto que me contactéis.", "88.1.2.3", "Mozilla/5.0", Reloj()).Valor;

    [Fact]
    public void Un_consentimiento_guarda_la_prueba_de_como_se_dio()
    {
        var c = Otorgar();

        c.Canal.Should().Be("formulario web");
        c.TextoAceptado.Should().Be("Acepto que me contactéis.");
        c.Ip.Should().Be("88.1.2.3");
        c.Agente.Should().Be("Mozilla/5.0");
        c.OtorgadoEn.Should().Be(Reloj().AhoraUtc);
        c.Vigente.Should().BeTrue();
    }

    [Fact]
    public void Sin_canal_no_hay_consentimiento_valido()
    {
        Consentimiento.Otorgar(Empresa, Contacto, FinalidadConsentimiento.Comercial, BaseLegal.Consentimiento, "  ", null, null, null, Reloj())
            .Error!.Codigo.Should().Be("consentimiento.sin_canal");
    }

    [Fact]
    public void Retirarlo_lo_deja_fuera_de_vigor()
    {
        var c = Otorgar();

        c.Retirar(Reloj()).Exito.Should().BeTrue();

        c.Vigente.Should().BeFalse();
        c.RetiradoEn.Should().NotBeNull();
    }

    [Fact]
    public void No_se_retira_dos_veces()
    {
        var c = Otorgar();
        c.Retirar(Reloj());

        c.Retirar(Reloj()).Error!.Codigo.Should().Be("consentimiento.ya_retirado");
    }

    [Fact]
    public void Atender_una_solicitud_no_es_lo_mismo_que_poder_mandarle_promociones()
    {
        // Dos finalidades distintas y dos registros distintos: un consentimiento sirve para lo que
        // dice y nada más.
        var atender = Otorgar();
        var comercial = Consentimiento.Otorgar(
            Empresa, Contacto, FinalidadConsentimiento.Comercial, BaseLegal.Consentimiento,
            "formulario web", "Acepto recibir ofertas.", null, null, Reloj()).Valor;

        atender.Finalidad.Should().NotBe(comercial.Finalidad);
        atender.Id.Should().NotBe(comercial.Id);
    }
}
