using Matchketing.Contactos.Dominio;

namespace Matchketing.Contactos.Aplicacion;

public sealed record ContactoResumen(
    Guid Id, string Nombre, string? Email, string? Telefono, string? Cargo,
    Guid? CuentaId, string? NombreCuenta, string Origen, EstadoContacto Estado,
    DateTimeOffset? UltimaActividadEn);

public sealed record ActividadVista(
    Guid Id, TipoActividad Tipo, SentidoActividad Sentido, string Cuerpo,
    ResultadoLlamada? Resultado, DateTimeOffset OcurridaEn);

public sealed record FichaContacto(ContactoResumen Contacto, IReadOnlyList<ActividadVista> Cronologia);

/// <summary>Dos contactos que parecen la misma persona, y por qué lo parecen.</summary>
public sealed record PropuestaDuplicado(ContactoResumen Uno, ContactoResumen Otro, string Motivo);

public sealed record FilaConError(int Linea, string Motivo);

/// <summary>
/// Resultado de una importación. En previsualización nada se guarda: solo se cuenta y se avisa.
/// </summary>
public sealed record ResultadoImportacion(
    bool Previsualizacion,
    int Validas,
    int Creados,
    int Duplicadas,
    IReadOnlyList<FilaConError> Errores,
    IReadOnlyList<string> ColumnasReconocidas);
