using FluentAssertions;
using Matchketing.Contactos.Dominio;
using Xunit;

namespace Matchketing.Contactos.Tests;

public sealed class PruebasContacto
{
    private static Contacto Crear(string? email = "manolo@casamanolo.es", string? telefono = null, string? nombre = "Manolo García") =>
        Contacto.Crear(Datos.Empresa, nombre, email, telefono, null, null, null, null, Datos.Reloj()).Valor;

    [Fact]
    public void Un_contacto_sin_correo_ni_telefono_no_es_un_contacto()
    {
        var r = Contacto.Crear(Datos.Empresa, "Manolo", null, null, null, null, null, null, Datos.Reloj());

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("contacto.sin_medio");
    }

    [Fact]
    public void Basta_con_el_telefono()
    {
        Contacto.Crear(Datos.Empresa, "Manolo", null, "961234567", null, null, null, null, Datos.Reloj())
            .Exito.Should().BeTrue();
    }

    [Fact]
    public void El_correo_y_el_telefono_se_guardan_ya_normalizados()
    {
        var c = Crear("  Manolo@CasaManolo.ES ", "96 123 45 67");

        c.Email.Should().Be("manolo@casamanolo.es");
        c.Telefono.Should().Be("+34961234567");
    }

    [Fact]
    public void Nace_como_lead_activo_y_con_origen_manual_si_no_se_dice_otra_cosa()
    {
        var c = Crear();

        c.Estado.Should().Be(EstadoContacto.Lead);
        c.Activo.Should().BeTrue();
        c.Origen.Should().Be("manual");
    }

    [Fact]
    public void Crear_emite_el_evento_de_alta()
    {
        Crear().Eventos.Should().ContainSingle().Which.Should().BeOfType<ContactoCreado>();
    }

    [Fact]
    public void Un_contacto_de_baja_no_vuelve_por_la_puerta_de_atras()
    {
        var c = Crear();
        c.DarDeBaja(Datos.Reloj());

        var r = c.CambiarEstado(EstadoContacto.Cliente, Datos.Reloj());

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("contacto.dado_de_baja");
        c.Estado.Should().Be(EstadoContacto.Baja);
    }

    [Fact]
    public void Al_fusionar_solo_se_rellenan_huecos_nunca_se_pisa_lo_que_ya_hay()
    {
        var superviviente = Crear("manolo@casamanolo.es", null);
        var absorbido = Contacto.Crear(Datos.Empresa, "M. García", "otro@correo.es", "961234567", "Gerente", null, null, null, Datos.Reloj()).Valor;

        superviviente.Absorber(absorbido, Datos.Reloj()).Exito.Should().BeTrue();

        superviviente.Email.Should().Be("manolo@casamanolo.es", "el correo que ya había no se toca");
        superviviente.Telefono.Should().Be("+34961234567", "el hueco sí se rellena");
        superviviente.Cargo.Should().Be("Gerente");
    }

    [Fact]
    public void Al_fusionar_el_absorbido_queda_desactivado_y_con_el_rastro()
    {
        var superviviente = Crear();
        var absorbido = Crear("otro@correo.es");

        superviviente.Absorber(absorbido, Datos.Reloj());

        absorbido.Activo.Should().BeFalse();
        absorbido.FusionadoEnId.Should().Be(superviviente.Id);
        superviviente.Eventos.OfType<ContactoFusionado>().Should().ContainSingle();
    }

    [Fact]
    public void Si_uno_de_los_dos_ya_era_cliente_el_superviviente_lo_es()
    {
        var superviviente = Crear();
        var absorbido = Crear("otro@correo.es");
        absorbido.CambiarEstado(EstadoContacto.Cliente, Datos.Reloj());

        superviviente.Absorber(absorbido, Datos.Reloj());

        superviviente.Estado.Should().Be(EstadoContacto.Cliente);
    }

    [Fact]
    public void Si_uno_de_los_dos_pidio_la_baja_la_baja_manda()
    {
        var superviviente = Crear();
        var absorbido = Crear("otro@correo.es");
        absorbido.DarDeBaja(Datos.Reloj());

        superviviente.Absorber(absorbido, Datos.Reloj());

        superviviente.Estado.Should().Be(EstadoContacto.Baja);
    }

    [Fact]
    public void No_se_pueden_fusionar_contactos_de_empresas_distintas()
    {
        var mio = Crear();
        var ajeno = Contacto.Crear(Datos.OtraEmpresa, "Ajeno", "ajeno@otra.es", null, null, null, null, null, Datos.Reloj()).Valor;

        var r = mio.Absorber(ajeno, Datos.Reloj());

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("contacto.fusion_otra_empresa");
    }

    [Fact]
    public void Un_contacto_no_se_fusiona_consigo_mismo()
    {
        var c = Crear();

        c.Absorber(c, Datos.Reloj()).Error!.Codigo.Should().Be("contacto.fusion_consigo_mismo");
    }

    [Fact]
    public void No_se_fusiona_dos_veces_el_mismo_contacto()
    {
        var superviviente = Crear();
        var absorbido = Crear("otro@correo.es");
        superviviente.Absorber(absorbido, Datos.Reloj());

        superviviente.Absorber(absorbido, Datos.Reloj()).Error!.Codigo.Should().Be("contacto.ya_fusionado");
    }
}
