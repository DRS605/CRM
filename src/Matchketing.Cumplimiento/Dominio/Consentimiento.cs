using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Cumplimiento.Dominio;

/// <summary>Para qué se guardó el permiso. Un consentimiento sirve para lo que dice y nada más.</summary>
public enum FinalidadConsentimiento
{
    /// <summary>Contestar a lo que ha pedido. No permite mandarle promociones.</summary>
    AtenderSolicitud = 1,

    /// <summary>Comunicaciones comerciales.</summary>
    Comercial = 2,
}

/// <summary>Por qué podemos tratar sus datos (RGPD art. 6).</summary>
public enum BaseLegal
{
    Consentimiento = 1,
    InteresLegitimo = 2,
    Contrato = 3,
}

/// <summary>
/// El permiso de una persona, con **prueba de cuándo, por dónde y desde dónde** se dio. No es un
/// `bool`: si algún día hay que demostrar que ese correo se podía enviar, un booleano no demuestra
/// nada.
/// </summary>
public sealed class Consentimiento : RaizAgregadoEmpresa<Guid>
{
    private Consentimiento(Guid id)
        : base(id, Guid.Empty) => Canal = null!;

    private Consentimiento(Guid id, Guid empresaId, Guid contactoId, FinalidadConsentimiento finalidad, BaseLegal baseLegal, string canal, string? textoAceptado, string? ip, string? agente, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        ContactoId = contactoId;
        Finalidad = finalidad;
        Base = baseLegal;
        Canal = canal;
        TextoAceptado = textoAceptado;
        Ip = ip;
        Agente = agente;
        OtorgadoEn = ahora;
    }

    public Guid ContactoId { get; private set; }

    public FinalidadConsentimiento Finalidad { get; private set; }

    public BaseLegal Base { get; private set; }

    /// <summary>Por dónde llegó: «formulario web», «alta manual», «importación»…</summary>
    public string Canal { get; private set; }

    /// <summary>El texto exacto que la persona aceptó. Sin esto no hay prueba de qué consintió.</summary>
    public string? TextoAceptado { get; private set; }

    public string? Ip { get; private set; }

    public string? Agente { get; private set; }

    public DateTimeOffset OtorgadoEn { get; private set; }

    public DateTimeOffset? RetiradoEn { get; private set; }

    public bool Vigente => RetiradoEn is null;

    public static Resultado<Consentimiento> Otorgar(
        Guid empresaId, Guid contactoId, FinalidadConsentimiento finalidad, BaseLegal baseLegal,
        string? canal, string? textoAceptado, string? ip, string? agente, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (string.IsNullOrWhiteSpace(canal))
        {
            return Resultado.Fallo<Consentimiento>(
                Error.Validacion("consentimiento.sin_canal", "Hay que decir por dónde se dio el consentimiento."));
        }

        return Resultado.Ok(new Consentimiento(
            Guid.NewGuid(), empresaId, contactoId, finalidad, baseLegal, canal.Trim(),
            Recortar(textoAceptado, 1000), Recortar(ip, 60), Recortar(agente, 400), reloj.AhoraUtc));
    }

    /// <summary>Retirarlo es inmediato e irreversible desde nuestro lado (invariante G2).</summary>
    public Resultado Retirar(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (RetiradoEn is not null)
        {
            return Resultado.Fallo(Error.Conflicto("consentimiento.ya_retirado", "Ese consentimiento ya estaba retirado."));
        }

        RetiradoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    private static string? Recortar(string? valor, int maximo) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim()[..Math.Min(valor.Trim().Length, maximo)];
}
