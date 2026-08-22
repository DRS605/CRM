using Matchketing.Correo.Aplicacion;
using Matchketing.Correo.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Correo.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; set; } = ahora;

    public void Avanzar(TimeSpan cuanto) => AhoraUtc = AhoraUtc.Add(cuanto);
}

public sealed class ContextoDePrueba(Guid? empresaId, Guid? usuarioId) : IContextoEmpresa
{
    public Guid? EmpresaId { get; } = empresaId;

    public Guid? UsuarioId { get; } = usuarioId;

    public IReadOnlyCollection<string> Permisos => [];

    public bool Tiene(string permiso) => true;
}

public sealed class RepositorioEnMemoria : IRepositorioCorreo
{
    public List<Plantilla> Plantillas { get; } = [];

    public List<Dominio.Correo> Mensajes { get; } = [];

    public Task<Plantilla?> PlantillaAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Plantillas.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Plantilla>> PlantillasAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Plantilla>>(Plantillas.ToList());

    public void Anadir(Plantilla plantilla) => Plantillas.Add(plantilla);

    public void Quitar(Plantilla plantilla) => Plantillas.Remove(plantilla);

    public Task<Dominio.Correo?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Mensajes.FirstOrDefault(c => c.Id == id));

    public Task<Dominio.Correo?> PorTokenAsync(string token, CancellationToken ct = default) =>
        Task.FromResult(Mensajes.FirstOrDefault(c => c.TokenApertura == token));

    public Task<IReadOnlyList<Dominio.Correo>> PendientesAsync(DateTimeOffset hasta, int tope, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Dominio.Correo>>(Mensajes
            .Where(c => c.LeToca(hasta))
            .OrderBy(c => c.CreadoEn)
            .Take(tope)
            .ToList());

    public Task<IReadOnlyList<Dominio.Correo>> DeContactoAsync(Guid contactoId, int cuantos, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Dominio.Correo>>(Mensajes
            .Where(c => c.ContactoId == contactoId)
            .OrderByDescending(c => c.CreadoEn)
            .Take(cuantos)
            .ToList());

    public void AnadirCorreo(Dominio.Correo correo) => Mensajes.Add(correo);
}

/// <summary>Deja escribir o no, según se le diga. Es el gancho con el módulo de cumplimiento.</summary>
public sealed class PermisoDePrueba : IPermisoDeEnvio
{
    public Error? Niega { get; set; }

    public List<(Guid ContactoId, ParaQue ParaQue)> Preguntas { get; } = [];

    public Task<Resultado> PuedeEscribirAsync(Guid contactoId, ParaQue paraQue, CancellationToken ct = default)
    {
        Preguntas.Add((contactoId, paraQue));
        return Task.FromResult(Niega is null ? Resultado.Ok() : Resultado.Fallo(Niega));
    }
}

public sealed class DatosDePrueba : IConsultaDatosDelEnvio
{
    public DatosDelEnvio? Datos { get; set; } =
        new("Manolo", "Bar Casa Manolo", "Marta Ruiz", "Instalaciones Ribera", "manolo@casamanolo.es");

    public Task<DatosDelEnvio?> DeAsync(Guid contactoId, Guid usuarioId, CancellationToken ct = default) =>
        Task.FromResult(Datos);
}

public sealed class EmisorDePrueba : IEnviaCorreo
{
    public List<(Dominio.Correo Correo, string? UrlPixel, string? UrlBaja)> Intentos { get; } = [];

    public Func<Dominio.Correo, ResultadoEnvioCorreo> Contesta { get; set; } =
        _ => new ResultadoEnvioCorreo(true, null, false);

    public Task<ResultadoEnvioCorreo> EnviarAsync(
        Dominio.Correo correo, string? urlPixel, string? urlBaja, CancellationToken ct = default)
    {
        Intentos.Add((correo, urlPixel, urlBaja));
        return Task.FromResult(Contesta(correo));
    }
}

/// <summary>El enlace de baja, de mentira pero reconocible.</summary>
public sealed class EnlacesDePrueba : IEnlaceDeBaja
{
    public string? Devuelve { get; set; } = "https://pruebas.matchketing.es/b/firma-de-prueba";

    public string? De(Guid contactoId) => Devuelve;
}

public sealed class CronologiaDePrueba : IApuntaEnCronologia
{
    public List<string> Correos { get; } = [];

    public List<string> Aperturas { get; } = [];

    public Task ApuntarCorreoAsync(Guid contactoId, string texto, CancellationToken ct = default)
    {
        Correos.Add(texto);
        return Task.CompletedTask;
    }

    public Task ApuntarAperturaAsync(Guid contactoId, string texto, CancellationToken ct = default)
    {
        Aperturas.Add(texto);
        return Task.CompletedTask;
    }
}
