using Matchketing.Webhooks.Dominio;

namespace Matchketing.Webhooks.Aplicacion;

/*
 * ---------- Qué viaja en un webhook ----------
 *
 * La regla, y es corta: **el webhook dice qué ha pasado y a quién apunta. Ni teléfonos, ni correos,
 * ni texto libre escrito por personas.**
 *
 * El motivo es que la URL la elige el cliente, y muchas veces no es un servidor suyo sino una
 * plataforma de automatización que guarda cada carga útil que recibe, para siempre y sin que nadie
 * vuelva a mirarla. Un teléfono que se escapa por ahí se ha escapado por nuestra culpa, no por la de
 * quien montó el flujo. Con el identificador y la API se puede pedir el resto cuando de verdad haga
 * falta; lo que se manda sin pensar no se puede recuperar.
 *
 * Nombre y origen sí van: sin ellos el evento no sirve para nada y obligaríamos a una llamada de
 * vuelta para cualquier cosa, que es la forma segura de que nadie use esto.
 *
 * Y una excepción, razonada: `contacto.baja` **lleva el correo**. El propósito exacto de ese evento es
 * que otro sistema deje de escribir a esa dirección, y exigir una llamada a la API para cumplir una
 * obligación legal es peor que mandar el dato que la cumple.
 */

/// <summary>Ha entrado un lead.</summary>
public sealed record DatosLead(Guid ContactoId, string Nombre, string? Origen);

/// <summary>
/// Algo le ha pasado a una oportunidad. Sirve para los tres eventos del embudo; los campos que no
/// aplican van nulos y no se serializan.
/// </summary>
public sealed record DatosOportunidad(
    Guid OportunidadId,
    string Titulo,
    decimal Importe,
    Guid? ContactoId,
    string? Cuenta,
    string? Etapa,
    string? EtapaAnterior,
    string? MotivoPerdida);

/// <summary>Alguien se ha dado de baja. Ver la excepción del correo, arriba.</summary>
public sealed record DatosBaja(Guid ContactoId, string Email);

/// <summary>Lo que se le pide al servicio: un tipo y sus datos. El sobre lo pone él.</summary>
public sealed record Evento(TipoEvento Tipo, object Datos);

/// <summary>Cómo fue el POST. Lo devuelve la infraestructura.</summary>
public sealed record ResultadoEntrega(bool Salio, int? Codigo, string? Fallo);

/// <summary>Una suscripción para la pantalla. **Sin el secreto**: ver <see cref="SuscripcionWebhook.Secreto"/>.</summary>
public sealed record FichaSuscripcion(
    Guid Id,
    string Url,
    string Descripcion,
    IReadOnlyList<string> Eventos,
    bool Activa,
    string? MotivoApagado,
    DateTimeOffset CreadaEn,
    DateTimeOffset? UltimaEntregaEn,
    int PendientesAhora);

/// <summary>Una entrega para la pantalla: el registro que se mira cuando algo no llega.</summary>
public sealed record FichaEntrega(
    Guid Id,
    string Evento,
    string Estado,
    int Intentos,
    DateTimeOffset CreadaEn,
    DateTimeOffset? ProximoIntentoEn,
    DateTimeOffset? EntregadaEn,
    int? UltimoCodigo,
    string? UltimoFallo);

/// <summary>Lo que devuelve una pasada del trabajo de entrega.</summary>
public sealed record ResumenEntregas(int Entregadas, int Reintentar, int Agotadas, int Apagadas);
