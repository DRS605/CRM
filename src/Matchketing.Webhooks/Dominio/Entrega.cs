using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Webhooks.Dominio;

public enum EstadoEntrega
{
    Pendiente = 1,
    Entregada = 2,

    /// <summary>Se agotaron los intentos. No se vuelve a tocar.</summary>
    Agotada = 3,
}

/// <summary>
/// Una entrega concreta a una suscripción concreta: el buzón de salida.
///
/// Existe como **fila** y no como una llamada HTTP en el momento del cambio, y esa es la decisión
/// importante de todo el módulo. Si al ganar una oportunidad se hiciera el POST allí mismo:
///
/// · una URL lenta dejaría al comercial mirando una rueda por algo que no le importa;
/// · un fallo de red perdería el evento para siempre, sin rastro;
/// · y si la transacción se deshiciera después, ya habríamos avisado de una venta que no existe.
///
/// La fila se escribe **en la misma transacción** que el cambio de negocio. Así, si el cambio se
/// deshace, el evento se deshace con él, y si el cambio se guarda, el evento está garantizado aunque
/// el proceso se caiga un milisegundo después. Es el patrón del buzón de salida, y es la única forma
/// conocida de que «pasó» y «se avisó» no puedan separarse.
///
/// La consecuencia hay que decirla en voz alta: la entrega es **al menos una vez**, nunca exactamente
/// una vez. Un reintento después de un tiempo de espera agotado puede llegar dos veces. Por eso cada
/// entrega lleva su identificador estable en la cabecera y en el cuerpo: quien recibe puede
/// descartar repetidos, y el reintento **conserva el mismo identificador** —si cambiara, la
/// deduplicación del otro lado no serviría de nada—.
/// </summary>
public sealed class Entrega : RaizAgregadoEmpresa<Guid>
{
    /// <summary>
    /// Los esperas entre intentos. Seis intentos que se estiran hasta pasado mañana: un despliegue
    /// del otro lado, una noche de mantenimiento o un fin de semana caben dentro sin perder el evento.
    /// Reintentar diez veces en un minuto y rendirse es lo mismo que no reintentar.
    /// </summary>
    private static readonly TimeSpan[] Esperas =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(25),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(10),
        TimeSpan.FromHours(24),
    ];

    public static int IntentosMaximos => Esperas.Length + 1;

    private Entrega(Guid id)
        : base(id, Guid.Empty)
    {
        Cuerpo = null!;
    }

    private Entrega(Guid id, Guid empresaId, Guid suscripcionId, TipoEvento tipo, string cuerpo, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        SuscripcionId = suscripcionId;
        Tipo = tipo;
        Cuerpo = cuerpo;
        CreadaEn = ahora;
        ProximoIntentoEn = ahora;
        Estado = EstadoEntrega.Pendiente;
    }

    public Guid SuscripcionId { get; private set; }

    public TipoEvento Tipo { get; private set; }

    /// <summary>El JSON tal cual se va a mandar. Se congela al crearla: es lo que se firma.</summary>
    public string Cuerpo { get; private set; }

    public EstadoEntrega Estado { get; private set; }

    public int Intentos { get; private set; }

    public DateTimeOffset CreadaEn { get; private set; }

    /// <summary>Cuándo toca el siguiente intento. Nulo cuando ya no hay más.</summary>
    public DateTimeOffset? ProximoIntentoEn { get; private set; }

    public DateTimeOffset? EntregadaEn { get; private set; }

    /// <summary>El código HTTP del último intento, si hubo respuesta. Nulo si no se llegó a hablar.</summary>
    public int? UltimoCodigo { get; private set; }

    /// <summary>Qué pasó la última vez, para poder enseñarlo. Nunca lleva el cuerpo de la respuesta.</summary>
    public string? UltimoFallo { get; private set; }

    /// <summary>
    /// El identificador viene de fuera porque **va dentro del cuerpo**: quien recibe deduplica por él,
    /// así que hay que conocerlo antes de serializar el JSON que luego se firma. Generarlo aquí
    /// obligaría a escribir el cuerpo después, y entonces lo firmado y lo enviado podrían separarse.
    /// </summary>
    public static Entrega Crear(Guid id, Guid empresaId, Guid suscripcionId, TipoEvento tipo, string cuerpo, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentException.ThrowIfNullOrWhiteSpace(cuerpo);

        return new Entrega(id, empresaId, suscripcionId, tipo, cuerpo, reloj.AhoraUtc);
    }

    public bool LeToca(DateTimeOffset ahora) =>
        Estado == EstadoEntrega.Pendiente && ProximoIntentoEn is { } cuando && cuando <= ahora;

    public void Salio(int codigo, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        Intentos++;
        Estado = EstadoEntrega.Entregada;
        EntregadaEn = reloj.AhoraUtc;
        ProximoIntentoEn = null;
        UltimoCodigo = codigo;
        UltimoFallo = null;
    }

    /// <summary>
    /// Se deja de intentar sin gastar reintentos: la suscripción se borró o se apagó mientras esta
    /// entrega esperaba su turno. No es un fallo del otro lado, así que no cuenta como tal.
    /// </summary>
    public void Abandonar(string porque, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        Estado = EstadoEntrega.Agotada;
        ProximoIntentoEn = null;
        UltimoFallo = porque;
    }

    /// <summary>
    /// No salió. Devuelve cierto si ya no se va a volver a intentar, que es cuando la suscripción se
    /// apunta un fallo.
    /// </summary>
    public bool NoSalio(int? codigo, string porque, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        Intentos++;
        UltimoCodigo = codigo;
        UltimoFallo = porque;

        if (Intentos >= IntentosMaximos)
        {
            Estado = EstadoEntrega.Agotada;
            ProximoIntentoEn = null;
            return true;
        }

        ProximoIntentoEn = reloj.AhoraUtc.Add(Esperas[Intentos - 1]);
        return false;
    }
}
