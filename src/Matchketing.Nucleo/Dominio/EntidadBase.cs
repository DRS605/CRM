namespace Matchketing.Nucleo.Dominio;

/// <summary>Entidad con identidad propia.</summary>
public abstract class EntidadBase<TId>
    where TId : notnull
{
    protected EntidadBase(TId id) => Id = id;

    public TId Id { get; protected set; }
}

/// <summary>Raíz de agregado: la única puerta de entrada a su grupo de entidades.</summary>
public abstract class RaizAgregado<TId> : EntidadBase<TId>
    where TId : notnull
{
    private readonly List<IEventoDominio> eventos = [];

    protected RaizAgregado(TId id)
        : base(id)
    {
    }

    public IReadOnlyCollection<IEventoDominio> Eventos => eventos;

    public void LimpiarEventos() => eventos.Clear();

    protected void RegistrarEvento(IEventoDominio evento) => eventos.Add(evento);
}

/// <summary>Raíz de agregado que pertenece siempre a una empresa (multiempresa obligatorio).</summary>
public abstract class RaizAgregadoEmpresa<TId> : RaizAgregado<TId>
    where TId : notnull
{
    protected RaizAgregadoEmpresa(TId id, Guid empresaId)
        : base(id) => EmpresaId = empresaId;

    public Guid EmpresaId { get; protected set; }
}
