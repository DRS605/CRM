namespace Matchketing.Nucleo.Comun;

/// <summary>
/// Empresa activa de la petición en curso. Se resuelve del JWT y alimenta tanto el filtro global
/// de EF Core como la variable de sesión de PostgreSQL que activa la RLS.
/// </summary>
public interface IContextoEmpresa
{
    Guid? EmpresaId { get; }

    Guid? UsuarioId { get; }

    IReadOnlyCollection<string> Permisos { get; }

    bool Tiene(string permiso);
}
