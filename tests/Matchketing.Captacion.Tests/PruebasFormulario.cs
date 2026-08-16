using FluentAssertions;
using Matchketing.Captacion.Dominio;
using Matchketing.Nucleo.Tiempo;
using Xunit;

namespace Matchketing.Captacion.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc => ahora;
}

public sealed class PruebasFormulario
{
    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static IReloj Reloj() => new RelojFijo(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

    private static Matchketing.Captacion.Dominio.Formulario Crear(string? gracias = null) =>
        Matchketing.Captacion.Dominio.Formulario.Crear(
            Empresa, "Presupuesto web", "Acepto que me contactéis para responder a mi solicitud.",
            true, false, true, gracias, null, Reloj()).Valor;

    [Fact]
    public void Un_formulario_nuevo_trae_clave_propia_y_origen_por_defecto()
    {
        var f = Crear();

        f.Clave.Should().HaveLength(Matchketing.Captacion.Dominio.Formulario.LongitudClave);
        f.Origen.Should().Be("formulario web");
        f.Activo.Should().BeTrue();
    }

    [Fact]
    public void Dos_formularios_nunca_comparten_clave()
    {
        var claves = Enumerable.Range(0, 50).Select(_ => Crear().Clave).ToList();

        claves.Distinct().Should().HaveCount(50);
    }

    [Fact]
    public void La_clave_no_usa_caracteres_que_se_confunden_al_dictarla()
    {
        var clave = Crear().Clave;

        clave.Should().NotContainAny("l", "o", "0", "1");
        clave.Should().MatchRegex("^[a-z2-9]+$");
    }

    [Theory]
    [InlineData(null, "formulario.nombre_vacio")]
    [InlineData("   ", "formulario.nombre_vacio")]
    public void El_formulario_necesita_nombre(string? nombre, string codigo)
    {
        Matchketing.Captacion.Dominio.Formulario
            .Crear(Empresa, nombre, "Acepto.", false, false, false, null, null, Reloj())
            .Error!.Codigo.Should().Be(codigo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Sin_texto_de_consentimiento_no_hay_formulario(string? texto)
    {
        // Sin el texto no hay prueba de qué consintió la persona, y sin prueba no se le puede
        // escribir nada después.
        Matchketing.Captacion.Dominio.Formulario
            .Crear(Empresa, "Presupuesto", texto, false, false, false, null, null, Reloj())
            .Error!.Codigo.Should().Be("formulario.sin_consentimiento");
    }

    [Theory]
    [InlineData("https://ribera.es/gracias")]
    [InlineData("http://ribera.es/gracias")]
    public void La_pagina_de_gracias_admite_direcciones_web(string url)
    {
        Matchketing.Captacion.Dominio.Formulario
            .Crear(Empresa, "P", "Acepto.", false, false, false, url, null, Reloj())
            .Exito.Should().BeTrue();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/gracias")]
    [InlineData("gracias.html")]
    public void La_pagina_de_gracias_rechaza_lo_que_no_sea_http(string url)
    {
        // Acaba en un location.href del navegador del visitante: un javascript: ahí sería un
        // agujero abierto de par en par.
        Matchketing.Captacion.Dominio.Formulario
            .Crear(Empresa, "P", "Acepto.", false, false, false, url, null, Reloj())
            .Error!.Codigo.Should().Be("formulario.gracias_invalida");
    }

    [Fact]
    public void Desactivar_lo_saca_de_servicio()
    {
        var f = Crear();
        f.Desactivar();

        f.Activo.Should().BeFalse();
    }

    [Fact]
    public void El_origen_se_normaliza_a_minusculas()
    {
        var f = Matchketing.Captacion.Dominio.Formulario
            .Crear(Empresa, "P", "Acepto.", false, false, false, null, "  Feria De Valencia ", Reloj()).Valor;

        f.Origen.Should().Be("feria de valencia");
    }
}
