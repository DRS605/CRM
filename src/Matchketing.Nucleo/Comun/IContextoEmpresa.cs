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

/// <summary>
/// Permite fijar la empresa **sin token**. Existe para un solo caso: la entrada pública de leads,
/// donde quien rellena el formulario en la web de un cliente no está autenticado y la empresa se
/// deduce de la clave del formulario.
///
/// No se usa en ningún endpoint autenticado: ahí la empresa sale del JWT y solo del JWT (T2).
/// </summary>
public interface IContextoEmpresaPublico
{
    void FijarEmpresa(Guid empresaId);
}
