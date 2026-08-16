using FluentAssertions;
using Matchketing.Embudo.Dominio;
using Xunit;

namespace Matchketing.Embudo.Tests;

public sealed class PruebasOportunidad
{
    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Contacto = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static RelojFijo Reloj() => new(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

    private static (Dominio.Embudo Embudo, Oportunidad Oportunidad, RelojFijo Reloj) Escenario(decimal importe = 14280m)
    {
        var reloj = Reloj();
        var embudo = Dominio.Embudo.CrearPorDefecto(Empresa, reloj);
        var o = Oportunidad.Crear(Empresa, Contacto, null, "Cámara frigorífica", importe, embudo, null, null, null, reloj).Valor;
        return (embudo, o, reloj);
    }

    [Fact]
    public void Una_oportunidad_nueva_cae_en_la_primera_etapa_y_esta_abierta()
    {
        var (embudo, o, _) = Escenario();

        o.EtapaId.Should().Be(embudo.Etapas[0].Id);
        o.Estado.Should().Be(EstadoOportunidad.Abierta);
        o.CerradaEn.Should().BeNull();
    }

    [Fact]
    public void Crear_emite_el_evento()
    {
        var (_, o, _) = Escenario();

        o.Eventos.Should().ContainSingle().Which.Should().BeOfType<OportunidadCreada>();
    }

    [Fact]
    public void El_importe_se_redondea_a_dos_decimales()
    {
        var (_, o, _) = Escenario(1234.567m);

        o.Importe.Should().Be(1234.57m);
    }

    [Fact]
    public void Un_importe_negativo_no_tiene_sentido()
    {
        var reloj = Reloj();
        var embudo = Dominio.Embudo.CrearPorDefecto(Empresa, reloj);

        Oportunidad.Crear(Empresa, Contacto, null, "Cámara", -1m, embudo, null, null, null, reloj)
            .Error!.Codigo.Should().Be("oportunidad.importe_negativo");
    }

    [Fact]
    public void La_oportunidad_necesita_titulo()
    {
        var reloj = Reloj();
        var embudo = Dominio.Embudo.CrearPorDefecto(Empresa, reloj);

        Oportunidad.Crear(Empresa, Contacto, null, "  ", 100m, embudo, null, null, null, reloj)
            .Error!.Codigo.Should().Be("oportunidad.titulo_vacio");
    }

    [Fact]
    public void No_se_puede_colocar_en_una_etapa_de_otro_embudo()
    {
        var reloj = Reloj();
        var mio = Dominio.Embudo.CrearPorDefecto(Empresa, reloj);
        var ajeno = Dominio.Embudo.CrearPorDefecto(Empresa, reloj);

        Oportunidad.Crear(Empresa, Contacto, null, "Cámara", 100m, mio, ajeno.Etapas[2].Id, null, null, reloj)
            .Error!.Codigo.Should().Be("oportunidad.etapa_invalida");
    }

    [Fact]
    public void Perder_sin_motivo_no_se_permite()
    {
        var (_, o, reloj) = Escenario();

        var r = o.Perder(null, null, reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("oportunidad.sin_motivo");
        o.Estado.Should().Be(EstadoOportunidad.Abierta, "si falla la transición, no se cierra nada");
    }

    [Fact]
    public void Perder_con_motivo_cierra_y_emite_el_evento()
    {
        var (_, o, reloj) = Escenario();

        o.Perder(MotivoPerdida.Precio, "Nos sacaba 2.000 €.", reloj).Exito.Should().BeTrue();

        o.Estado.Should().Be(EstadoOportunidad.Perdida);
        o.Motivo.Should().Be(MotivoPerdida.Precio);
        o.DetalleMotivo.Should().Be("Nos sacaba 2.000 €.");
        o.Eventos.OfType<OportunidadPerdida>().Should().ContainSingle();
    }

    [Fact]
    public void Ganar_cierra_sin_motivo_y_el_estado_se_deduce()
    {
        var (_, o, reloj) = Escenario();

        o.Ganar(reloj).Exito.Should().BeTrue();

        o.Estado.Should().Be(EstadoOportunidad.Ganada);
        o.Motivo.Should().BeNull();
        o.CerradaEn.Should().Be(reloj.AhoraUtc);
    }

    [Fact]
    public void Una_oportunidad_cerrada_no_se_reabre_ni_por_ganar_ni_por_perder()
    {
        var (_, o, reloj) = Escenario();
        o.Ganar(reloj);

        o.Ganar(reloj).Error!.Codigo.Should().Be("oportunidad.ya_cerrada");
        o.Perder(MotivoPerdida.Precio, null, reloj).Error!.Codigo.Should().Be("oportunidad.ya_cerrada");
    }

    [Fact]
    public void Una_oportunidad_cerrada_no_se_mueve_ni_se_edita()
    {
        var (embudo, o, reloj) = Escenario();
        o.Perder(MotivoPerdida.NoContesta, null, reloj);

        o.Mover(embudo, embudo.Etapas[2].Id, reloj).Error!.Codigo.Should().Be("oportunidad.cerrada");
        o.Actualizar("Otro título", 100m, null, null, reloj).Error!.Codigo.Should().Be("oportunidad.cerrada");
    }

    [Fact]
    public void Mover_reinicia_el_contador_de_estancamiento()
    {
        var (embudo, o, reloj) = Escenario();
        reloj.Avanzar(TimeSpan.FromDays(9));
        o.DiasEnEtapa(reloj.AhoraUtc).Should().Be(9);

        o.Mover(embudo, embudo.Etapas[2].Id, reloj).Exito.Should().BeTrue();

        o.DiasEnEtapa(reloj.AhoraUtc).Should().Be(0);
        o.EtapaId.Should().Be(embudo.Etapas[2].Id);
    }

    [Fact]
    public void Mover_a_la_misma_etapa_no_reinicia_el_contador()
    {
        var (embudo, o, reloj) = Escenario();
        reloj.Avanzar(TimeSpan.FromDays(5));

        o.Mover(embudo, o.EtapaId, reloj).Exito.Should().BeTrue();

        o.DiasEnEtapa(reloj.AhoraUtc).Should().Be(5);
    }

    [Fact]
    public void Se_marca_estancada_al_pasar_los_dias_que_tolera_la_etapa()
    {
        var (embudo, o, reloj) = Escenario();
        var dias = embudo.Etapas[0].DiasAviso;

        o.EstaEstancada(dias, reloj.AhoraUtc).Should().BeFalse();
        reloj.Avanzar(TimeSpan.FromDays(dias + 1));
        o.EstaEstancada(dias, reloj.AhoraUtc).Should().BeTrue();
    }

    [Fact]
    public void Una_oportunidad_cerrada_nunca_esta_estancada()
    {
        var (embudo, o, reloj) = Escenario();
        o.Ganar(reloj);
        reloj.Avanzar(TimeSpan.FromDays(90));

        o.EstaEstancada(embudo.Etapas[0].DiasAviso, reloj.AhoraUtc).Should().BeFalse();
    }
}
