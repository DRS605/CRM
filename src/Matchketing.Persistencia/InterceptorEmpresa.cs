using System.Data.Common;
using Matchketing.Nucleo.Comun;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Matchketing.Persistencia;

/// <summary>
/// Fija <c>app.empresa_actual</c> en cada conexión que se abre. Es lo que activa las políticas de
/// RLS de PostgreSQL: la segunda barrera, por debajo del filtro global de EF Core.
/// </summary>
public sealed class InterceptorEmpresa(IContextoEmpresa contexto) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(DbConnection conexion, ConnectionEndEventData datos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conexion);

        // Siempre se fija, también cuando no hay empresa: dejar el valor de la petición anterior en
        // una conexión reutilizada del pool sería exactamente la fuga que esto viene a evitar.
        await using var orden = conexion.CreateCommand();
        orden.CommandText = "SELECT set_config('app.empresa_actual', $1, false)";

        var p = orden.CreateParameter();
        p.Value = contexto.EmpresaId?.ToString() ?? string.Empty;
        orden.Parameters.Add(p);

        await orden.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(conexion, datos, ct).ConfigureAwait(false);
    }
}
