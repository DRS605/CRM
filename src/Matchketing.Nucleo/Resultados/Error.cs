namespace Matchketing.Nucleo.Resultados;

/// <summary>Tipo de fallo esperado. No se usan excepciones para el flujo normal.</summary>
public enum TipoError
{
    Validacion,
    NoEncontrado,
    Conflicto,
    NoAutorizado,
    Prohibido,
}

/// <summary>Un fallo esperado, con código estable para el cliente y mensaje en español.</summary>
public sealed record Error(string Codigo, string Mensaje, TipoError Tipo)
{
    public static Error Validacion(string codigo, string mensaje) => new(codigo, mensaje, TipoError.Validacion);

    public static Error NoEncontrado(string codigo, string mensaje) => new(codigo, mensaje, TipoError.NoEncontrado);

    public static Error Conflicto(string codigo, string mensaje) => new(codigo, mensaje, TipoError.Conflicto);

    public static Error NoAutorizado(string codigo, string mensaje) => new(codigo, mensaje, TipoError.NoAutorizado);

    public static Error Prohibido(string codigo, string mensaje) => new(codigo, mensaje, TipoError.Prohibido);
}
