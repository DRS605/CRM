using Matchketing.Nucleo.Resultados;

namespace Matchketing.Api.Comun;

/// <summary>Traduce un <see cref="Error"/> de dominio a una respuesta HTTP, con el código estable dentro.</summary>
public static class ResultadosHttp
{
    public static IResult Problema(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var estado = error.Tipo switch
        {
            TipoError.Validacion => StatusCodes.Status400BadRequest,
            TipoError.NoEncontrado => StatusCodes.Status404NotFound,
            TipoError.Conflicto => StatusCodes.Status409Conflict,
            TipoError.NoAutorizado => StatusCodes.Status401Unauthorized,
            TipoError.Prohibido => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Json(new { codigo = error.Codigo, mensaje = error.Mensaje }, statusCode: estado);
    }
}
