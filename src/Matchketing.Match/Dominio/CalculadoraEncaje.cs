namespace Matchketing.Match.Dominio;

/// <summary>
/// Cómo son las oportunidades que **ganas**. Se calcula a partir de tu histórico, no de una
/// plantilla: el cliente ideal de una empresa de climatización en Valencia no es el de nadie más.
/// </summary>
public sealed record PerfilGanadas(
    IReadOnlyList<string> SectoresTop,
    IReadOnlyList<string> ProvinciasConGanadas,
    IReadOnlyList<string> OrigenesBuenos,
    int? TamanoMinimo,
    int? TamanoMaximo,
    int TotalCerradas);

/// <summary>Los datos del contacto que entran en el Encaje.</summary>
public sealed record DatosContacto(
    string? Sector,
    string? Provincia,
    string Origen,
    int? Tamano,
    bool TieneEmail,
    bool TieneTelefono);

/// <summary>
/// El **Encaje**: cuánto se parece este contacto a los que ya te han comprado. Reglas con pesos, no
/// un modelo: tiene que poder explicarse en una frase (invariante M1).
/// </summary>
public static class CalculadoraEncaje
{
    /// <summary>Por debajo de esto no hay histórico del que fiarse.</summary>
    public const int CerradasMinimas = 20;

    /// <summary>Encaje neutro mientras no hay datos. Es preferible decir «no lo sé» a inventar.</summary>
    public const int EncajeNeutro = 50;

    public const int PesoSector = 30;
    public const int PesoProvincia = 20;
    public const int PesoOrigen = 20;
    public const int PesoTamano = 15;
    public const int PesoCalidad = 15;

    public static (int Encaje, IReadOnlyList<Aporte> Aportes, bool SinHistorico) Calcular(DatosContacto datos, PerfilGanadas perfil)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(perfil);

        // M2: sin histórico suficiente, encaje neutro y se dice. No se inventa un número.
        if (perfil.TotalCerradas < CerradasMinimas)
        {
            return (EncajeNeutro,
                [new Aporte("encaje.sin_historico", 0, "Todavía sin histórico para calibrar el encaje")],
                true);
        }

        var aportes = new List<Aporte>();
        var total = 0;

        if (datos.Sector is not null && perfil.SectoresTop.Contains(datos.Sector, StringComparer.OrdinalIgnoreCase))
        {
            total += PesoSector;
            aportes.Add(new Aporte("encaje.sector", PesoSector, $"Encaja con tus clientes de {datos.Sector.ToLowerInvariant()}"));
        }

        if (datos.Provincia is not null && perfil.ProvinciasConGanadas.Contains(datos.Provincia, StringComparer.OrdinalIgnoreCase))
        {
            total += PesoProvincia;
            aportes.Add(new Aporte("encaje.provincia", PesoProvincia, $"Está en {datos.Provincia}, donde ya vendes"));
        }

        if (perfil.OrigenesBuenos.Contains(datos.Origen, StringComparer.OrdinalIgnoreCase))
        {
            total += PesoOrigen;
            aportes.Add(new Aporte("encaje.origen", PesoOrigen, $"Viene de «{datos.Origen}», que te convierte bien"));
        }

        if (datos.Tamano is { } tamano && perfil.TamanoMinimo is { } min && perfil.TamanoMaximo is { } max && tamano >= min && tamano <= max)
        {
            total += PesoTamano;
            aportes.Add(new Aporte("encaje.tamano", PesoTamano, $"Del tamaño que sueles cerrar ({min}–{max} personas)"));
        }

        // Un contacto al que puedes llamar *y* escribir vale más que uno al que solo puedes una cosa.
        if (datos.TieneEmail && datos.TieneTelefono)
        {
            total += PesoCalidad;
            aportes.Add(new Aporte("encaje.calidad", PesoCalidad, "Tienes su correo y su teléfono"));
        }

        return (Math.Clamp(total, 0, 100), aportes, false);
    }
}
