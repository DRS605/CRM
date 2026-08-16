using FluentAssertions;
using Matchketing.Identidad.Dominio;
using Xunit;

namespace Matchketing.Identidad.Tests;

public sealed class PruebasUsuario
{
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

    private static string Hashear(string c) => "hash:" + c;

    [Fact]
    public void Registrar_normaliza_el_correo_a_minusculas()
    {
        var r = Usuario.Registrar("  Marta@Empresa.ES ", "Levante2026", "Marta Ruiz", Hashear, Reloj);

        r.Exito.Should().BeTrue();
        r.Valor.Email.Should().Be("marta@empresa.es");
    }

    [Fact]
    public void Registrar_recorta_el_nombre_y_deja_el_correo_sin_verificar()
    {
        var r = Usuario.Registrar("marta@empresa.es", "Levante2026", "  Marta Ruiz  ", Hashear, Reloj);

        r.Valor.Nombre.Should().Be("Marta Ruiz");
        r.Valor.EmailVerificado.Should().BeFalse();
        r.Valor.Activo.Should().BeTrue();
    }

    [Fact]
    public void Registrar_emite_el_evento_de_alta()
    {
        var r = Usuario.Registrar("marta@empresa.es", "Levante2026", "Marta Ruiz", Hashear, Reloj);

        r.Valor.Eventos.Should().ContainSingle().Which.Should().BeOfType<UsuarioRegistrado>();
    }

    [Theory]
    [InlineData(null, "email.vacio")]
    [InlineData("", "email.vacio")]
    [InlineData("sin-arroba", "email.invalido")]
    [InlineData("sin@dominio", "email.invalido")]
    public void Registrar_rechaza_correos_invalidos(string? email, string codigo)
    {
        var r = Usuario.Registrar(email, "Levante2026", "Marta Ruiz", Hashear, Reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be(codigo);
    }

    [Theory]
    [InlineData("corta1", "contrasena.corta")]
    [InlineData("solamenteletras", "contrasena.debil")]
    [InlineData("12345678", "contrasena.debil")]
    public void Registrar_rechaza_contrasenas_flojas(string contrasena, string codigo)
    {
        var r = Usuario.Registrar("marta@empresa.es", contrasena, "Marta Ruiz", Hashear, Reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be(codigo);
    }

    [Fact]
    public void Registrar_exige_nombre()
    {
        var r = Usuario.Registrar("marta@empresa.es", "Levante2026", "   ", Hashear, Reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("usuario.nombre_vacio");
    }

    [Fact]
    public void Leer_el_valor_de_un_resultado_fallido_es_un_error_de_programacion()
    {
        var r = Usuario.Registrar("no-vale", "Levante2026", "Marta", Hashear, Reloj);

        var leer = () => r.Valor;
        leer.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RegistrarAcceso_guarda_el_momento_del_ultimo_acceso()
    {
        var reloj = new RelojFijo(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));
        var usuario = Usuario.Registrar("marta@empresa.es", "Levante2026", "Marta", Hashear, reloj).Valor;

        usuario.UltimoAccesoEn.Should().BeNull();
        reloj.Avanzar(TimeSpan.FromHours(3));
        usuario.RegistrarAcceso(reloj);

        usuario.UltimoAccesoEn.Should().Be(reloj.AhoraUtc);
    }
}
