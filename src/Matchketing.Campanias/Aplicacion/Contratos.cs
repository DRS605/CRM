using Matchketing.Campanias.Dominio;

namespace Matchketing.Campanias.Aplicacion;

/// <summary>
/// Un segmento para la lista. <paramref name="Cuantos"/> se cuenta al pedir la lista, no se guarda: un
/// número guardado sería una foto de cuando se guardó, y la mitad del valor de un segmento es que el
/// número de hoy sea el de hoy.
/// </summary>
public sealed record FichaSegmento(
    Guid Id,
    string Nombre,
    string Frase,
    int Cuantos,
    EstadoBuscado? Estado,
    string? Provincia,
    string? Origen,
    int? MatchMinimo,
    int? SinActividadDias,
    Guid? EtapaId,
    bool EnUso,
    DateTimeOffset CreadoEn);

/// <summary>
/// Lo que se ve antes de lanzar. <paramref name="Cuantos"/> es la audiencia entera;
/// <paramref name="Muestra"/> son los primeros, para que se pueda reconocer a alguien y confirmar que
/// el filtro dice lo que se creía.
/// </summary>
public sealed record VistaPreviaSegmento(Guid SegmentoId, string Nombre, string Frase, int Cuantos, IReadOnlyList<QuienRecibe> Muestra);

/// <summary>Una campaña en la lista.</summary>
public sealed record FichaCampania(
    Guid Id,
    string Nombre,
    string Estado,
    Guid SegmentoId,
    string? SegmentoNombre,
    Guid PlantillaId,
    string? PlantillaNombre,
    int Destinatarios,
    int Encolados,
    int Excluidos,
    int Pendientes,
    DateTimeOffset CreadaEn,
    DateTimeOffset? LanzadaEn);

/// <summary>
/// La ficha de una campaña ya lanzada: lo que se prometió y lo que pasó.
///
/// Lleva los dos números que ninguna plataforma de envío pone juntos en la misma pantalla: a cuántos se
/// llegó y a cuántos **no**, con el detalle de por qué. Ahí está la diferencia entre medir una campaña y
/// entender una campaña.
/// </summary>
public sealed record DetalleCampania(
    FichaCampania Campania,
    string? SegmentoAlLanzar,
    ContadoresCorreo Correos,
    IReadOnlyList<MotivoExclusion> PorQueNoLlego);

/// <summary>Un motivo de exclusión y a cuántos les pasó. Ordenado de más a menos.</summary>
public sealed record MotivoExclusion(string Motivo, int Cuantos);

/// <summary>Lo que hizo una pasada del trabajo que va encolando. Va al registro.</summary>
public sealed record PasadaCampanias(int Campanias, int Encolados, int Excluidos, int Cerradas);
