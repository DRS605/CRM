using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Campanias.Dominio;

public enum EstadoEnvio
{
    /// <summary>Está en la audiencia y todavía no le ha llegado el turno.</summary>
    Pendiente = 1,

    /// <summary>Se le ha encolado el correo. Que salga o no ya lo cuenta el buzón de salida.</summary>
    Encolado = 2,

    /// <summary>No se le manda, y en <see cref="EnvioCampania.Motivo"/> está por qué.</summary>
    Excluido = 3,
}

/// <summary>
/// Una fila por persona y por campaña: la audiencia congelada, y luego qué pasó con cada uno.
///
/// Es la tabla que hace posible la única pregunta que en una plataforma de mailing no se puede
/// contestar: **¿por qué a esta persona no le llegó?** Las herramientas de envío masivo cuentan
/// entregas y aperturas; lo que se queda fuera desaparece en un número agregado de «no válidos». Aquí
/// cada exclusión guarda su motivo en una frase, y esa frase es la respuesta que hay que dar cuando el
/// cliente pregunta —o cuando lo pregunta la Agencia—.
///
/// No guarda el correo ni el nombre de nadie: guarda el identificador del contacto. Así un borrado del
/// artículo 17 se lleva por delante estas filas con el contacto, sin dejar copias del dato personal
/// esparcidas por el historial de campañas.
/// </summary>
public sealed class EnvioCampania : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaMotivo = 200;

    private EnvioCampania(Guid id)
        : base(id, Guid.Empty)
    {
    }

    private EnvioCampania(Guid id, Guid empresaId, Guid campaniaId, Guid contactoId)
        : base(id, empresaId)
    {
        CampaniaId = campaniaId;
        ContactoId = contactoId;
        Estado = EstadoEnvio.Pendiente;
    }

    public Guid CampaniaId { get; private set; }

    public Guid ContactoId { get; private set; }

    public EstadoEnvio Estado { get; private set; }

    /// <summary>Por qué se queda fuera. Nulo mientras no lo esté.</summary>
    public string? Motivo { get; private set; }

    /// <summary>
    /// El correo que se le encoló, para poder mirar después si salió y si lo abrió. Es una referencia a
    /// otro módulo por identificador y sin clave ajena, igual que <c>contacto_id</c>: quien quiera saber
    /// qué fue de ese correo pregunta al buzón de salida.
    /// </summary>
    public Guid? CorreoId { get; private set; }

    public DateTimeOffset? ResueltoEn { get; private set; }

    public static EnvioCampania Crear(Guid empresaId, Guid campaniaId, Guid contactoId) =>
        new(Guid.NewGuid(), empresaId, campaniaId, contactoId);

    /// <summary>
    /// Le hemos encolado el correo. Devuelve falso si ya estaba resuelto, y eso importa: el trabajo que
    /// recorre los lotes se puede solapar consigo mismo si una pasada tarda más de lo que dura el
    /// intervalo, y sin esta guarda la misma persona recibiría el correo dos veces.
    /// </summary>
    public bool Encolar(Guid correoId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (Estado != EstadoEnvio.Pendiente)
        {
            return false;
        }

        Estado = EstadoEnvio.Encolado;
        CorreoId = correoId;
        ResueltoEn = reloj.AhoraUtc;
        return true;
    }

    /// <summary>Se queda fuera. Mismo cuidado que <see cref="Encolar"/> con el doble paso.</summary>
    public bool Excluir(string porque, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (Estado != EstadoEnvio.Pendiente)
        {
            return false;
        }

        Estado = EstadoEnvio.Excluido;
        Motivo = Recortar(porque);
        ResueltoEn = reloj.AhoraUtc;
        return true;
    }

    /// <summary>
    /// El motivo se recorta en vez de rechazarse. Es la única cadena de este módulo que viene de otro
    /// —el mensaje de error de la comprobación de permiso— y perder una campaña entera porque un módulo
    /// vecino devolvió un mensaje largo sería absurdo.
    /// </summary>
    private static string Recortar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return "Sin motivo apuntado.";
        }

        var limpio = texto.Trim();
        return limpio.Length <= LongitudMaximaMotivo ? limpio : limpio[..LongitudMaximaMotivo];
    }
}
