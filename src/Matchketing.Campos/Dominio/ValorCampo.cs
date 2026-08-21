using System.Globalization;
using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Campos.Dominio;

/// <summary>
/// Lo que vale un campo propio para un contacto o una cuenta concretos.
///
/// El valor se guarda **en una sola columna de texto**, no en cinco columnas tipadas. La decisión tiene
/// un coste y conviene que se sepa cuál:
///
/// · A favor: una fila por valor, sin cuatro nulos al lado, y sin un `switch` sobre el tipo en cada
///   lectura. El dominio normaliza al guardar —una fecha siempre `aaaa-mm-dd`, un número siempre con
///   punto decimal— así que lo que hay dentro es predecible y comparable como texto.
/// · En contra: **no se puede filtrar ni ordenar por un campo propio**, porque «12» y «100» ordenados
///   como texto salen al revés. Por eso este módulo no ofrece filtrar por ellos y lo dice claro; el día
///   que alguien lo pida, es una columna tipada más y una migración, no un rediseño.
///
/// Un valor vacío **no se guarda**: se borra la fila. Una fila con la cadena vacía y una fila que no
/// existe significan lo mismo para quien lee, y tener las dos formas de decir «no hay dato» garantiza
/// que algún día una pantalla enseñe «—» y otra enseñe nada.
/// </summary>
public sealed class ValorCampo : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaTexto = 500;

    private ValorCampo(Guid id)
        : base(id, Guid.Empty) => Texto = null!;

    private ValorCampo(Guid id, Guid empresaId, Guid campoId, Ambito ambito, Guid entidadId, string texto, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        CampoId = campoId;
        Ambito = ambito;
        EntidadId = entidadId;
        Texto = texto;
        ActualizadoEn = ahora;
    }

    public Guid CampoId { get; private set; }

    /// <summary>
    /// Se repite el ámbito, que ya está en el campo. Es a propósito: sin él, borrar los valores de un
    /// contacto obligaría a cruzar con la tabla de campos para saber cuáles son suyos, y esa consulta
    /// está en el camino de la supresión del artículo 17. Ahí no se juega con las uniones.
    /// </summary>
    public Ambito Ambito { get; private set; }

    /// <summary>El contacto o la cuenta. Sin clave ajena, como el resto de las referencias entre módulos.</summary>
    public Guid EntidadId { get; private set; }

    public string Texto { get; private set; }

    public DateTimeOffset ActualizadoEn { get; private set; }

    public static Resultado<ValorCampo> Crear(
        Guid empresaId, CampoPropio campo, Guid entidadId, string? valor, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(campo);
        ArgumentNullException.ThrowIfNull(reloj);

        if (entidadId == Guid.Empty)
        {
            return Resultado.Fallo<ValorCampo>(Error.Validacion(
                "valor.sin_entidad", "Un valor es de alguien."));
        }

        var normalizado = Normalizar(campo, valor);
        return normalizado.Fallido
            ? Resultado.Fallo<ValorCampo>(normalizado.Error!)
            : Resultado.Ok(new ValorCampo(
                Guid.NewGuid(), empresaId, campo.Id, campo.Ambito, entidadId, normalizado.Valor, reloj.AhoraUtc));
    }

    public Resultado Cambiar(CampoPropio campo, string? valor, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(campo);
        ArgumentNullException.ThrowIfNull(reloj);

        var normalizado = Normalizar(campo, valor);
        if (normalizado.Fallido)
        {
            return normalizado;
        }

        Texto = normalizado.Valor;
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>
    /// Deja el valor escrito de una forma y solo una, según el tipo del campo.
    ///
    /// Normalizar al guardar y no al leer es lo que hace que la columna de texto sirva: si un usuario
    /// escribe «3,5» y otro «3.5», guardarlos tal cual daría dos valores distintos para el mismo número,
    /// y la exportación saldría con las dos formas mezcladas.
    /// </summary>
    public static Resultado<string> Normalizar(CampoPropio campo, string? valor)
    {
        ArgumentNullException.ThrowIfNull(campo);

        var crudo = (valor ?? string.Empty).Trim();
        if (crudo.Length == 0)
        {
            return Resultado.Fallo<string>(Error.Validacion(
                "valor.vacio", "Un valor vacío no se guarda: se quita el dato."));
        }

        switch (campo.Tipo)
        {
            case TipoCampo.Texto:
                return crudo.Length > LongitudMaximaTexto
                    ? Resultado.Fallo<string>(Error.Validacion(
                        "valor.texto_largo", $"No puede pasar de {LongitudMaximaTexto} caracteres."))
                    : Resultado.Ok(crudo);

            case TipoCampo.Numero:
                // Se aceptan las dos comas decimales porque las dos se teclean en España, y se guarda
                // siempre con punto: la cultura invariante es la única que existe aquí
                // —`InvariantGlobalization`— y es la que va a leer la exportación.
                var conPunto = crudo.Replace(',', '.');
                return decimal.TryParse(conPunto, NumberStyles.Number, CultureInfo.InvariantCulture, out var numero)
                    ? Resultado.Ok(numero.ToString(CultureInfo.InvariantCulture))
                    : Resultado.Fallo<string>(Error.Validacion("valor.no_es_numero", $"«{crudo}» no es un número."));

            case TipoCampo.Fecha:
                // Solo `aaaa-mm-dd`, que es lo que manda un `<input type="date">`. Aceptar «12/03/2026»
                // obligaría a decidir si el 12 es el día o el mes, y esa decisión no se puede acertar.
                return DateOnly.TryParseExact(crudo, "yyyy-MM-dd", out var fecha)
                    ? Resultado.Ok(fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    : Resultado.Fallo<string>(Error.Validacion(
                        "valor.no_es_fecha", $"«{crudo}» no es una fecha. Se escribe como 2026-03-12."));

            case TipoCampo.SiNo:
                return Verdadero(crudo) is { } si
                    ? Resultado.Ok(si ? "si" : "no")
                    : Resultado.Fallo<string>(Error.Validacion("valor.no_es_si_ni_no", $"«{crudo}» no es sí ni no."));

            case TipoCampo.Lista:
                // Se devuelve **la opción tal como está escrita en el campo**, no como la escribió quien
                // rellenó: así todos los valores de esa lista son idénticos y se pueden agrupar. Sin
                // esto, «Gas» y «gas» serían dos grupos en cualquier recuento futuro.
                var cual = campo.Opciones.FirstOrDefault(o =>
                    Nucleo.Comun.Castellano.SinAcentos(o) == Nucleo.Comun.Castellano.SinAcentos(crudo));

                return cual is null
                    ? Resultado.Fallo<string>(Error.Validacion(
                        "valor.fuera_de_la_lista",
                        $"«{crudo}» no es una de las opciones: {string.Join(", ", campo.Opciones)}."))
                    : Resultado.Ok(cual);

            default:
                return Resultado.Fallo<string>(Error.Validacion("campo.tipo_invalido", "Ese tipo no existe."));
        }
    }

    /// <summary>
    /// Sí o no, escrito de las formas en que la gente lo escribe. Nulo si no es ninguna de ellas.
    /// </summary>
    private static bool? Verdadero(string crudo) => Nucleo.Comun.Castellano.SinAcentos(crudo) switch
    {
        "si" or "s" or "true" or "1" or "verdadero" => true,
        "no" or "n" or "false" or "0" or "falso" => false,
        _ => null,
    };
}
