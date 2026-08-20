using FluentAssertions;
using Matchketing.Identidad.Dominio;
using Xunit;

namespace Matchketing.Identidad.Tests;

public sealed class PruebasInvitacion
{
    private static readonly Guid Empresa = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Quien = Guid.Parse("99999999-8888-7777-6666-555555555555");

    private static RelojFijo Reloj() => new(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void El_token_solo_existe_una_vez_y_de_el_se_guarda_la_huella()
    {
        var reloj = Reloj();

        var r = Invitacion.Crear(Empresa, "vicent@ribera.es", Rol.Comercial, Quien, reloj);

        r.Exito.Should().BeTrue();
        r.Valor.Token.Should().NotBeNullOrWhiteSpace();

        // Lo que queda en la entidad —y por tanto en la tabla— es un SHA-256, no el token: quien lea
        // una copia de seguridad no se lleva llaves de nadie.
        r.Valor.Invitacion.HuellaToken.Should().NotContain(r.Valor.Token);
        r.Valor.Invitacion.HuellaToken.Should().HaveLength(64);
        r.Valor.Invitacion.HuellaToken.Should().Be(Invitacion.Huella(r.Valor.Token));
    }

    [Fact]
    public void Dos_invitaciones_no_comparten_token()
    {
        var reloj = Reloj();

        var una = Invitacion.Crear(Empresa, "vicent@ribera.es", Rol.Comercial, Quien, reloj).Valor;
        var otra = Invitacion.Crear(Empresa, "vicent@ribera.es", Rol.Comercial, Quien, reloj).Valor;

        otra.Token.Should().NotBe(una.Token, "si dos llaves fueran iguales, una abriría la puerta de la otra");
    }

    [Fact]
    public void La_empresa_se_puede_leer_del_token_sin_tocar_la_base()
    {
        // Es lo que permite que el endpoint público fije el inquilino **antes** de consultar. Sin esto
        // la RLS de PostgreSQL no devolvería la fila y la invitación no se encontraría nunca.
        var token = Invitacion.Crear(Empresa, "vicent@ribera.es", Rol.Comercial, Quien, Reloj()).Valor.Token;

        Invitacion.EmpresaDelToken(token).Should().Be(Empresa);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corto")]
    [InlineData("...................... esto no es base64url")]
    public void Un_token_con_mala_forma_no_dice_ninguna_empresa(string? token)
    {
        Invitacion.EmpresaDelToken(token).Should().BeNull();
    }

    [Fact]
    public void Nace_viva_y_caduca_en_una_semana()
    {
        var reloj = Reloj();
        var invitacion = Invitacion.Crear(Empresa, "vicent@ribera.es", Rol.Comercial, Quien, reloj).Valor.Invitacion;

        invitacion.EstaViva(reloj).Should().BeTrue();
        invitacion.CaducaEn.Should().Be(reloj.AhoraUtc.AddDays(Invitacion.DiasDeVida));

        reloj.Avanzar(TimeSpan.FromDays(Invitacion.DiasDeVida) + TimeSpan.FromMinutes(1));
        invitacion.EstaViva(reloj).Should().BeFalse();

        // Y una caducada no se puede aceptar: es media invitación, no una invitación.
        invitacion.Aceptar(reloj).Error!.Codigo.Should().Be("invitacion.caducada");
    }

    [Fact]
    public void Se_usa_una_sola_vez()
    {
        var reloj = Reloj();
        var invitacion = Invitacion.Crear(Empresa, "vicent@ribera.es", Rol.Comercial, Quien, reloj).Valor.Invitacion;

        invitacion.Aceptar(reloj).Exito.Should().BeTrue();

        // El enlace se queda en un chat para siempre. Si valiera dos veces, cualquiera que lo
        // encontrara entraría en la empresa.
        invitacion.Aceptar(reloj).Error!.Codigo.Should().Be("invitacion.ya_aceptada");
        invitacion.EstaViva(reloj).Should().BeFalse();
    }

    [Fact]
    public void Una_retirada_ya_no_vale()
    {
        var reloj = Reloj();
        var invitacion = Invitacion.Crear(Empresa, "vicent@ribera.es", Rol.Comercial, Quien, reloj).Valor.Invitacion;

        invitacion.Retirar(reloj).Exito.Should().BeTrue();

        invitacion.EstaViva(reloj).Should().BeFalse();
        invitacion.Aceptar(reloj).Error!.Codigo.Should().Be("invitacion.retirada");
    }

    [Fact]
    public void Una_ya_aceptada_no_se_retira_porque_lo_que_hay_que_quitar_es_el_acceso()
    {
        var reloj = Reloj();
        var invitacion = Invitacion.Crear(Empresa, "vicent@ribera.es", Rol.Comercial, Quien, reloj).Valor.Invitacion;
        invitacion.Aceptar(reloj);

        var r = invitacion.Retirar(reloj);

        r.Error!.Codigo.Should().Be("invitacion.ya_aceptada");
        r.Error!.Mensaje.Should().Contain("quitar es el acceso",
            "retirar una invitación usada no le quita el acceso a nadie, y creerlo sería peor que no poder");
    }

    [Fact]
    public void Retirar_dos_veces_no_cambia_la_fecha()
    {
        var reloj = Reloj();
        var invitacion = Invitacion.Crear(Empresa, "vicent@ribera.es", Rol.Comercial, Quien, reloj).Valor.Invitacion;

        invitacion.Retirar(reloj);
        var cuando = invitacion.RetiradaEn;
        reloj.Avanzar(TimeSpan.FromHours(3));
        invitacion.Retirar(reloj).Exito.Should().BeTrue();

        invitacion.RetiradaEn.Should().Be(cuando, "la primera vez es la que cuenta");
    }

    [Fact]
    public void El_correo_se_normaliza_y_uno_inválido_se_rechaza()
    {
        var reloj = Reloj();

        Invitacion.Crear(Empresa, "  VICENT@Ribera.ES  ", Rol.Comercial, Quien, reloj)
            .Valor.Invitacion.Email.Should().Be("vicent@ribera.es");

        Invitacion.Crear(Empresa, "esto no es un correo", Rol.Comercial, Quien, reloj)
            .Fallido.Should().BeTrue();
    }

    [Fact]
    public void Un_rol_que_no_existe_no_se_invita()
    {
        // Llega como número por la API: un 7 en el JSON no puede convertirse en un rol sin permisos.
        Invitacion.Crear(Empresa, "vicent@ribera.es", (Rol)7, Quien, Reloj())
            .Error!.Codigo.Should().Be("invitacion.rol_invalido");
    }
}
