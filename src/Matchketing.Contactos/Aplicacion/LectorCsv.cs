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
    /// Con un mapa explícito y no con <c>Normalize(FormD)</c>: el proyecto compila con
    /// <c>InvariantGlobalization</c>, y en ese modo la normalización Unicode no hace nada. Para
    /// cabeceras en español esto basta y además es determinista.
    /// </summary>
    public static string NormalizarCabecera(string valor)
    {
        ArgumentNullException.ThrowIfNull(valor);

        const string ConAcento = "áàäâãéèëêíìïîóòöôõúùüûñçÁÀÄÂÃÉÈËÊÍÌÏÎÓÒÖÔÕÚÙÜÛÑÇ";
        const string SinAcento = "aaaaaeeeeiiiiooooouuuuncAAAAAEEEEIIIIOOOOOUUUUNC";

        var sb = new System.Text.StringBuilder(valor.Length);
        foreach (var c in valor.Trim())
        {
            var i = ConAcento.IndexOf(c, StringComparison.Ordinal);
            sb.Append(i >= 0 ? SinAcento[i] : c);
        }

        return sb.ToString().ToLowerInvariant();
    }

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
