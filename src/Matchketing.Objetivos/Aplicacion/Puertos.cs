using Matchketing.Objetivos.Dominio;

namespace Matchketing.Objetivos.Aplicacion;

public interface IRepositorioObjetivos
{
    Task<Objetivo?> DeAsync(Guid usuarioId, DateOnly mes, CancellationToken ct = default);

    Task<IReadOnlyList<Objetivo>> DelMesAsync(DateOnly mes, CancellationToken ct = default);

    /// <summary>Los de una persona, del mes más reciente hacia atrás. Para su histórico.</summary>
    Task<IReadOnlyList<Objetivo>> DePersonaAsync(Guid usuarioId, int cuantos, CancellationToken ct = default);

    void Anadir(Objetivo objetivo);

    void Quitar(Objetivo objetivo);
}

/// <summary>
/// Cuánto ha cerrado cada persona en un mes. Lo implementa la persistencia porque cruza el embudo con
/// la identidad, y este módulo no conoce ninguno de los dos.
///
/// Cuenta por **fecha de cierre** y no de creación, y solo las **ganadas**: un objetivo de venta se
/// cumple cuando se firma. Y se atribuye al **propietario de la oportunidad**, que es quien la cerró,
/// no a quien creó el contacto hace ocho meses.
/// </summary>
public interface IConsultaLogrado
{
    Task<IReadOnlyDictionary<Guid, decimal>> GanadoPorPersonaAsync(DateOnly mes, CancellationToken ct = default);

    Task<decimal> GanadoDeAsync(Guid usuarioId, DateOnly mes, CancellationToken ct = default);
}

/// <summary>Quién hay en el equipo. Hace falta para poder poner objetivos y para listarlos con nombre.</summary>
public interface IConsultaEquipoObjetivos
{
    Task<IReadOnlyList<QuienVende>> ActivosAsync(CancellationToken ct = default);
}

/// <summary>Una persona del equipo. <paramref name="Vende"/> distingue quién puede tener objetivo.</summary>
public sealed record QuienVende(Guid UsuarioId, string Nombre, bool Vende);
