namespace Matchketing.Nucleo.Resultados;

/// <summary>Resultado de una operación que puede fallar de forma esperada.</summary>
public class Resultado
{
    protected Resultado(bool exito, Error? error)
    {
        Exito = exito;
        Error = error;
    }

    public bool Exito { get; }

    public bool Fallido => !Exito;

    public Error? Error { get; }

    public static Resultado Ok() => new(true, null);

    public static Resultado Fallo(Error error) => new(false, error);

    public static Resultado<T> Ok<T>(T valor) => Resultado<T>.Correcto(valor);

    public static Resultado<T> Fallo<T>(Error error) => Resultado<T>.Incorrecto(error);
}

/// <summary>Resultado con valor.</summary>
public sealed class Resultado<T> : Resultado
{
    private readonly T? valor;

    private Resultado(bool exito, T? valor, Error? error)
        : base(exito, error)
    {
        this.valor = valor;
    }

    /// <summary>Valor devuelto. Solo se puede leer si la operación fue correcta.</summary>
    public T Valor => Exito
        ? valor!
        : throw new InvalidOperationException("No se puede leer el valor de un resultado fallido.");

    internal static Resultado<T> Correcto(T valor) => new(true, valor, null);

    internal static Resultado<T> Incorrecto(Error error) => new(false, default, error);
}
