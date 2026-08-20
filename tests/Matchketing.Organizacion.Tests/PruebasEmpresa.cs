using FluentAssertions;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Organizacion.Dominio;
using Xunit;

namespace Matchketing.Organizacion.Tests;

file sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc => ahora;
}

public sealed class PruebasEmpresa
{
    private static readonly IReloj Reloj = new RelojFijo(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Una_empresa_nueva_nace_con_el_match_mitad_y_mitad()
    {
        var r = Empresa.Crear("Instalaciones Ribera, S.L.", "B12345678", "Valencia", Reloj);

        r.Exito.Should().BeTrue();
        r.Valor.PesoEncaje.Should().Be(0.5m);
        r.Valor.HorasRebote.Should().Be(4);
        r.Valor.Activa.Should().BeTrue();
    }

    [Fact]
    public void Crear_emite_el_evento_de_alta()
    {
        var r = Empresa.Crear("Ribera", null, null, Reloj);

        r.Valor.Eventos.Should().ContainSingle().Which.Should().BeOfType<EmpresaCreada>();
    }

    [Fact]
    public void El_nombre_es_obligatorio()
    {
        var r = Empresa.Crear("  ", null, null, Reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("empresa.nombre_vacio");
    }

    [Theory]
    [InlineData(-0.1, 4, "empresa.peso_invalido")]
    [InlineData(1.5, 4, "empresa.peso_invalido")]
    [InlineData(0.5, 0, "empresa.rebote_invalido")]
    [InlineData(0.5, 500, "empresa.rebote_invalido")]
    public void Los_ajustes_del_match_se_validan(decimal peso, int horas, string codigo)
    {
        var empresa = Empresa.Crear("Ribera", null, null, Reloj).Valor;

        var r = empresa.AjustarMatch(peso, horas, Reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be(codigo);
    }

    [Fact]
    public void Los_ajustes_validos_se_guardan()
    {
        var empresa = Empresa.Crear("Ribera", null, null, Reloj).Valor;

        empresa.AjustarMatch(0.65m, 6, Reloj).Exito.Should().BeTrue();

        empresa.PesoEncaje.Should().Be(0.65m);
        empresa.HorasRebote.Should().Be(6);
    }

    [Fact]
    public void Una_empresa_nace_conservando_los_leads_dos_anos()
    {
        Empresa.Crear("Ribera", null, null, Reloj).Valor.MesesRetencionLeads.Should().Be(24);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(36)]
    [InlineData(120)]
    public void El_plazo_de_conservacion_admite_lo_razonable(int meses)
    {
        var empresa = Empresa.Crear("Ribera", null, null, Reloj).Valor;

        empresa.AjustarRetencion(meses, Reloj).Exito.Should().BeTrue();

        empresa.MesesRetencionLeads.Should().Be(meses);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(121)]
    public void Un_plazo_absurdo_se_rechaza(int meses)
    {
        // Por debajo de tres meses el sistema borraría leads que todavía se están trabajando, y un
        // CRM que se come los leads no es un CRM.
        var empresa = Empresa.Crear("Ribera", null, null, Reloj).Valor;

        empresa.AjustarRetencion(meses, Reloj).Error!.Codigo.Should().Be("empresa.retencion_invalida");
        empresa.MesesRetencionLeads.Should().Be(24, "un ajuste inválido no cambia el que había");
    }

    [Fact]
    public void Los_campos_opcionales_en_blanco_se_guardan_como_nulos()
    {
        var r = Empresa.Crear("Ribera", "   ", "", Reloj);

        r.Valor.Nif.Should().BeNull();
        r.Valor.Provincia.Should().BeNull();
    }

    [Fact]
    public void Los_datos_de_la_ficha_se_pueden_corregir()
    {
        // `Actualizar` existía desde el primer módulo **sin un solo llamante**: no había endpoint ni
        // pantalla, así que el NIF se enseñaba en Ajustes y no se podía rellenar nunca, y una errata
        // en el nombre era para siempre. Estas pruebas llegan con el endpoint que lo usa.
        var empresa = Empresa.Crear("Bar Nou, S.L.", null, null, Reloj).Valor;
        var despues = new RelojFijo(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

        var r = empresa.Actualizar("  Bar Nou de Vinaròs, S.L.  ", " B98765432 ", " Castellón ", despues);

        r.Exito.Should().BeTrue();
        empresa.Nombre.Should().Be("Bar Nou de Vinaròs, S.L.", "el nombre se recorta");
        empresa.Nif.Should().Be("B98765432");
        empresa.Provincia.Should().Be("Castellón");
        empresa.ActualizadoEn.Should().Be(despues.AhoraUtc);
    }

    [Fact]
    public void Corregir_los_datos_puede_dejar_en_blanco_lo_opcional()
    {
        var empresa = Empresa.Crear("Ribera", "B12345678", "Valencia", Reloj).Valor;

        empresa.Actualizar("Ribera", "  ", null, Reloj).Exito.Should().BeTrue();

        empresa.Nif.Should().BeNull("borrar un NIF equivocado tiene que poder hacerse");
        empresa.Provincia.Should().BeNull();
    }

    [Fact]
    public void Corregir_los_datos_no_puede_dejar_la_empresa_sin_nombre()
    {
        var empresa = Empresa.Crear("Ribera", null, null, Reloj).Valor;

        empresa.Actualizar("   ", null, null, Reloj).Error!.Codigo.Should().Be("empresa.nombre_vacio");
        empresa.Nombre.Should().Be("Ribera", "un cambio inválido no deja la empresa a medias");
    }

    [Fact]
    public void La_medicion_de_aperturas_nace_apagada_y_se_enciende_a_mano()
    {
        // Es la parte que hacía falta para que la frase de la documentación fuera verdad: «que sea una
        // decisión explícita». Sin interruptor no hay decisión, solo un valor que nadie puede cambiar.
        var empresa = Empresa.Crear("Ribera", null, null, Reloj).Valor;
        empresa.SigueAperturas.Should().BeFalse();

        var despues = new RelojFijo(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        empresa.AjustarSeguimiento(true, despues);

        empresa.SigueAperturas.Should().BeTrue();
        empresa.ActualizadoEn.Should().Be(despues.AhoraUtc);

        empresa.AjustarSeguimiento(false, despues);
        empresa.SigueAperturas.Should().BeFalse("y se tiene que poder apagar igual de fácil");
    }
}
