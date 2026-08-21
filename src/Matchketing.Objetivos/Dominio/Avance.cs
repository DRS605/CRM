using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Objetivos.Dominio;

/// <summary>
/// Cómo va el mes. Es un cálculo puro y por eso vive en el dominio: se prueba sin base de datos y dice
/// lo mismo en las cuatro pantallas que lo usan.
///
/// El número que importa aquí no es el porcentaje —eso lo enseña cualquiera— sino
/// <see cref="PorDiaQueQueda"/>: «te faltan 18.400 € y quedan 7 días laborables» son 2.630 € al día, y
/// **ese** es el número que cambia lo que hace alguien esta tarde. Un porcentaje al 62 % no le dice a
/// nadie si tiene que darse prisa.
/// </summary>
public sealed record Avance
{
    private Avance(decimal objetivo, decimal logrado, int diasLaborablesRestantes)
    {
        Objetivo = objetivo;
        Logrado = logrado;
        DiasLaborablesRestantes = diasLaborablesRestantes;
    }

    public decimal Objetivo { get; }

    /// <summary>Lo ganado en el mes. Por **fecha de cierre**, no de creación: cuenta cuando se firma.</summary>
    public decimal Logrado { get; }

    /// <summary>Hoy incluido, si hoy es laborable. Cero cuando el mes ya terminó.</summary>
    public int DiasLaborablesRestantes { get; }

    /// <summary>Lo que falta. Nunca negativo: pasarse del objetivo no deja «−4.000 € pendientes».</summary>
    public decimal Falta => Math.Max(0m, Objetivo - Logrado);

    /// <summary>
    /// El porcentaje, redondeado y **sin techo**. Un 140 % es un dato correcto y enseñarlo como 100 %
    /// sería esconder el mejor mes del año. (Al contrario que la conversión del embudo, donde pasar del
    /// 100 % era un fallo de cálculo: aquí no hay nada que impida vender más de lo previsto.)
    /// </summary>
    public int Porcentaje => Objetivo <= 0m ? 0 : (int)Math.Round(Logrado * 100m / Objetivo);

    public bool Cumplido => Logrado >= Objetivo;

    /// <summary>
    /// Cuánto hay que cerrar cada día laborable que queda para llegar. Nulo cuando ya está cumplido —no
    /// hay nada que repartir— y también cuando **no quedan días**: en un mes ya acabado, «te faltan
    /// 18.400 € al día» sería una cifra sin sentido puesta al lado de un mes cerrado.
    /// </summary>
    public decimal? PorDiaQueQueda => Cumplido || DiasLaborablesRestantes <= 0
        ? null
        : decimal.Round(Falta / DiasLaborablesRestantes, 0);

    public static Avance De(decimal objetivo, decimal logrado, DateOnly hoy, DateOnly mes) =>
        new(objetivo, logrado, DiasLaborablesQueQuedan(hoy, mes));

    /// <summary>
    /// Días de lunes a viernes desde hoy hasta el final del mes, hoy incluido.
    ///
    /// Sin festivos, por lo mismo que <see cref="HorasLaborables"/>: cada comunidad y cada municipio
    /// tienen los suyos, y equivocarse un día cuesta mucho menos que mantener catorce calendarios.
    ///
    /// Si <paramref name="hoy"/> no es de ese mes se devuelve 0 y no el mes entero: mirar el objetivo de
    /// noviembre en septiembre tiene que decir «no quedan días de ese mes por trabajar todavía», no
    /// repartir el importe entre veintiún días que aún no han empezado.
    /// </summary>
    public static int DiasLaborablesQueQuedan(DateOnly hoy, DateOnly mes)
    {
        // Con el nombre completo: la propiedad `Objetivo` de este mismo registro tapa a la clase, y sin
        // cualificarlo el compilador entiende que se le pide el mes de un importe.
        var primero = Matchketing.Objetivos.Dominio.Objetivo.MesDe(mes);
        if (Matchketing.Objetivos.Dominio.Objetivo.MesDe(hoy) != primero)
        {
            return 0;
        }

        var ultimo = primero.AddMonths(1).AddDays(-1);
        var cuantos = 0;

        for (var dia = hoy; dia <= ultimo; dia = dia.AddDays(1))
        {
            if (dia.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                cuantos++;
            }
        }

        return cuantos;
    }
}
