using System.Globalization;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Automatizacion.Dominio;

/// <summary>
/// Una cosa que hacer. Los parámetros van en dos campos genéricos —un texto y un número— en vez de en
/// un registro por tipo de acción.
///
/// Con cuatro acciones y un parámetro y medio cada una, una jerarquía de cuatro clases con su
/// serialización polimórfica sería más código del que ahorra. Lo que sí hace falta es que
/// <see cref="Validar"/> exija lo que cada tipo necesita, y que el mensaje de error diga qué falta: eso
/// es lo que impide guardar una acción que no puede funcionar.
/// </summary>
public sealed record Accion(TipoAccion Tipo, string? Texto, Guid? Referencia, int? Numero)
{
    /// <summary>Cuánto se puede aplazar una tarea creada por una regla. Un año es de sobra.</summary>
    public const int DiasMaximos = 365;

    public static Accion Tarea(string titulo, int dias) => new(TipoAccion.CrearTarea, titulo, null, dias);

    public static Accion Asignar(Guid usuarioId) => new(TipoAccion.AsignarComercial, null, usuarioId, null);

    public static Accion Correo(Guid plantillaId) => new(TipoAccion.MandarCorreo, null, plantillaId, null);

    public static Accion Nota(string texto) => new(TipoAccion.ApuntarNota, texto, null, null);

    public string Leer() => Tipo switch
    {
        TipoAccion.CrearTarea => Numero is 0
            ? $"crear la tarea «{Texto}» para hoy"
            : $"crear la tarea «{Texto}» para dentro de {Numero} días",
        TipoAccion.AsignarComercial => "asignárselo a un comercial",
        TipoAccion.MandarCorreo => "mandarle un correo con una plantilla",
        _ => $"apuntar «{Recortar(Texto)}»",
    };

    public Resultado Validar() => Tipo switch
    {
        TipoAccion.CrearTarea when string.IsNullOrWhiteSpace(Texto) =>
            Resultado.Fallo(Error.Validacion("regla.tarea_sin_titulo", "La tarea necesita un título.")),

        TipoAccion.CrearTarea when Numero is null or < 0 or > DiasMaximos =>
            Resultado.Fallo(Error.Validacion(
                "regla.tarea_plazo_invalido", $"El plazo de la tarea va de 0 a {DiasMaximos} días.")),

        TipoAccion.AsignarComercial when Referencia is null || Referencia == Guid.Empty =>
            Resultado.Fallo(Error.Validacion("regla.sin_comercial", "Elige a qué comercial se le asigna.")),

        TipoAccion.MandarCorreo when Referencia is null || Referencia == Guid.Empty =>
            Resultado.Fallo(Error.Validacion("regla.sin_plantilla", "Elige con qué plantilla se manda.")),

        TipoAccion.ApuntarNota when string.IsNullOrWhiteSpace(Texto) =>
            Resultado.Fallo(Error.Validacion("regla.nota_vacia", "La nota no puede estar vacía.")),

        _ => Resultado.Ok(),
    };

    private static string Recortar(string? texto) =>
        texto is null ? string.Empty
        : texto.Length <= 40 ? texto
        : string.Concat(texto.AsSpan(0, 40).TrimEnd(), "…");
}
