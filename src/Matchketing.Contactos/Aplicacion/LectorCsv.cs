namespace Matchketing.Contactos.Aplicacion;

/// <summary>
/// Lector de CSV tolerante: detecta el separador (`;`, `,` o tabulador), respeta los campos
/// entrecomillados y reconoce las columnas por nombre, sin importar el orden ni los acentos. Un
/// cliente exporta de Excel o de la agenda del móvil; no le vamos a pedir un formato exacto.
/// </summary>
public static class LectorCsv
{
    public static char DetectarSeparador(string primeraLinea)
    {
        ArgumentNullException.ThrowIfNull(primeraLinea);

        var candidatos = new[] { ';', ',', '\t' };
        var mejor = ';';
        var maximo = -1;

        foreach (var c in candidatos)
        {
            var cuenta = primeraLinea.Count(x => x == c);
            if (cuenta > maximo)
            {
                maximo = cuenta;
                mejor = c;
            }
        }

        return mejor;
    }

    public static IReadOnlyList<string> PartirLinea(string linea, char separador)
    {
        ArgumentNullException.ThrowIfNull(linea);

        var campos = new List<string>();
        var actual = new System.Text.StringBuilder();
        var entreComillas = false;

        for (var i = 0; i < linea.Length; i++)
        {
            var c = linea[i];

            if (entreComillas)
            {
                if (c == '"')
                {
                    if (i + 1 < linea.Length && linea[i + 1] == '"')
                    {
                        actual.Append('"');
                        i++;
                    }
                    else
                    {
                        entreComillas = false;
                    }
                }
                else
                {
                    actual.Append(c);
                }
            }
            else if (c == '"')
            {
                entreComillas = true;
            }
            else if (c == separador)
            {
                campos.Add(actual.ToString().Trim());
                actual.Clear();
            }
            else
            {
                actual.Append(c);
            }
        }

        campos.Add(actual.ToString().Trim());
        return campos;
    }

    /// <summary>
    /// Quita acentos y pasa a minúsculas, para que «Teléfono» y «telefono» sean la misma columna.
    /// El mapa vive en <see cref="Matchketing.Nucleo.Comun.Castellano.SinAcentos"/>: lo necesitan
    /// también las claves de los campos propios, y dos mapas de acentos son dos mapas que se separan.
    /// </summary>
    public static string NormalizarCabecera(string valor) =>
        Matchketing.Nucleo.Comun.Castellano.SinAcentos(valor);

    /// <summary>Devuelve el índice de la primera cabecera que coincida con alguno de los alias.</summary>
    public static int IndiceDe(IReadOnlyList<string> cabeceras, params string[] alias)
    {
        ArgumentNullException.ThrowIfNull(cabeceras);
        ArgumentNullException.ThrowIfNull(alias);

        for (var i = 0; i < cabeceras.Count; i++)
        {
            var c = NormalizarCabecera(cabeceras[i]);
            if (alias.Any(a => string.Equals(c, a, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return -1;
    }
}
