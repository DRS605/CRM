using FluentAssertions;
using Matchketing.Identidad.Dominio;
using Xunit;

namespace Matchketing.Identidad.Tests;

public sealed class PruebasCambioContrasena
{
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero));

    private static string Hashear(string c) => "hash:" + c;

    private static bool Verificar(string enClaro, string hash) => hash == Hashear(enClaro);

    private static Usuario Alguien() =>
        Usuario.Registrar("marta@empresa.es", "Levante2026", "Marta Ruiz", Hashear, Reloj).Valor;

    [Fact]
    public void Con_la_actual_correcta_se_cambia()
    {
        var usuario = Alguien();

        usuario.CambiarContrasena("Levante2026", "Albufera2027", Verificar, Hashear, Reloj)
            .Exito.Should().BeTrue();

        Verificar("Albufera2027", usuario.HashContrasena).Should().BeTrue();
        Verificar("Levante2026", usuario.HashContrasena).Should().BeFalse();
    }

    [Fact]
    public void Sin_la_actual_correcta_no_se_cambia_nada()
    {
        var usuario = Alguien();
        var antes = usuario.HashContrasena;

        var r = usuario.CambiarContrasena("MeLoInvento1", "Albufera2027", Verificar, Hashear, Reloj);

        r.Error!.Codigo.Should().Be("contrasena.actual_incorrecta");
        r.Error!.Tipo.Should().Be(Nucleo.Resultados.TipoError.NoAutorizado);
        usuario.HashContrasena.Should().Be(antes);
    }

    [Fact]
    public void La_actual_vacia_tampoco_vale()
    {
        // El caso que importa: si `actual` llega nula porque el cliente no la mandó, no puede colar.
        Alguien().CambiarContrasena(null, "Albufera2027", Verificar, Hashear, Reloj)
            .Error!.Codigo.Should().Be("contrasena.actual_incorrecta");
    }

    [Theory]
    [InlineData("corta", "contrasena.corta")]
    [InlineData("solamenteletras", "contrasena.debil")]
    public void La_nueva_pasa_los_mismos_requisitos_que_al_registrarse(string nueva, string codigo)
    {
        Alguien().CambiarContrasena("Levante2026", nueva, Verificar, Hashear, Reloj)
            .Error!.Codigo.Should().Be(codigo);
    }

    [Fact]
    public void Poner_la_misma_de_antes_no_es_un_cambio()
    {
        Alguien().CambiarContrasena("Levante2026", "Levante2026", Verificar, Hashear, Reloj)
            .Error!.Codigo.Should().Be("contrasena.repetida");
    }
}
