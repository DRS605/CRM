using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Match.Dominio;

/// <summary>
/// La puntuación guardada de un contacto, con sus motivos ya redactados. Se guarda calculada para
/// que listar cien contactos no signifique cien cálculos, y se recalcula cuando llega una señal o
/// en el barrido nocturno.
/// </summary>
public sealed class PuntuacionMatch : RaizAgregadoEmpresa<Guid>
{
    private PuntuacionMatch(Guid id)
        : base(id, Guid.Empty) => Motivos = string.Empty;

    private PuntuacionMatch(Guid id, Guid empresaId, Guid contactoId)
        : base(id, empresaId)
    {
        ContactoId = contactoId;
        Motivos = string.Empty;
    }

    public Guid ContactoId { get; private set; }

    /// <summary>Nulo cuando no hay ningún motivo que contar (invariante M1).</summary>
    public int? Match { get; private set; }

    public int Encaje { get; private set; }

    public int Momento { get; private set; }

    /// <summary>Los motivos, separados por saltos de línea. Se guardan redactados, no en crudo.</summary>
    public string Motivos { get; private set; }

    public bool SinHistorico { get; private set; }

    public DateTimeOffset CalculadaEn { get; private set; }

    public IReadOnlyList<string> ListaMotivos =>
        string.IsNullOrEmpty(Motivos) ? [] : Motivos.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public static PuntuacionMatch Crear(Guid empresaId, Guid contactoId, ResultadoMatch resultado, IReloj reloj)
    {
        var p = new PuntuacionMatch(Guid.NewGuid(), empresaId, contactoId);
        p.Actualizar(resultado, reloj);
        return p;
    }

    public void Actualizar(ResultadoMatch resultado, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(reloj);

        Match = resultado.Match;
        Encaje = resultado.Encaje;
        Momento = resultado.Momento;
        Motivos = string.Join('\n', resultado.Motivos);
        SinHistorico = resultado.SinHistorico;
        CalculadaEn = reloj.AhoraUtc;
    }
}
