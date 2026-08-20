using Matchketing.Nucleo.Resultados;

namespace Matchketing.Repaso.Dominio;

/// <summary>
/// Por qué el sistema pregunta. Cada tipo nace de algo que **ya está en la base de datos** y que no
/// cuadra: una tarea que venció, un lead que nadie tocó, una oportunidad parada. Ninguno pide un dato
/// nuevo; todos piden una decisión que el comercial ya tiene tomada en la cabeza.
///
/// El orden del enum es el orden en que se pregunta, y no es arbitrario: primero lo que rompe una
/// promesa (dijiste que lo harías), después lo que tiene dinero encima, y al final los avisos.
/// </summary>
public enum TipoPregunta
{
    /// <summary>Dijiste que harías esto y la fecha pasó.</summary>
    TareaVencida = 1,

    /// <summary>Se te asignó un lead y no consta ni una llamada, ni un correo, ni una reunión.</summary>
    LeadSinTocar = 2,

    /// <summary>La fecha de cierre que pusiste ya pasó y la oportunidad sigue abierta.</summary>
    CierrePasado = 3,

    /// <summary>Lleva en la misma etapa más días de los que esa etapa tolera.</summary>
    OportunidadEstancada = 4,

    /// <summary>Match alto y mucho tiempo sin saber nada. Es el aviso más rentable del sistema.</summary>
    SilencioCaliente = 5,

    /// <summary>Le vendiste y ahí se quedó. En una pyme, la recomendación es el primer canal.</summary>
    ClienteSinSiguientePaso = 6,

    /// <summary>
    /// Le escribiste y no ha contestado.
    ///
    /// Es la pregunta que el repaso no podía hacer hasta que hubo correo dentro, y probablemente la más
    /// rentable de las siete: un correo sin respuesta es la situación comercial más común que existe y
    /// la que más se queda sin resolver, porque no genera ninguna tarea ni ninguna alerta. Nadie apunta
    /// «volver a llamar a quien no me contestó».
    ///
    /// Y si además consta que lo **abrió** y no contestó, la pregunta se vuelve muy distinta: ahí no hay
    /// duda de que le interesó lo suficiente para abrirlo. Por eso una apertura tiene tipo de actividad
    /// propio y no cuenta como respuesta.
    /// </summary>
    CorreoSinRespuesta = 7,
}

/// <summary>
/// Lo que el comercial puede contestar. Es un catálogo cerrado a propósito: en cuanto haya una
/// respuesta que exija escribir algo, el repaso deja de durar minutos.
/// </summary>
public enum Respuesta
{
    Hecha = 1,
    AunNo = 2,
    YaNoHaceFalta = 3,

    Contactado = 4,
    NoContesta = 5,
    NoLeInteresa = 6,

    SigueViva = 7,
    Ganada = 8,
    Perdida = 9,
    OtraFecha = 10,

    LlamarHoy = 11,
    DejarloEstar = 12,

    /// <summary>
    /// «Ya me contestó.» Apunta la respuesta en la cronología, que es lo que hace que no se vuelva a
    /// preguntar y lo que además la deja registrada donde tiene que estar.
    ///
    /// Existe porque sin ella la pregunta del correo sin respuesta no se podría cerrar diciendo la
    /// verdad: el comercial que ha recibido la respuesta en su buzón —no aquí— solo podría contestar
    /// «déjalo estar», y entonces el sistema seguiría creyendo que nadie contestó.
    /// </summary>
    YaContesto = 13,

    /// <summary>«Ahora no.» Vale para cualquier pregunta y no cambia nada del negocio.</summary>
    Saltar = 99,
}

/// <summary>
/// Una respuesta posible, tal como se pinta. <paramref name="Principal"/> marca la que se ofrece
/// destacada —y solo puede haber una—; <paramref name="PideMotivo"/> marca las que **no** se resuelven
/// de un toque, porque su consecuencia no es reversible.
/// </summary>
public sealed record Opcion(Respuesta Respuesta, string Etiqueta, bool Principal = false, bool PideMotivo = false);

/// <summary>
/// El catálogo de qué se puede contestar a qué, y la invariante que lo sujeta.
///
/// **R1 — una respuesta que no pertenece a la pregunta se rechaza.** Sin esto, el cliente podría
/// mandar «Ganada» a una tarea vencida y el servidor buscaría una oportunidad que no existe. Es la
/// clase de fallo que solo aparece cuando alguien escribe un cliente nuevo.
///
/// **R2 — nada irreversible en un toque.** Perder una oportunidad exige el motivo, que además es el
/// dato que alimenta el único informe que de verdad cambia decisiones. Ganar sí es de un toque: es
/// buena noticia, y deshacerla es una conversación con el jefe, no un botón.
/// </summary>
public static class Opciones
{
    private static readonly Opcion Saltar = new(Respuesta.Saltar, "Ahora no");

    public static IReadOnlyList<Opcion> De(TipoPregunta tipo) => tipo switch
    {
        TipoPregunta.TareaVencida =>
        [
            new(Respuesta.Hecha, "Hecha", Principal: true),
            new(Respuesta.AunNo, "Aún no"),
            new(Respuesta.YaNoHaceFalta, "Ya no hace falta"),
        ],
        TipoPregunta.LeadSinTocar =>
        [
            new(Respuesta.Contactado, "Hablé con él", Principal: true),
            new(Respuesta.NoContesta, "No contesta"),
            new(Respuesta.NoLeInteresa, "No le interesa"),
        ],
        TipoPregunta.CierrePasado =>
        [
            new(Respuesta.OtraFecha, "Se retrasa dos semanas", Principal: true),
            new(Respuesta.Ganada, "Ganada"),
            new(Respuesta.Perdida, "Perdida", PideMotivo: true),
        ],
        TipoPregunta.OportunidadEstancada =>
        [
            new(Respuesta.SigueViva, "Sigue viva", Principal: true),
            new(Respuesta.Ganada, "Ganada"),
            new(Respuesta.Perdida, "Perdida", PideMotivo: true),
        ],
        TipoPregunta.SilencioCaliente =>
        [
            new(Respuesta.LlamarHoy, "Le llamo hoy", Principal: true),
            new(Respuesta.DejarloEstar, "Déjalo estar"),
        ],
        TipoPregunta.ClienteSinSiguientePaso =>
        [
            new(Respuesta.LlamarHoy, "Le llamo hoy", Principal: true),
            new(Respuesta.DejarloEstar, "Déjalo estar"),
        ],
        TipoPregunta.CorreoSinRespuesta =>
        [
            // «Le llamo» va primero y no «vuelvo a escribirle» a propósito: si el primer correo no ha
            // funcionado, el segundo correo casi nunca funciona. Lo que cambia el resultado es el
            // teléfono, y el repaso está para proponer lo que funciona, no lo que es más cómodo.
            new(Respuesta.LlamarHoy, "Le llamo hoy", Principal: true),
            new(Respuesta.YaContesto, "Ya me contestó"),
            new(Respuesta.DejarloEstar, "Déjalo estar"),
        ],
        _ => [],
    };

    /// <summary>Las opciones más «Ahora no», que vale para todas y por eso no está en el catálogo.</summary>
    public static IReadOnlyList<Opcion> ConSaltar(TipoPregunta tipo) => [.. De(tipo), Saltar];

    /// <summary>R1: comprueba que la respuesta es de esa pregunta.</summary>
    public static Resultado<Opcion> Comprobar(TipoPregunta tipo, Respuesta respuesta)
    {
        var opcion = ConSaltar(tipo).FirstOrDefault(o => o.Respuesta == respuesta);

        return opcion is null
            ? Resultado.Fallo<Opcion>(Error.Validacion(
                "repaso.respuesta_no_valida", $"«{respuesta}» no es una respuesta posible a esa pregunta."))
            : Resultado.Ok(opcion);
    }
}
