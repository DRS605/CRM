using System.Text.Json;
using Matchketing.Auditoria.Aplicacion;
using Matchketing.Auditoria.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

/// <summary>
/// Escribe la auditoría **en el mismo <see cref="ContextoMatchketing"/>** que la operación auditada,
/// sin llamar a <c>SaveChanges</c>. Así el apunte viaja en la transacción de quien lo provocó: si la
/// operación se deshace, el apunte también, y no queda constancia de algo que no llegó a pasar.
/// </summary>
public sealed class RegistradorAuditoria(ContextoMatchketing bd, IContextoEmpresa contexto, IReloj reloj) : IRegistradorAuditoria
{
    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = false };

    public void Registrar(string entidad, Guid? entidadId, string accion, object? detalle = null)
    {
        // Sin empresa activa no hay operación de negocio: no se inventa un apunte huérfano.
        if (contexto.EmpresaId is not { } empresaId)
        {
            return;
        }

        Anotar(empresaId, contexto.UsuarioId, entidad, entidadId, accion, detalle);
    }

    public void RegistrarDelSistema(Guid empresaId, string entidad, Guid? entidadId, string accion, object? detalle = null) =>
        Anotar(empresaId, null, entidad, entidadId, accion, detalle);

    public async Task<IReadOnlyList<LineaAuditoria>> UltimosAsync(int cuantos, CancellationToken ct = default)
    {
        var tope = Math.Clamp(cuantos, 1, 500);

        // `usuario` no lleva filtro de empresa (es global), así que el join se hace a mano y en
        // LEFT: las acciones del sistema no tienen actor y no deben desaparecer del listado.
        var filas = await (
            from r in bd.RegistrosAuditoria
            orderby r.En descending, r.Id
            join u in bd.Usuarios on r.ActorId equals u.Id into posibles
            from u in posibles.DefaultIfEmpty()
            select new
            {
                r.Id,
                r.ActorId,
                Nombre = u == null ? null : u.Nombre,
                r.Entidad,
                r.EntidadId,
                r.Accion,
                r.Detalle,
                r.En,
            })
            .Take(tope)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return filas
            .Select(f => new LineaAuditoria(f.Id, f.ActorId, f.Nombre ?? "el sistema", f.Entidad, f.EntidadId, f.Accion, f.Detalle, f.En))
            .ToList();
    }

    private void Anotar(Guid empresaId, Guid? actorId, string entidad, Guid? entidadId, string accion, object? detalle)
    {
        var json = detalle is null ? null : Detalles.Tapar(JsonSerializer.Serialize(detalle, Opciones));
        bd.RegistrosAuditoria.Add(RegistroAuditoria.Crear(empresaId, actorId, entidad, entidadId, accion, json, reloj));
    }
}
