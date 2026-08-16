using FluentAssertions;
using Matchketing.Contactos.Dominio;
using Xunit;

namespace Matchketing.Contactos.Tests;

public sealed class PruebasActividad
{
    private static readonly Guid Contacto = Guid.NewGuid();

    [Fact]
    public void Una_llamada_sin_resultado_no_se_puede_registrar()
    {
        var r = Actividad.Crear(Datos.Empresa, Contacto, TipoActividad.Llamada, SentidoActividad.Saliente, "Le he llamado", null, Datos.Reloj());

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("actividad.llamada_sin_resultado");
    }

    [Fact]
    public void Una_llamada_con_resultado_si()
    {
        var r = Actividad.Crear(
            Datos.Empresa, Contacto, TipoActividad.Llamada, SentidoActividad.Saliente, "Le he llamado", null,
            Datos.Reloj(), ResultadoLlamada.Contactado);

        r.Exito.Should().BeTrue();
        r.Valor.Resultado.Should().Be(ResultadoLlamada.Contactado);
    }

    [Fact]
    public void Una_actividad_sin_texto_no_dice_nada_y_se_rechaza()
    {
        Actividad.Crear(Datos.Empresa, Contacto, TipoActividad.Nota, SentidoActividad.Interna, "   ", null, Datos.Reloj())
            .Error!.Codigo.Should().Be("actividad.cuerpo_vacio");
    }

    [Fact]
    public void El_texto_se_recorta()
    {
        var a = Actividad.Crear(Datos.Empresa, Contacto, TipoActividad.Nota, SentidoActividad.Interna, "  Ha llamado él  ", null, Datos.Reloj()).Valor;

        a.Cuerpo.Should().Be("Ha llamado él");
    }

    [Fact]
    public void Reasignar_es_lo_unico_que_se_le_puede_cambiar_a_una_actividad()
    {
        var a = Actividad.Crear(Datos.Empresa, Contacto, TipoActividad.Nota, SentidoActividad.Interna, "Nota", null, Datos.Reloj()).Valor;
        var otro = Guid.NewGuid();

        a.ReasignarA(otro);

        a.ContactoId.Should().Be(otro);
    }
}
