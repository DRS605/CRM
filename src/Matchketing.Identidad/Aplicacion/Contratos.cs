using Matchketing.Identidad.Dominio;

namespace Matchketing.Identidad.Aplicacion;

public sealed record UsuarioResumen(Guid Id, string Nombre, string Email);

public sealed record EmpresaDeUsuario(Guid Id, string Nombre, Rol Rol);

public sealed record RespuestaSesion(
    string Token,
    DateTimeOffset ExpiraEn,
    UsuarioResumen Usuario,
    Guid? EmpresaId,
    string? NombreEmpresa,
    IReadOnlyList<string> Permisos);
