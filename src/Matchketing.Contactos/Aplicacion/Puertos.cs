using Matchketing.Contactos.Dominio;

namespace Matchketing.Contactos.Aplicacion;

public interface IRepositorioContactos
{
    Task<Contacto?> BuscarPorIdAsync(Guid id, CancellationToken ct = default);

    Task<Contacto?> BuscarPorEmailAsync(string email, CancellationToken ct = default);

    Task<Contacto?> BuscarPorTelefonoAsync(string telefono, CancellationToken ct = default);

    Task<IReadOnlyList<Contacto>> ActivosAsync(CancellationToken ct = default);

    void Anadir(Contacto contacto);
}

public interface IRepositorioCuentas
{
    Task<Cuenta?> BuscarPorIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Cuenta>> ActivasAsync(CancellationToken ct = default);

    void Anadir(Cuenta cuenta);
}

public interface IRepositorioActividades
{
    Task<IReadOnlyList<Actividad>> DeContactoAsync(Guid contactoId, CancellationToken ct = default);

    Task<int> ReasignarAsync(Guid deContactoId, Guid aContactoId, CancellationToken ct = default);

    void Anadir(Actividad actividad);
}

/// <summary>Consultas de lectura. Van aparte de la escritura porque devuelven vistas, no agregados.</summary>
public interface IConsultaContactos
{
    Task<IReadOnlyList<ContactoResumen>> ListarAsync(string? busqueda, EstadoContacto? estado, CancellationToken ct = default);

    Task<FichaContacto?> FichaAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<PropuestaDuplicado>> DuplicadosAsync(CancellationToken ct = default);
}
