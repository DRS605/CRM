namespace Matchketing.Webhooks.Dominio;

/// <summary>
/// Lo que se puede escuchar. Son **cinco**, y son cinco a propósito.
///
/// Un catálogo de cuarenta eventos parece generosidad y es lo contrario: nadie sabe a cuál
/// suscribirse, la mitad no se emiten nunca, y cada uno es un sitio más donde se puede filtrar un
/// dato. La regla para que un evento entre en esta lista es que **otro sistema haga algo distinto al
/// recibirlo**. «Se ha editado un contacto» no la pasa; «se ha ganado una oportunidad» sí, porque al
/// otro lado se emite un pedido.
///
/// El texto es lo que viaja en el JSON y en la cabecera, así que es parte del contrato público: no se
/// cambia sin romperle la integración a alguien.
/// </summary>
public enum TipoEvento
{
    /// <summary>Ha entrado un lead. Lo escucha quien quiera avisar a un comercial por otro canal.</summary>
    LeadCreado = 1,

    /// <summary>Una oportunidad ha cambiado de etapa. El pulso del embudo para un cuadro de mando.</summary>
    OportunidadMovida = 2,

    /// <summary>
    /// Venta cerrada. Es **el** evento: es el que enlaza con el ERP para que se emita el pedido sin
    /// que nadie lo teclee dos veces.
    /// </summary>
    OportunidadGanada = 3,

    /// <summary>Venta perdida, con su motivo. Alimenta el análisis de por qué se pierde.</summary>
    OportunidadPerdida = 4,

    /// <summary>
    /// Alguien se ha dado de baja. El único evento **obligatorio** de propagar: una baja que no llega
    /// al sistema que manda los correos es una baja que no existe, y eso ya no es un fallo técnico.
    /// </summary>
    ContactoBaja = 5,
}

public static class TiposEvento
{
    private static readonly (TipoEvento Tipo, string Texto)[] Nombres =
    [
        (TipoEvento.LeadCreado, "lead.creado"),
        (TipoEvento.OportunidadMovida, "oportunidad.movida"),
        (TipoEvento.OportunidadGanada, "oportunidad.ganada"),
        (TipoEvento.OportunidadPerdida, "oportunidad.perdida"),
        (TipoEvento.ContactoBaja, "contacto.baja"),
    ];

    public static IReadOnlyList<TipoEvento> Todos { get; } = Nombres.Select(n => n.Tipo).ToArray();

    public static string Texto(TipoEvento tipo) =>
        Nombres.FirstOrDefault(n => n.Tipo == tipo).Texto ?? "desconocido";

    public static TipoEvento? De(string? texto)
    {
        var encontrado = Nombres.FirstOrDefault(n => n.Texto == texto);
        return encontrado.Texto is null ? null : encontrado.Tipo;
    }
}
