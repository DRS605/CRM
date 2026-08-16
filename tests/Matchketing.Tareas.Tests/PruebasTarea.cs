using FluentAssertions;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Tareas.Dominio;
using Xunit;

namespace Matchketing.Tareas.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; private set; } = ahora;

    public void Avanzar(TimeSpan cuanto) => AhoraUtc = AhoraUtc.Add(cuanto);
}

public sealed class PruebasTarea
{
    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Contacto = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static RelojFijo Reloj() => new(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

    private static readonly DateOnly Hoy = new(2026, 8, 16);

    private static Tarea Crear(RelojFijo reloj, DateOnly? vence = null) =>
        Tarea.Crear(Empresa, "Llamar a Manolo", Contacto, null, vence, null, reloj).Valor;

    [Fact]
    public void Una_tarea_sin_fecha_vence_hoy()
    {
        var t = Crear(Reloj());

        t.VenceEl.Should().Be(Hoy);
        t.Estado.Should().Be(EstadoTarea.Pendiente);
        t.Origen.Should().Be(OrigenTarea.Manual);
    }

    [Fact]
    public void Crear_emite_el_evento()
    {
        Crear(Reloj()).Eventos.Should().ContainSingle().Which.Should().BeOfType<TareaCreada>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void La_tarea_necesita_titulo(string? titulo)
    {
        Tarea.Crear(Empresa, titulo, Contacto, null, null, null, Reloj())
            .Error!.Codigo.Should().Be("tarea.titulo_vacio");
    }

    [Fact]
    public void Completar_la_cierra_y_emite_el_evento()
    {
        var reloj = Reloj();
        var t = Crear(reloj);

        t.Completar(reloj).Exito.Should().BeTrue();

        t.Estado.Should().Be(EstadoTarea.Hecha);
        t.CerradaEn.Should().Be(reloj.AhoraUtc);
        t.Eventos.OfType<TareaCompletada>().Should().ContainSingle();
    }

    [Fact]
    public void Una_tarea_cerrada_no_se_completa_ni_se_descarta_dos_veces()
    {
        var reloj = Reloj();
        var t = Crear(reloj);
        t.Completar(reloj);

        t.Completar(reloj).Error!.Codigo.Should().Be("tarea.ya_cerrada");
        t.Descartar(reloj).Error!.Codigo.Should().Be("tarea.ya_cerrada");
    }

    [Fact]
    public void Descartar_no_es_lo_mismo_que_hacer_pero_tambien_cierra()
    {
        var reloj = Reloj();
        var t = Crear(reloj);

        t.Descartar(reloj).Exito.Should().BeTrue();

        t.Estado.Should().Be(EstadoTarea.Descartada);
        t.Eventos.OfType<TareaCompletada>().Should().BeEmpty("descartar no es completar");
    }

    [Fact]
    public void Aplazar_sin_fecha_no_existe()
    {
        var reloj = Reloj();
        var t = Crear(reloj);

        var r = t.Aplazar(null, reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("tarea.aplazar_sin_fecha");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void Aplazar_a_hoy_o_al_pasado_tampoco(int dias)
    {
        var reloj = Reloj();
        var t = Crear(reloj);

        t.Aplazar(Hoy.AddDays(dias), reloj).Error!.Codigo.Should().Be("tarea.aplazar_al_pasado");
    }

    [Fact]
    public void Aplazar_mueve_la_fecha_y_lleva_la_cuenta()
    {
        var reloj = Reloj();
        var t = Crear(reloj);

        t.Aplazar(Hoy.AddDays(1), reloj).Exito.Should().BeTrue();
        t.Aplazar(Hoy.AddDays(7), reloj).Exito.Should().BeTrue();

        t.VenceEl.Should().Be(Hoy.AddDays(7));
        t.VecesAplazada.Should().Be(2, "aplazar cinco veces es una señal, no un accidente");
    }

    [Fact]
    public void Una_tarea_cerrada_no_se_aplaza_ni_se_edita()
    {
        var reloj = Reloj();
        var t = Crear(reloj);
        t.Completar(reloj);

        t.Aplazar(Hoy.AddDays(1), reloj).Error!.Codigo.Should().Be("tarea.ya_cerrada");
        t.Actualizar("Otro título", null, null, reloj).Error!.Codigo.Should().Be("tarea.ya_cerrada");
    }

    [Fact]
    public void Vencida_es_la_que_tenia_que_haberse_hecho_y_sigue_pendiente()
    {
        var reloj = Reloj();
        var t = Crear(reloj, Hoy.AddDays(-2));

        t.EstaVencida(Hoy).Should().BeTrue();
        t.TocaHoy(Hoy).Should().BeTrue();

        t.Completar(reloj);
        t.EstaVencida(Hoy).Should().BeFalse("una tarea hecha no está vencida");
    }

    [Fact]
    public void Lo_que_vence_manana_no_entra_en_la_pila_de_hoy()
    {
        var t = Crear(Reloj(), Hoy.AddDays(1));

        t.TocaHoy(Hoy).Should().BeFalse();
        t.EstaVencida(Hoy).Should().BeFalse();
    }
}
