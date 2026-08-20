using Matchketing.Correo.Dominio;

namespace Matchketing.Correo.Aplicacion;

public sealed record FichaPlantilla(
    Guid Id, string Nombre, string Asunto, string Cuerpo, string ParaQue, int Usos, DateTimeOffset CreadaEn);

/// <summary>
/// Un correo para la cronología o la pantalla. Lleva el texto: la mitad del valor de guardar esto es
/// poder ver el mensaje exacto que se le mandó, sin ir a buscarlo al buzón de nadie.
/// </summary>
public sealed record FichaCorreo(
    Guid Id,
    string Para,
    string Asunto,
    string Cuerpo,
    string Estado,
    DateTimeOffset CreadoEn,
    DateTimeOffset? EnviadoEn,
    string? UltimoFallo,
    int Aperturas,
    DateTimeOffset? PrimeraAperturaEn);

/// <summary>La vista previa antes de enviar: lo que se va a mandar, y si se puede mandar.</summary>
public sealed record Borrador(string Asunto, string Cuerpo, string Para, bool SePuede, string? PorQueNo);

public sealed record ResumenEnvios(int Enviados, int Reintentar, int Fallidos, int Cancelados);
