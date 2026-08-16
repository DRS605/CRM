using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Tiempo;
using Microsoft.IdentityModel.Tokens;

namespace Matchketing.Api.Comun;

public sealed record AjustesJwt(string Clave, string Emisor, string Audiencia, int MinutosVigencia);

public sealed class GeneradorJwt(AjustesJwt ajustes, IReloj reloj) : IGeneradorTokens
{
    public TokenGenerado Generar(Usuario usuario, Membresia? membresia, string? nombreEmpresa)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        var expira = reloj.AhoraUtc.AddMinutes(ajustes.MinutosVigencia);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(Claims.UsuarioId, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Nombre),
        };

        if (membresia is not null)
        {
            claims.Add(new Claim(Claims.EmpresaId, membresia.EmpresaId.ToString()));
            claims.Add(new Claim(Claims.NombreEmpresa, nombreEmpresa ?? string.Empty));
            claims.Add(new Claim(ClaimTypes.Role, membresia.Rol.ToString()));
            claims.AddRange(membresia.Permisos.Select(p => new Claim(Claims.Permiso, p)));
        }

        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ajustes.Clave)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: ajustes.Emisor,
            audience: ajustes.Audiencia,
            claims: claims,
            notBefore: reloj.AhoraUtc.UtcDateTime,
            expires: expira.UtcDateTime,
            signingCredentials: credenciales);

        return new TokenGenerado(new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}
