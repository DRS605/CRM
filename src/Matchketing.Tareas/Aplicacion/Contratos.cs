using Matchketing.Tareas.Dominio;

namespace Matchketing.Tareas.Aplicacion;

/// <summary>Por qué esta tarjeta está hoy en la pila.</summary>
public enum TipoTarjeta
{
    /// <summary>Una tarea que vence hoy o que ya venció.</summary>
    Tarea = 1,

    /// <summary>Un contacto activo sin próxima acción. La promesa del producto (invariante H1).</summary>
    SinProximaAccion = 2,

    /// <summary>Una oportunidad parada más días de los que su etapa tolera.</summary>
    Estancada = 3,
}

/// <summary>
/// Una tarjeta de la pila de Hoy: quién, por qué ahora, y qué se puede hacer. El motivo es
/// obligatorio por diseño — una tarjeta sin motivo no se enseña.
/// </summary>
public sealed record TarjetaHoy(
    TipoTarjeta Tipo,
    Guid? TareaId,
    Guid? ContactoId,
    Guid? OportunidadId,
    string Titulo,
    string NombreContacto,
    string? NombreCuenta,
    string? Telefono,
    string Motivo,
    DateOnly? VenceEl,
    int DiasVencida,
    decimal? Importe,
    int Urgencia);

public sealed record PilaHoy(
    IReadOnlyList<TarjetaHoy> Tarjetas,
    int Pendientes,
    int HechasHoy,
    int SinProximaAccion,
    int Estancadas);

public sealed record TareaVista(
    Guid Id, string Titulo, Guid? ContactoId, string? NombreContacto,
    DateOnly VenceEl, EstadoTarea Estado, OrigenTarea Origen, int VecesAplazada);
