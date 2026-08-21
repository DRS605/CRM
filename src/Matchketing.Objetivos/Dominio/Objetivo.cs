using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Objetivos.Dominio;

/// <summary>
/// Lo que una persona se compromete a vender en un mes.
///
/// El módulo entero cabe en una frase, y aun así cambia las tres pantallas que más se miran: Informes y
/// el resumen del repaso dicen **qué pasó**, y sin un objetivo no hay forma de saber si eso era
/// suficiente. Un número al lado convierte un informe en una herramienta.
///
/// Cuatro decisiones, y las cuatro son sobre lo que **no** hace:
///
/// 1. **Un objetivo es de un mes concreto, no «el objetivo».** Guardar un solo número en la empresa
///    habría sido menos código y habría reescrito la historia: el día que se sube el objetivo, todos
///    los meses anteriores pasarían a estar incumplidos de golpe. Un compromiso tiene fecha.
/// 2. **Solo dinero ganado.** No hay objetivo de llamadas, ni de correos, ni de oportunidades creadas.
///    Un objetivo sobre actividad se cumple haciendo actividad, y así es exactamente como se enseña a
///    un equipo a rellenar el CRM en vez de vender. Aquí la actividad la propone el sistema —eso es
///    Hoy y el repaso— y lo que se le pide a la persona es el resultado.
/// 3. **No se inventa ninguno.** Sin objetivo puesto, las pantallas no enseñan la línea. Un objetivo
///    por defecto sería un número sin motivo, que en esta interfaz es lo mismo que una mentira.
/// 4. **No regaña.** Un objetivo incumplido se enseña como número y como barra, sin adjetivos. Es la
///    misma regla que el resumen semanal: «una semana floja se cuenta sin regañar».
/// </summary>
public sealed class Objetivo : RaizAgregadoEmpresa<Guid>
{
    /// <summary>
    /// Un millón de euros al mes. No es una opinión sobre nadie: es que a partir de ahí lo que hay es
    /// un cero de más al teclear, y un objetivo mal escrito hace que la barra de todo el equipo no
    /// signifique nada durante un mes.
    /// </summary>
    public const decimal ImporteMaximo = 1_000_000m;

    private Objetivo(Guid id)
        : base(id, Guid.Empty)
    {
    }

    private Objetivo(Guid id, Guid empresaId, Guid usuarioId, DateOnly mes, decimal importe, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        UsuarioId = usuarioId;
        Mes = mes;
        Importe = importe;
        FijadoEn = ahora;
    }

    /// <summary>De quién es el objetivo. Uno por persona y mes.</summary>
    public Guid UsuarioId { get; private set; }

    /// <summary>
    /// El mes, siempre como el **día 1**. Guardar un mes como fecha y no como dos enteros permite
    /// ordenarlo y compararlo sin escribir aritmética de calendario en cada consulta; normalizarlo al
    /// día 1 es lo que evita que «agosto» y «agosto» sean dos filas distintas.
    /// </summary>
    public DateOnly Mes { get; private set; }

    public decimal Importe { get; private set; }

    public DateTimeOffset FijadoEn { get; private set; }

    /// <summary>El mes de una fecha cualquiera, normalizado al día 1.</summary>
    public static DateOnly MesDe(DateOnly dia) => new(dia.Year, dia.Month, 1);

    public static DateOnly MesDe(DateTimeOffset instante) =>
        MesDe(HorasLaborables.DiaDeTrabajo(instante));

    public static Resultado<Objetivo> Fijar(
        Guid empresaId, Guid usuarioId, DateOnly mes, decimal importe, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (usuarioId == Guid.Empty)
        {
            return Resultado.Fallo<Objetivo>(Error.Validacion(
                "objetivo.sin_persona", "Un objetivo es de alguien."));
        }

        var comprobado = Comprobar(mes, importe, reloj);
        return comprobado.Fallido
            ? Resultado.Fallo<Objetivo>(comprobado.Error!)
            : Resultado.Ok(new Objetivo(Guid.NewGuid(), empresaId, usuarioId, MesDe(mes), importe, reloj.AhoraUtc));
    }

    /// <summary>
    /// Cambia el importe. Se puede, y a propósito: los objetivos se revisan a mitad de mes en la vida
    /// real, y prohibirlo solo conseguiría que se llevaran en una hoja aparte.
    /// </summary>
    public Resultado Cambiar(decimal importe, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var comprobado = Comprobar(Mes, importe, reloj);
        if (comprobado.Fallido)
        {
            return comprobado;
        }

        Importe = importe;
        FijadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>
    /// Las dos reglas del importe y la del mes, juntas porque se comprueban en los dos sitios.
    ///
    /// La del mes es la que importa: **el pasado no se toca**. Poner en marzo el objetivo de enero es
    /// escribir la historia después de conocerla, y un histórico que se puede retocar no sirve para
    /// nada —ni para el que lo mira ni para el que lo cumplió—.
    /// </summary>
    private static Resultado Comprobar(DateOnly mes, decimal importe, IReloj reloj)
    {
        if (importe <= 0m)
        {
            return Resultado.Fallo(Error.Validacion(
                "objetivo.importe_invalido", "Un objetivo de cero no es un objetivo."));
        }

        if (importe > ImporteMaximo)
        {
            return Resultado.Fallo(Error.Validacion(
                "objetivo.importe_enorme",
                "Ese objetivo parece un cero de más. El máximo por persona y mes es un millón de euros."));
        }

        return MesDe(mes) < MesDe(reloj.AhoraUtc)
            ? Resultado.Fallo(Error.Validacion(
                "objetivo.mes_pasado",
                "El objetivo de un mes que ya pasó no se puede tocar. Lo que se cumplió, se cumplió."))
            : Resultado.Ok();
    }
}
