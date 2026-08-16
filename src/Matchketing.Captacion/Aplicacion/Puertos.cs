using Matchketing.Captacion.Dominio;

namespace Matchketing.Captacion.Aplicacion;

public interface IRepositorioFormularios
{
    Task<Formulario?> BuscarPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Busca por la clave pública **sin filtrar por empresa**: quien rellena el formulario en una
    /// web no está autenticado y no hay empresa activa en la petición. La clave es la que
    /// determina de qué empresa es.
    /// </summary>
    Task<Formulario?> BuscarPorClaveAsync(string clave, CancellationToken ct = default);

    Task<IReadOnlyList<Formulario>> ActivosAsync(CancellationToken ct = default);

    void Anadir(Formulario formulario);
}

public interface IRepositorioEnvios
{
    void Anadir(EnvioFormulario envio);

    Task<int> ContarDeFormularioAsync(Guid formularioId, CancellationToken ct = default);
}

public sealed record ResumenFormulario(Guid Id, string Nombre, string Clave, string TextoConsentimiento, bool PideTelefono, bool PideEmpresa, bool PideMensaje, string? PaginaGracias, string Origen, int Envios);
