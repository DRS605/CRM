using Matchketing.Nucleo.Comun;
using Matchketing.Repaso.Dominio;

namespace Matchketing.Repaso.Aplicacion;

/// <summary>
/// Una tarjeta del repaso. Lleva **la pregunta ya escrita** y las respuestas posibles: el cliente no
/// tiene que saber redactar nada ni decidir qué se puede contestar a qué.
/// </summary>
public sealed record Pregunta(
    string Clave,
    TipoPregunta Tipo,
    string Titular,
    string Detalle,
    Guid? ContactoId,
    string? NombreContacto,
    string? Telefono,
    Guid? OportunidadId,
    Guid? TareaId,
    decimal? Importe,
    int? Match,
    IReadOnlyList<Opcion> Opciones);

/// <summary>
/// La pila del repaso. <paramref name="Total"/> puede ser mayor que las preguntas devueltas: la pila
/// se corta a propósito, y decirlo es más honesto que servir doscientas tarjetas y dejar que la
/// persona descubra sola que esto no se acaba.
/// </summary>
public sealed record PilaRepaso(
    IReadOnlyList<Pregunta> Preguntas,
    int Total,
    int SegundosEstimados)
{
    /// <summary>Nada que preguntar. Se dice y se para, como en Hoy.</summary>
    public bool AlDia => Total == 0;
}

/// <summary>
/// Lo que ha pasado con la respuesta, para que la persona vea que su toque hizo algo: «Tarea
/// cerrada», «Ganada: 8.400 €».
///
/// No devuelve cuántas preguntas quedan a propósito. Contarlas obligaría a rehacer todas las consultas
/// de la pila en cada toque —seis consultas por cada tres segundos de trabajo—, y el cliente ya sabe
/// cuántas le quedan porque tiene la pila delante. Cuando la vacía, pide la siguiente y ahí se corrige
/// cualquier desajuste: ganar una oportunidad puede haber tumbado también su tarjeta de cierre pasado.
/// </summary>
public sealed record Resuelta(string Clave, string Efecto);

/// <summary>
/// La semana del comercial, contada para él.
///
/// Existe para romper la asimetría que mata los CRM: el coste lo paga quien introduce los datos y el
/// valor se lo lleva quien lee los informes. Aquí, al acabar el repaso, quien lo hizo ve sus propios
/// números. No es vigilancia —es de él y solo lo ve él— y es lo que hace que vuelva el viernes
/// siguiente.
/// </summary>
public sealed record ResumenSemana(
    int Dias,
    int Llamadas,
    int LlamadasSemanaAnterior,
    int ContactosNuevos,
    int OportunidadesAbiertas,
    int Ganadas,
    decimal ImporteGanado,
    int Perdidas,
    int TareasCerradas,
    int PreguntasResueltas)
{
    /// <summary>De las que se cerraron esta semana, cuántas se ganaron. Nulo si no se cerró ninguna.</summary>
    public int? RatioCierre => Ganadas + Perdidas == 0
        ? null
        : (int)Math.Round(Ganadas * 100m / (Ganadas + Perdidas));

    /// <summary>
    /// Una frase para el final del repaso. Nunca regaña: si la semana ha sido floja, lo dice sin
    /// adjetivos y ya está. Un CRM que echa la culpa se cierra y no se vuelve a abrir.
    /// </summary>
    public string Titular => (Ganadas, Llamadas) switch
    {
        (> 0, _) => $"Has cerrado {Ganadas} {(Ganadas == 1 ? "venta" : "ventas")} por {Castellano.Euros(ImporteGanado)}.",
        (0, > 0) => $"{Llamadas} {(Llamadas == 1 ? "llamada" : "llamadas")} esta semana. Ninguna cerrada todavía.",
        _ => "Semana tranquila.",
    };
}
