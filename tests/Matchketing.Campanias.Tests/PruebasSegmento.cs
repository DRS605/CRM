using FluentAssertions;
using Matchketing.Campanias.Dominio;
using Xunit;

namespace Matchketing.Campanias.Tests;

public sealed class PruebasCriterios
{
    [Fact]
    public void Un_segmento_sin_ningun_criterio_no_se_guarda()
    {
        // La regla más importante del módulo. Un segmento vacío significa «todos mis contactos», y a un
        // segmento así se le lanza una campaña sin darse cuenta. Que haya que decir a quién apuntas.
        var r = CriteriosSegmento.Crear(null, null, null, null, null, null);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("segmento.sin_criterios");
    }

    [Fact]
    public void Un_criterio_en_blanco_no_cuenta_como_criterio()
    {
        // Espacios en la provincia es lo que llega de un formulario con un campo tocado y vaciado. Si
        // contase, el segmento pasaría la validación y luego apuntaría a todo el mundo.
        var r = CriteriosSegmento.Crear(null, "   ", "\t", null, null, Guid.Empty);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("segmento.sin_criterios");
    }

    [Fact]
    public void No_hay_forma_de_pedir_a_los_que_estan_de_baja()
    {
        // No es una comprobación de tiempo de ejecución, es que el valor no existe. Una baja no es un
        // segmento al que apuntar; es un muro. Si estuviera en el enumerado, habría un desplegable donde
        // se puede elegir y lo único que lo impediría sería la comprobación del final.
        Enum.GetNames<EstadoBuscado>().Should().BeEquivalentTo("Lead", "Cliente", "Perdido");
        Enum.GetValues<EstadoBuscado>().Should().NotContain(e => e.ToString() == "Baja");
    }

    [Fact]
    public void Un_match_minimo_de_cero_se_rechaza_porque_no_filtra_nada()
    {
        CriteriosSegmento.Crear(null, null, null, 0, null, null).Error!.Codigo
            .Should().Be("segmento.match_invalido");

        CriteriosSegmento.Crear(null, null, null, 101, null, null).Error!.Codigo
            .Should().Be("segmento.match_invalido");

        CriteriosSegmento.Crear(null, null, null, 1, null, null).Exito.Should().BeTrue();
        CriteriosSegmento.Crear(null, null, null, 100, null, null).Exito.Should().BeTrue();
    }

    [Fact]
    public void Los_dias_sin_actividad_tienen_techo()
    {
        CriteriosSegmento.Crear(null, null, null, null, 0, null).Error!.Codigo
            .Should().Be("segmento.dias_invalidos");

        CriteriosSegmento.Crear(null, null, null, null, CriteriosSegmento.MaximoDias + 1, null).Error!.Codigo
            .Should().Be("segmento.dias_invalidos");

        CriteriosSegmento.Crear(null, null, null, null, 30, null).Exito.Should().BeTrue();
    }

    [Fact]
    public void Los_textos_se_limpian_de_espacios()
    {
        var r = CriteriosSegmento.Crear(null, "  Valencia  ", " formulario web ", null, null, null);

        r.Exito.Should().BeTrue();
        r.Valor.Provincia.Should().Be("Valencia");
        r.Valor.Origen.Should().Be("formulario web");
    }

    [Fact]
    public void Un_guid_vacio_de_etapa_no_es_un_criterio()
    {
        // `Guid.Empty` es lo que llega de un desplegable en blanco. Guardarlo dejaría un segmento que no
        // encuentra a nadie —ninguna etapa tiene ese identificador— y nadie entendería por qué.
        var r = CriteriosSegmento.Crear(EstadoBuscado.Cliente, null, null, null, null, Guid.Empty);

        r.Exito.Should().BeTrue();
        r.Valor.EtapaId.Should().BeNull();
        r.Valor.Cuantos.Should().Be(1);
    }

    [Fact]
    public void La_frase_dice_el_segmento_en_castellano()
    {
        var r = CriteriosSegmento.Crear(EstadoBuscado.Cliente, "Valencia", null, 60, 90, null);

        r.Valor.Frase().Should().Be(
            "clientes, de Valencia, con match de 60 o más, sin actividad desde hace 90 días");
    }

    [Fact]
    public void La_frase_nombra_la_etapa_si_se_la_dan_y_no_se_la_inventa_si_no()
    {
        var etapa = Guid.NewGuid();
        var r = CriteriosSegmento.Crear(EstadoBuscado.Lead, null, null, null, null, etapa);

        r.Valor.Frase("Propuesta").Should().Be("leads, con una oportunidad abierta en «Propuesta»");
        r.Valor.Frase().Should().Be("leads, con una oportunidad abierta en una etapa concreta");
    }

    [Fact]
    public void Sin_estado_la_frase_dice_contactos_y_no_deja_el_hueco()
    {
        var r = CriteriosSegmento.Crear(null, "Castellón", null, null, null, null);

        r.Valor.Frase().Should().Be("contactos, de Castellón");
    }

    [Fact]
    public void Un_dia_se_dice_en_singular()
    {
        // Detalle pequeño y del producto: «sin actividad desde hace 1 días» es la marca de que el texto
        // lo escribió una plantilla y no una persona, y esto lo lee un cliente.
        CriteriosSegmento.Crear(null, null, null, null, 1, null).Valor.Frase()
            .Should().Be("contactos, sin actividad desde ayer");
    }
}

public sealed class PruebasSegmento
{
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));

    private static CriteriosSegmento Clientes =>
        CriteriosSegmento.Crear(EstadoBuscado.Cliente, null, null, null, null, null).Valor;

    [Fact]
    public void Un_segmento_necesita_nombre()
    {
        Segmento.Crear(Guid.NewGuid(), null, Clientes, Reloj).Error!.Codigo.Should().Be("segmento.sin_nombre");
        Segmento.Crear(Guid.NewGuid(), "   ", Clientes, Reloj).Error!.Codigo.Should().Be("segmento.sin_nombre");
    }

    [Fact]
    public void El_nombre_tiene_techo()
    {
        var largo = new string('a', Segmento.LongitudMaximaNombre + 1);

        Segmento.Crear(Guid.NewGuid(), largo, Clientes, Reloj).Error!.Codigo.Should().Be("segmento.nombre_largo");
    }

    [Fact]
    public void Un_record_de_criterios_vacio_construido_a_mano_tampoco_pasa()
    {
        // `CriteriosSegmento` es un `record`, así que se puede construir con `new` saltándose la fábrica.
        // El segmento lo vuelve a comprobar: la regla de «al menos un criterio» no puede depender de que
        // todo el mundo se acuerde de usar la puerta correcta.
        var r = Segmento.Crear(Guid.NewGuid(), "Todos", CriteriosSegmento.Vacios, Reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("segmento.sin_criterios");
    }

    [Fact]
    public void Cambiar_un_segmento_actualiza_la_fecha_y_no_la_de_creacion()
    {
        var reloj = new RelojFijo(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));
        var s = Segmento.Crear(Guid.NewGuid(), "Clientes", Clientes, reloj).Valor;
        var creado = s.CreadoEn;

        reloj.Avanzar(TimeSpan.FromDays(3));
        var nuevos = CriteriosSegmento.Crear(EstadoBuscado.Lead, "Valencia", null, null, null, null).Valor;

        s.Cambiar("Leads de Valencia", nuevos, reloj).Exito.Should().BeTrue();

        s.Nombre.Should().Be("Leads de Valencia");
        s.Criterios.Should().Be(nuevos);
        s.CreadoEn.Should().Be(creado);
        s.ActualizadoEn.Should().Be(reloj.AhoraUtc);
    }
}
