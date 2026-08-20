using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Webhooks.Dominio;

/// <summary>
/// Una URL de la empresa que quiere enterarse de ciertas cosas.
///
/// Se desactiva sola. Un endpoint que lleva dos días devolviendo 500 no va a arreglarse porque
/// insistamos, y seguir insistiendo tiene dos costes que no son teóricos: la tabla de entregas crece
/// sin parar, y desde el otro lado nuestro reintento cada minuto contra una URL muerta se parece
/// bastante a un ataque. Así que a los <see cref="FallosParaDesactivar"/> fallos definitivos
/// seguidos se apaga y se guarda el motivo, para que se pueda leer en la pantalla en vez de
/// adivinarlo.
/// </summary>
public sealed class SuscripcionWebhook : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaUrl = 500;

    /// <summary>Entregas agotadas seguidas antes de apagar la suscripción.</summary>
    public const int FallosParaDesactivar = 5;

    private readonly List<TipoEvento> tipos = [];

    private SuscripcionWebhook(Guid id)
        : base(id, Guid.Empty)
    {
        Url = null!;
        Secreto = null!;
        Descripcion = null!;
    }

    private SuscripcionWebhook(
        Guid id, Guid empresaId, string url, string secreto, string descripcion,
        IEnumerable<TipoEvento> escucha, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Url = url;
        Secreto = secreto;
        Descripcion = descripcion;
        tipos.AddRange(escucha);
        CreadaEn = ahora;
        Activa = true;
    }

    /// <summary>Adónde se manda el POST. Solo https.</summary>
    public string Url { get; private set; }

    /// <summary>
    /// Con qué se firma. Se enseña **una sola vez**, al crearla: después ya no se puede leer, solo
    /// rotar. Guardarlo en claro es inevitable —hay que firmar con él— pero devolverlo en cada
    /// listado sería regalarlo a cualquier sesión abierta en un portátil sin bloquear.
    /// </summary>
    public string Secreto { get; private set; }

    /// <summary>Para qué es, en una línea. Con tres suscripciones ya nadie recuerda cuál es cuál.</summary>
    public string Descripcion { get; private set; }

    public IReadOnlyList<TipoEvento> Tipos => tipos;

    public bool Activa { get; private set; }

    /// <summary>Por qué se apagó, si se apagó. Nulo mientras esté viva.</summary>
    public string? MotivoApagado { get; private set; }

    public int FallosSeguidos { get; private set; }

    public DateTimeOffset CreadaEn { get; private set; }

    public DateTimeOffset? UltimaEntregaEn { get; private set; }

    public static Resultado<SuscripcionWebhook> Crear(
        Guid empresaId, string? url, string? descripcion, IReadOnlyCollection<TipoEvento>? escucha, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        // Solo https, y sin excepción para localhost. La tentación es dejar http «para probar», y esa
        // excepción acaba en producción con un webhook de ventas viajando en claro. Para probar en
        // local hay túneles de sobra.
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return Resultado.Fallo<SuscripcionWebhook>(Error.Validacion(
                "webhook.url_invalida", "La dirección tiene que ser una URL https."));
        }

        if (url.Length > LongitudMaximaUrl)
        {
            return Resultado.Fallo<SuscripcionWebhook>(Error.Validacion(
                "webhook.url_larga", "La dirección es demasiado larga."));
        }

        if (string.IsNullOrWhiteSpace(descripcion))
        {
            return Resultado.Fallo<SuscripcionWebhook>(Error.Validacion(
                "webhook.sin_descripcion", "Dile para qué es, aunque sean tres palabras."));
        }

        // Sin eventos no hay suscripción. Crearla vacía «para elegir luego» deja una fila que no hace
        // nada y que parece que sí, que es la peor clase de configuración.
        if (escucha is null || escucha.Count == 0)
        {
            return Resultado.Fallo<SuscripcionWebhook>(Error.Validacion(
                "webhook.sin_eventos", "Elige al menos un evento que escuchar."));
        }

        var limpios = escucha.Distinct().ToArray();

        return Resultado.Ok(new SuscripcionWebhook(
            Guid.NewGuid(), empresaId, url, FirmaWebhook.SecretoNuevo(), descripcion.Trim(), limpios, reloj.AhoraUtc));
    }

    public bool Escucha(TipoEvento tipo) => Activa && tipos.Contains(tipo);

    /// <summary>Una entrega salió bien: se borra el historial de fallos.</summary>
    public void Entregada(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        FallosSeguidos = 0;
        UltimaEntregaEn = reloj.AhoraUtc;
    }

    /// <summary>
    /// Una entrega se ha dado por perdida después de agotar sus intentos. Devuelve cierto si esto ha
    /// apagado la suscripción.
    /// </summary>
    public bool Fallada(string porque)
    {
        FallosSeguidos++;
        if (FallosSeguidos < FallosParaDesactivar || !Activa)
        {
            return false;
        }

        Activa = false;
        MotivoApagado = porque;
        return true;
    }

    /// <summary>
    /// La vuelve a encender, a mano. No se reactiva sola: si se apagó porque la URL estaba mal, hay
    /// que arreglar la URL, y volver a intentarlo por nuestra cuenta solo repetiría el fallo.
    /// </summary>
    public void Reactivar()
    {
        Activa = true;
        MotivoApagado = null;
        FallosSeguidos = 0;
    }

    /// <summary>Secreto nuevo. Devuelve el nuevo, que es la única vez que se puede leer.</summary>
    public string RotarSecreto()
    {
        Secreto = FirmaWebhook.SecretoNuevo();
        return Secreto;
    }

    public Resultado Cambiar(string? descripcion, IReadOnlyCollection<TipoEvento>? escucha)
    {
        if (string.IsNullOrWhiteSpace(descripcion))
        {
            return Resultado.Fallo(Error.Validacion(
                "webhook.sin_descripcion", "Dile para qué es, aunque sean tres palabras."));
        }

        if (escucha is null || escucha.Count == 0)
        {
            return Resultado.Fallo(Error.Validacion(
                "webhook.sin_eventos", "Elige al menos un evento que escuchar."));
        }

        Descripcion = descripcion.Trim();
        tipos.Clear();
        tipos.AddRange(escucha.Distinct());
        return Resultado.Ok();
    }
}
