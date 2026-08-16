namespace Matchketing.Match.Dominio;

/// <summary>
/// Un comercial con lo que hace falta para saber si este lead es para él. Los números vienen de su
/// histórico real, no de una configuración.
/// </summary>
public sealed record CandidatoComercial(
    Guid UsuarioId,
    string Nombre,
    IReadOnlyList<string> Zonas,
    int GanadasEnSector,
    int CerradasEnSector,
    int OportunidadesAbiertas,
    double? HorasPrimeraRespuesta);

public sealed record AsignacionPropuesta(Guid UsuarioId, string Nombre, int Puntos, IReadOnlyList<string> Motivos);

/// <summary>
/// El match **lead ↔ comercial**. Donde HubSpot reparte por turnos, aquí se reparte por afinidad
/// real: quién cubre esa zona, quién cierra ese sector, quién tiene hueco y quién responde rápido.
///
/// Y se explica, como todo lo demás.
/// </summary>
public static class Repartidor
{
    public const int PesoZona = 30;
    public const int PesoAfinidad = 30;
    public const int PesoCarga = 20;
    public const int PesoVelocidad = 20;

    public static AsignacionPropuesta? Repartir(IReadOnlyList<CandidatoComercial> candidatos, string? provincia, string? sector)
    {
        ArgumentNullException.ThrowIfNull(candidatos);

        if (candidatos.Count == 0)
        {
            return null;
        }

        // M4: quien no tiene histórico arranca con la media del equipo. No se penaliza a la persona
        // nueva por serlo; eso la condenaría a no recibir nunca un lead bueno.
        var conHistorico = candidatos.Where(c => c.CerradasEnSector > 0).ToList();
        var tasaMedia = conHistorico.Count > 0
            ? conHistorico.Average(c => (double)c.GanadasEnSector / c.CerradasEnSector)
            : 0.5;

        var conVelocidad = candidatos.Where(c => c.HorasPrimeraRespuesta is not null).ToList();
        var velocidadMedia = conVelocidad.Count > 0
            ? conVelocidad.Average(c => c.HorasPrimeraRespuesta!.Value)
            : 4.0;

        var cargaMaxima = Math.Max(1, candidatos.Max(c => c.OportunidadesAbiertas));
        var velocidadMaxima = Math.Max(0.5, candidatos.Max(c => c.HorasPrimeraRespuesta ?? velocidadMedia));

        var mejores = candidatos
            .Select(c => Puntuar(c, provincia, sector, tasaMedia, velocidadMedia, cargaMaxima, velocidadMaxima))
            .OrderByDescending(x => x.Propuesta.Puntos)
            // Empate: gana quien menos carga tiene. Repartir trabajo también es repartir atención.
            .ThenBy(x => x.Carga)
            .ThenBy(x => x.Propuesta.Nombre, StringComparer.Ordinal)
            .ToList();

        return mejores[0].Propuesta;
    }

    private static (AsignacionPropuesta Propuesta, int Carga) Puntuar(
        CandidatoComercial c, string? provincia, string? sector,
        double tasaMedia, double velocidadMedia, int cargaMaxima, double velocidadMaxima)
    {
        var puntos = 0;
        var motivos = new List<string>();

        if (provincia is not null && c.Zonas.Contains(provincia, StringComparer.OrdinalIgnoreCase))
        {
            puntos += PesoZona;
            motivos.Add($"lleva {provincia}");
        }

        var tasa = c.CerradasEnSector > 0 ? (double)c.GanadasEnSector / c.CerradasEnSector : tasaMedia;
        var puntosAfinidad = (int)Math.Round(tasa * PesoAfinidad, MidpointRounding.AwayFromZero);
        puntos += puntosAfinidad;
        if (c.CerradasEnSector > 0 && sector is not null && tasa > tasaMedia)
        {
            motivos.Add($"cierra el {Math.Round(tasa * 100)} % en {sector.ToLowerInvariant()}");
        }

        var puntosCarga = (int)Math.Round(PesoCarga * (1 - ((double)c.OportunidadesAbiertas / cargaMaxima)), MidpointRounding.AwayFromZero);
        puntos += puntosCarga;
        if (c.OportunidadesAbiertas == 0)
        {
            motivos.Add("tiene hueco");
        }

        var horas = c.HorasPrimeraRespuesta ?? velocidadMedia;
        var puntosVelocidad = (int)Math.Round(PesoVelocidad * (1 - (horas / velocidadMaxima)), MidpointRounding.AwayFromZero);
        puntos += puntosVelocidad;
        if (c.HorasPrimeraRespuesta is { } h && h < velocidadMedia)
        {
            motivos.Add($"responde en {Math.Round(h, 1)} h de media");
        }

        if (motivos.Count == 0)
        {
            motivos.Add("es quien mejor encaja de los disponibles");
        }

        return (new AsignacionPropuesta(c.UsuarioId, c.Nombre, Math.Clamp(puntos, 0, 100), motivos), c.OportunidadesAbiertas);
    }
}
