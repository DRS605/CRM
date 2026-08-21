using FluentAssertions;
using Matchketing.Objetivos.Aplicacion;
using Matchketing.Objetivos.Dominio;
using Xunit;

namespace Matchketing.Objetivos.Tests;

public sealed class PruebasServicioObjetivos
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Marta = Guid.NewGuid();
    private static readonly Guid Vicent = Guid.NewGuid();
    private static readonly Guid Rocio = Guid.NewGuid();

    private static readonly DateOnly Agosto = new(2026, 8, 1);

    private readonly RelojFijo reloj = new(new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero));
    private readonly RepositorioEnMemoria repositorio = new();
    private readonly LogradoDePrueba logrado = new();
    private readonly EquipoDePrueba equipo = new();

    public PruebasServicioObjetivos()
    {
        equipo.Gente.Add(new QuienVende(Marta, "Marta Ruiz", Vende: true));
        equipo.Gente.Add(new QuienVende(Vicent, "Vicent Ferrer", Vende: true));

        // Rocío es de solo lectura: está en el equipo pero no vende, así que no le toca objetivo.
        equipo.Gente.Add(new QuienVende(Rocio, "Rocío Ferrán", Vende: false));
    }

    private ServicioObjetivos Servicio(Guid? quien = null) => new(
        repositorio, logrado, equipo, new ContextoDePrueba(Empresa, quien ?? Marta), reloj);

    [Fact]
    public async Task Fijar_dos_veces_el_mismo_mes_cambia_el_objetivo_y_no_crea_otro()
    {
        // Quien pone objetivos rellena la tabla del equipo entero y no le importa cuáles existían ya.
        // Dos operaciones distintas le habrían obligado a saberlo.
        (await Servicio().FijarAsync(Marta, Agosto, 30_000m)).Exito.Should().BeTrue();
        (await Servicio().FijarAsync(Marta, Agosto, 45_000m)).Exito.Should().BeTrue();

        repositorio.Todos.Should().ContainSingle();
        repositorio.Todos[0].Importe.Should().Be(45_000m);
    }

    [Fact]
    public async Task No_se_le_pone_objetivo_a_alguien_que_no_esta_en_el_equipo()
    {
        // Sin esto aparecería una fila sin nombre en la tabla del equipo, y nadie sabría de quién es.
        var r = await Servicio().FijarAsync(Guid.NewGuid(), Agosto, 30_000m);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("objetivo.persona_no_esta");
        repositorio.Todos.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_objetivo_puesto_Hoy_no_ensena_nada()
    {
        // No tener objetivo es normal, no es un fallo. Y «has ganado 12.400 € este mes» sin objetivo es
        // una curiosidad: el número solo dice algo al lado del compromiso.
        logrado.Ganado[(Marta, Agosto)] = 12_400m;

        (await Servicio().MioAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Con_objetivo_puesto_Hoy_dice_cuanto_falta_y_cuanto_al_dia()
    {
        await Servicio().FijarAsync(Marta, Agosto, 30_000m);
        logrado.Ganado[(Marta, Agosto)] = 11_600m;

        var mio = await Servicio().MioAsync();

        mio.Should().NotBeNull();
        mio!.Logrado.Should().Be(11_600m);
        mio.Avance!.Falta.Should().Be(18_400m);
        mio.Avance.DiasLaborablesRestantes.Should().Be(10);
        mio.Avance.PorDiaQueQueda.Should().Be(1_840m);
    }

    [Fact]
    public async Task Cada_uno_ve_lo_suyo_y_no_lo_del_companero()
    {
        await Servicio().FijarAsync(Marta, Agosto, 30_000m);
        await Servicio().FijarAsync(Vicent, Agosto, 20_000m);
        logrado.Ganado[(Marta, Agosto)] = 25_000m;
        logrado.Ganado[(Vicent, Agosto)] = 4_000m;

        (await Servicio(Marta).MioAsync())!.Logrado.Should().Be(25_000m);
        (await Servicio(Vicent).MioAsync())!.Logrado.Should().Be(4_000m);
    }

    [Fact]
    public async Task La_tabla_del_equipo_saca_tambien_a_quien_no_tiene_objetivo()
    {
        // Es la pantalla donde se ponen. Si solo apareciera quien ya tiene uno, no habría forma de darle
        // objetivo a nadie nuevo.
        await Servicio().FijarAsync(Marta, Agosto, 30_000m);

        var mes = await Servicio().EquipoAsync();

        mes.Personas.Should().HaveCount(2, "Rocío no vende, así que no le toca objetivo");
        mes.Personas.Single(p => p.UsuarioId == Marta).Avance.Should().NotBeNull();
        mes.Personas.Single(p => p.UsuarioId == Vicent).Avance.Should().BeNull();
    }

    [Fact]
    public async Task El_objetivo_de_la_empresa_es_la_suma_de_los_de_su_gente()
    {
        // Un objetivo de empresa guardado aparte que no cuadre con la suma de los de su gente son dos
        // verdades, y la que se mira es siempre la equivocada.
        await Servicio().FijarAsync(Marta, Agosto, 30_000m);
        await Servicio().FijarAsync(Vicent, Agosto, 20_000m);
        logrado.Ganado[(Marta, Agosto)] = 25_000m;
        logrado.Ganado[(Vicent, Agosto)] = 5_000m;

        var mes = await Servicio().EquipoAsync();

        mes.Objetivo.Should().Be(50_000m);
        mes.Logrado.Should().Be(30_000m);
        mes.Porcentaje.Should().Be(60);
        mes.HayObjetivos.Should().BeTrue();
    }

    [Fact]
    public async Task Lo_ganado_por_quien_no_tiene_objetivo_no_entra_en_el_total()
    {
        // Sumar lo de todos y compararlo con la suma de unos pocos objetivos daría más del cien por cien
        // sin que nadie hubiera vendido más de lo previsto: la clase de número que hace inútil un panel.
        await Servicio().FijarAsync(Marta, Agosto, 10_000m);
        logrado.Ganado[(Marta, Agosto)] = 10_000m;
        logrado.Ganado[(Vicent, Agosto)] = 90_000m;

        var mes = await Servicio().EquipoAsync();

        mes.Objetivo.Should().Be(10_000m);
        mes.Logrado.Should().Be(10_000m);
        mes.Porcentaje.Should().Be(100);

        // Pero lo de Vicent sigue estando en su fila: no se esconde, solo no se suma a un total que no
        // le corresponde.
        mes.Personas.Single(p => p.UsuarioId == Vicent).Logrado.Should().Be(90_000m);
    }

    [Fact]
    public async Task Sin_ningun_objetivo_no_hay_porcentaje_que_dar()
    {
        logrado.Ganado[(Marta, Agosto)] = 25_000m;

        var mes = await Servicio().EquipoAsync();

        mes.HayObjetivos.Should().BeFalse();
        mes.Porcentaje.Should().BeNull();
    }

    [Fact]
    public async Task Quitar_un_objetivo_no_es_ponerlo_a_cero()
    {
        // Con el objetivo a cero, la pantalla enseñaría un 0 % permanente. Quitándolo, deja de enseñar
        // la línea, que es lo que significa «esta persona no tiene objetivo este mes».
        await Servicio().FijarAsync(Marta, Agosto, 30_000m);

        (await Servicio().QuitarAsync(Marta, Agosto)).Exito.Should().BeTrue();

        repositorio.Todos.Should().BeEmpty();
        (await Servicio().MioAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Quitar_el_objetivo_de_un_mes_pasado_no_se_puede()
    {
        await Servicio().FijarAsync(Marta, Agosto, 30_000m);
        reloj.AhoraUtc = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

        var r = await Servicio().QuitarAsync(Marta, Agosto);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("objetivo.mes_pasado");
        repositorio.Todos.Should().ContainSingle();
    }

    [Fact]
    public async Task Quitar_lo_que_no_hay_se_dice()
    {
        (await Servicio().QuitarAsync(Marta, Agosto)).Error!.Codigo.Should().Be("objetivo.no_encontrado");
    }

    [Fact]
    public async Task El_historico_dice_que_se_pidio_y_que_se_hizo_cada_mes()
    {
        await Servicio().FijarAsync(Marta, Agosto, 30_000m);
        await Servicio().FijarAsync(Marta, new DateOnly(2026, 9, 1), 40_000m);
        logrado.Ganado[(Marta, Agosto)] = 33_000m;
        logrado.Ganado[(Marta, new DateOnly(2026, 9, 1))] = 20_000m;

        var historico = await Servicio().HistoricoAsync(Marta);

        // Del más reciente hacia atrás: lo que interesa mirar primero es el mes en curso.
        historico.Should().HaveCount(2);
        historico[0].Mes.Should().Be(new DateOnly(2026, 9, 1));
        historico[0].Porcentaje.Should().Be(50);
        historico[1].Mes.Should().Be(Agosto);
        historico[1].Porcentaje.Should().Be(110);
    }

    [Fact]
    public async Task Se_puede_mirar_el_mes_que_viene_y_los_dias_que_quedan_son_cero()
    {
        await Servicio().FijarAsync(Marta, new DateOnly(2026, 9, 1), 40_000m);

        var mes = await Servicio().EquipoAsync(new DateOnly(2026, 9, 15));

        mes.Mes.Should().Be(new DateOnly(2026, 9, 1), "el mes se normaliza también aquí");
        mes.Objetivo.Should().Be(40_000m);
        mes.DiasLaborablesRestantes.Should().Be(0, "septiembre no ha empezado");
    }
}
