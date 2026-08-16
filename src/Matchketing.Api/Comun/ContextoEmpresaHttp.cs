using System.Security.Claims;
using Matchketing.Nucleo.Comun;

namespace Matchketing.Api.Comun;

/// <summary>Nombres de los claims del token. La empresa activa viaja dentro del JWT.</summary>
public static class Claims
{
    public const string UsuarioId = "uid";
    public const string EmpresaId = "eid";
    public const string NombreEmpresa = "enom";
    public const string Permiso = "perm";
}

/// <summary>
/// Resuelve la empresa activa de la petición a partir del token. Es la única fuente de verdad del
/// tenant: ningún endpoint acepta un `empresa_id` por parámetro (invariante T2).
/// </summary>
public sealed class ContextoEmpresaHttp(IHttpContextAccessor acceso) : IContextoEmpresa, IContextoEmpresaPublico
{
    private Guid? empresaPublica;

    public Guid? EmpresaId => Leer(Claims.EmpresaId) ?? empresaPublica;

    /// <summary>Solo para la entrada pública de leads. Ver <see cref="IContextoEmpresaPublico"/>.</summary>
    public void FijarEmpresa(Guid empresaId) => empresaPublica = empresaId;

    public Guid? UsuarioId => Leer(Claims.UsuarioId);

    public IReadOnlyCollection<string> Permisos =>
        acceso.HttpContext?.User.FindAll(Claims.Permiso).Select(c => c.Value).ToArray() ?? [];

    public bool Tiene(string permiso) => Permisos.Contains(permiso);

    private Guid? Leer(string claim)
    {
        var valor = acceso.HttpContext?.User.FindFirstValue(claim);
        return Guid.TryParse(valor, out var id) ? id : null;
    }
}
