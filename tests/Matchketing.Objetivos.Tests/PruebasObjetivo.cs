using FluentAssertions;
using Matchketing.Objetivos.Dominio;
using Xunit;

namespace Matchketing.Objetivos.Tests;

public sealed class PruebasObjetivo
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();

    /// <summary>Un martes de agosto, para que «los días que quedan» sea comprobable a mano.</summary>
    private static RelojFijo Reloj() => new(new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Un_objetivo_de_cero_no_es_un_objetivo()
    {
        var reloj = Reloj();

        Objetivo.Fijar(Empresa, Usuario, new DateOnly(2026, 8, 1), 0m, reloj)
            .Error!.Codigo.Should().Be("objetivo.importe_invalido");

        Objetivo.Fijar(Empresa, Usuario, new DateOnly(2026, 8, 1), -100m, reloj)
            .Error!.Codigo.Should().Be("objetivo.importe_invalido");
    }

    [Fact]
    public void Un_cero_de_mas_al_teclear_se_rechaza()
    {
        // No es una opinión sobre nadie: un objetivo mal escrito hace que la barra de todo el equipo no
        // signifique nada durante un mes entero.
        var reloj = Reloj();

        Objetivo.Fijar(Empresa, Usuario, new DateOnly(2026, 8, 1), Objetivo.ImporteMaximo + 1m, reloj)
            .Error!.Codigo.Should().Be("objetivo.importe_enorme");

        Objetivo.Fijar(Empresa, Usuario, new DateOnly(2026, 8, 1), Objetivo.ImporteMaximo, reloj)
            .Exito.Should().BeTrue();
    }

    [Fact]
    public void El_mes_se_normaliza_al_dia_uno()
    {
        // Sin normalizar, «agosto» puesto el día 18 y «agosto» puesto el día 3 serían dos filas
        // distintas, y la persona tendría dos objetivos del mismo mes.
        var r = Objetivo.Fijar(Empresa, Usuario, new DateOnly(2026, 8, 18), 30_000m, Reloj());

        r.Valor.Mes.Should().Be(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public void El_objetivo_de_un_mes_que_ya_paso_no_se_puede_tocar()
    {
        // Poner en agosto el objetivo de julio es escribir la historia después de conocerla, y un
        // histórico que se puede retocar no sirve ni para quien lo mira ni para quien lo cumplió.
        var reloj = Reloj();

        Objetivo.Fijar(Empresa, Usuario, new DateOnly(2026, 7, 1), 30_000m, reloj)
            .Error!.Codigo.Should().Be("objetivo.mes_pasado");
    }

    [Fact]
    public void El_mes_en_curso_y_los_futuros_si_se_pueden_poner()
    {
        var reloj = Reloj();

        Objetivo.Fijar(Empresa, Usuario, new DateOnly(2026, 8, 1), 30_000m, reloj).Exito.Should().BeTrue();
        Objetivo.Fijar(Empresa, Usuario, new DateOnly(2026, 12, 1), 50_000m, reloj).Exito.Should().BeTrue();
    }

    [Fact]
    public void Un_objetivo_es_de_alguien()
    {
        Objetivo.Fijar(Empresa, Guid.Empty, new DateOnly(2026, 8, 1), 30_000m, Reloj())
            .Error!.Codigo.Should().Be("objetivo.sin_persona");
    }

    [Fact]
    public void Cambiar_el_importe_se_puede_hasta_que_el_mes_acaba()
    {
        // Los objetivos se revisan a mitad de mes en la vida real; prohibirlo solo conseguiría que se
        // llevaran en una hoja aparte.
        var reloj = Reloj();
        var o = Objetivo.Fijar(Empresa, Usuario, new DateOnly(2026, 8, 1), 30_000m, reloj).Valor;

        o.Cambiar(45_000m, reloj).Exito.Should().BeTrue();
        o.Importe.Should().Be(45_000m);
        o.FijadoEn.Should().Be(reloj.AhoraUtc);

        // Pasa el mes: ya no.
        reloj.AhoraUtc = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        o.Cambiar(10_000m, reloj).Error!.Codigo.Should().Be("objetivo.mes_pasado");
        o.Importe.Should().Be(45_000m, "el importe no se toca si el cambio se rechaza");
    }
}

public sealed class PruebasAvance
{
    [Fact]
    public void Lo_que_falta_nunca_es_negativo()
    {
        // Pasarse del objetivo no puede dejar «−4.000 € pendientes» en la pantalla.
        var a = Avance.De(30_000m, 34_000m, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 1));

        a.Falta.Should().Be(0m);
        a.Cumplido.Should().BeTrue();
    }

    [Fact]
    public void El_porcentaje_no_tiene_techo_porque_vender_mas_es_un_dato()
    {
        // Al contrario que la conversión del embudo, donde pasar del 100 % era un fallo de cálculo: aquí
        // no hay nada que impida vender más de lo previsto, y enseñarlo como 100 % esconde el mejor mes
        // del año.
        var a = Avance.De(30_000m, 42_000m, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 1));

        a.Porcentaje.Should().Be(140);
    }

    [Fact]
    public void Los_dias_laborables_que_quedan_se_cuentan_de_lunes_a_viernes_con_hoy_dentro()
    {
        // Agosto de 2026: el 18 es martes. Del 18 al 31 hay 18,19,20,21 / 24,25,26,27,28 / 31 = 10.
        Avance.DiasLaborablesQueQuedan(new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 1))
            .Should().Be(10);

        // Un domingo no se cuenta a sí mismo. El 30 de agosto de 2026 es domingo: solo queda el 31.
        Avance.DiasLaborablesQueQuedan(new DateOnly(2026, 8, 30), new DateOnly(2026, 8, 1))
            .Should().Be(1);

        // El último día del mes, si es laborable, cuenta como uno.
        Avance.DiasLaborablesQueQuedan(new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 1))
            .Should().Be(1);
    }

    [Fact]
    public void Mirar_otro_mes_no_reparte_el_importe_entre_dias_que_no_han_empezado()
    {
        // Mirar el objetivo de noviembre en agosto tiene que decir «no quedan días de ese mes por
        // trabajar todavía», no repartirlo entre los veintiún días de noviembre.
        Avance.DiasLaborablesQueQuedan(new DateOnly(2026, 8, 18), new DateOnly(2026, 11, 1))
            .Should().Be(0);

        // Y un mes ya pasado, tampoco.
        Avance.DiasLaborablesQueQuedan(new DateOnly(2026, 8, 18), new DateOnly(2026, 7, 1))
            .Should().Be(0);
    }

    [Fact]
    public void Lo_que_hace_falta_al_dia_es_el_numero_que_cambia_la_tarde()
    {
        // «Te faltan 18.400 € y quedan 10 días laborables» son 1.840 € al día. Ese es el número que hace
        // que alguien llame esta tarde; un 38 % no le dice a nadie si tiene que darse prisa.
        var a = Avance.De(30_000m, 11_600m, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 1));

        a.Falta.Should().Be(18_400m);
        a.DiasLaborablesRestantes.Should().Be(10);
        a.PorDiaQueQueda.Should().Be(1_840m);
    }

    [Fact]
    public void Cumplido_no_reparte_nada_al_dia()
    {
        var a = Avance.De(30_000m, 30_000m, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 1));

        a.PorDiaQueQueda.Should().BeNull("no hay nada que repartir");
    }

    [Fact]
    public void Un_mes_acabado_y_sin_cumplir_no_inventa_un_importe_diario()
    {
        // Con el mes cerrado, «te faltan 18.400 € al día» sería una cifra sin sentido al lado de un mes
        // que ya no se puede cambiar.
        var a = Avance.De(30_000m, 11_600m, new DateOnly(2026, 9, 5), new DateOnly(2026, 8, 1));

        a.DiasLaborablesRestantes.Should().Be(0);
        a.PorDiaQueQueda.Should().BeNull();
        a.Falta.Should().Be(18_400m, "lo que faltó sigue siendo un dato del mes");
    }
}
