using Matchketing.Automatizacion.Dominio;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Automatizacion.Aplicacion;

public interface IRepositorioReglas
{
    Task<Regla?> PorIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Regla>> DeLaEmpresaAsync(CancellationToken ct = default);

    /// <summary>
    /// Las activas de este disparador. Es la consulta del camino caliente: se hace en cada cambio de
    /// negocio que puede disparar algo, así que tiene que devolver poco y por índice.
    /// </summary>
    Task<IReadOnlyList<Regla>> ActivasParaAsync(Disparador disparador, CancellationToken ct = default);

    void Anadir(Regla regla);

    void Quitar(Regla regla);

    /// <summary>¿Ya actuó esta regla sobre este sujeto? La garantía de «una sola vez».</summary>
    Task<bool> YaActuoAsync(Guid reglaId, Guid sujetoId, CancellationToken ct = default);

    Task<IReadOnlyList<Ejecucion>> UltimasDeAsync(Guid reglaId, int cuantas, CancellationToken ct = default);

    void AnadirEjecucion(Ejecucion ejecucion);
}

/// <summary>
/// Lo que se sabe del sujeto para poder evaluar las condiciones. Una consulta por disparo, no una por
/// regla: con diez reglas activas, una consulta por regla convertiría guardar un contacto en diez
/// viajes a la base.
/// </summary>
public interface IConsultaHechos
{
    Task<Hechos?> DeContactoAsync(Guid contactoId, CancellationToken ct = default);

    Task<Hechos?> DeOportunidadAsync(Guid oportunidadId, CancellationToken ct = default);
}

/// <summary>
/// Lo que una acción provoca en los otros módulos. Igual que <c>IAccionesRepaso</c>: un puerto para que
/// este módulo no referencie a tareas, contactos ni correo, y el adaptador en la API, que ya los conoce.
///
/// Cada método devuelve **lo que hizo, ya escrito en castellano**, o nulo si no pudo. Ese texto es el
/// que se guarda en el registro de ejecuciones y el que se lee en la pantalla: si el módulo devolviera
/// un booleano, habría que redactarlo aquí sin saber lo que pasó de verdad al otro lado.
/// </summary>
public interface IAccionesAutomatizacion
{
    Task<string?> CrearTareaAsync(Guid contactoId, string titulo, int dias, CancellationToken ct = default);

    Task<string?> AsignarAsync(Guid contactoId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Encola un correo con esa plantilla. **Pasa por la misma comprobación de permiso que un correo a
    /// mano**, así que puede devolver nulo perfectamente: es lo que tiene que pasar si esa persona no ha
    /// dado su consentimiento. Una automatización no es una excusa para saltarse el RGPD.
    /// </summary>
    Task<string?> MandarCorreoAsync(Guid contactoId, Guid plantillaId, CancellationToken ct = default);

    Task<string?> ApuntarNotaAsync(Guid contactoId, string texto, CancellationToken ct = default);
}

/// <summary>Lo que ocurrió, tal y como llega del despachador de eventos.</summary>
public sealed record Ocurrencia(Disparador Disparador, Guid SujetoId, Guid? ContactoId);

public sealed record FichaRegla(
    Guid Id,
    string Nombre,
    string Disparador,
    string Leida,
    bool Activa,
    int Veces,
    DateTimeOffset CreadaEn,
    DateTimeOffset? UltimaVezEn);

public sealed record FichaEjecucion(Guid Id, Guid SujetoId, Guid? ContactoId, string QueHizo, DateTimeOffset CuandoEn);

/// <summary>
/// Qué haría una regla con un sujeto concreto, sin hacerlo. Es la prueba en seco.
/// </summary>
public sealed record Ensayo(bool Aplicaria, string? PorQueNo, IReadOnlyList<string> Haria);
