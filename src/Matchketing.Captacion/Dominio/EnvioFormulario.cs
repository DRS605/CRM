using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Captacion.Dominio;

/// <summary>
/// Lo que llegó por el formulario, tal cual. Se guarda aunque el contacto se cree o se fusione:
/// es la prueba de qué escribió esa persona y desde dónde.
/// </summary>
public sealed class EnvioFormulario : RaizAgregadoEmpresa<Guid>
{
    private EnvioFormulario(Guid id)
        : base(id, Guid.Empty) => Datos = null!;

    private EnvioFormulario(Guid id, Guid empresaId, Guid formularioId, string datos, string? ip, string? agente, Guid? contactoId, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        FormularioId = formularioId;
        Datos = datos;
        Ip = ip;
        Agente = agente;
        ContactoId = contactoId;
        RecibidoEn = ahora;
    }

    public Guid FormularioId { get; private set; }

    /// <summary>Los campos enviados, en JSON.</summary>
    public string Datos { get; private set; }

    public string? Ip { get; private set; }

    public string? Agente { get; private set; }

    public Guid? ContactoId { get; private set; }

    public DateTimeOffset RecibidoEn { get; private set; }

    public static EnvioFormulario Crear(Guid empresaId, Guid formularioId, string datos, string? ip, string? agente, Guid? contactoId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        return new EnvioFormulario(Guid.NewGuid(), empresaId, formularioId, datos, ip, agente, contactoId, reloj.AhoraUtc);
    }
}
