using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

/// <summary>Lecturas de la ficha y del listado. Devuelven vistas, no agregados.</summary>
public sealed class ConsultaContactos(ContextoMatchketing bd) : IConsultaContactos
{
    public async Task<IReadOnlyList<ContactoResumen>> ListarAsync(string? busqueda, EstadoContacto? estado, CancellationToken ct = default)
    {
        var consulta = bd.Contactos.Where(c => c.Activo);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var patron = $"%{busqueda.Trim()}%";
            consulta = consulta.Where(c =>
                EF.Functions.ILike(c.Nombre, patron) ||
                (c.Email != null && EF.Functions.ILike(c.Email, patron)) ||
                (c.Telefono != null && EF.Functions.ILike(c.Telefono, patron)) ||
                (c.Cargo != null && EF.Functions.ILike(c.Cargo, patron)));
        }

        if (estado is { } e)
        {
            consulta = consulta.Where(c => c.Estado == e);
        }

        return await consulta
            .OrderBy(c => c.Nombre)
            .Select(c => new ContactoResumen(
                c.Id, c.Nombre, c.Email, c.Telefono, c.Cargo, c.CuentaId,
                bd.Cuentas.Where(x => x.Id == c.CuentaId).Select(x => x.Nombre).FirstOrDefault(),
                c.Origen, c.Estado,
                bd.Actividades.Where(a => a.ContactoId == c.Id).Max(a => (DateTimeOffset?)a.OcurridaEn)))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<FichaContacto?> FichaAsync(Guid id, CancellationToken ct = default)
    {
        var resumen = await bd.Contactos
            .Where(c => c.Id == id)
            .Select(c => new ContactoResumen(
                c.Id, c.Nombre, c.Email, c.Telefono, c.Cargo, c.CuentaId,
                bd.Cuentas.Where(x => x.Id == c.CuentaId).Select(x => x.Nombre).FirstOrDefault(),
                c.Origen, c.Estado,
                bd.Actividades.Where(a => a.ContactoId == c.Id).Max(a => (DateTimeOffset?)a.OcurridaEn)))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (resumen is null)
        {
            return null;
        }

        var cronologia = await bd.Actividades
            .Where(a => a.ContactoId == id)
            .OrderByDescending(a => a.OcurridaEn)
            .Select(a => new ActividadVista(a.Id, a.Tipo, a.Sentido, a.Cuerpo, a.Resultado, a.OcurridaEn))
            .ToListAsync(ct).ConfigureAwait(false);

        return new FichaContacto(resumen, cronologia);
    }

    /// <summary>
    /// Parejas que parecen la misma persona: mismo correo o mismo teléfono normalizados. Se apoya
    /// en que la normalización ya ocurrió al guardar; si no, esto no encontraría nada.
    /// </summary>
    public async Task<IReadOnlyList<PropuestaDuplicado>> DuplicadosAsync(CancellationToken ct = default)
    {
        var emailsRepetidos = await bd.Contactos
            .Where(c => c.Activo && c.Email != null)
            .GroupBy(c => c.Email!)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(ct).ConfigureAwait(false);

        var telefonosRepetidos = await bd.Contactos
            .Where(c => c.Activo && c.Telefono != null)
            .GroupBy(c => c.Telefono!)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(ct).ConfigureAwait(false);

        if (emailsRepetidos.Count == 0 && telefonosRepetidos.Count == 0)
        {
            return [];
        }

        var afectados = await bd.Contactos
            .Where(c => c.Activo &&
                ((c.Email != null && emailsRepetidos.Contains(c.Email)) ||
                 (c.Telefono != null && telefonosRepetidos.Contains(c.Telefono))))
            .OrderBy(c => c.CreadoEn)
            .Select(c => new ContactoResumen(
                c.Id, c.Nombre, c.Email, c.Telefono, c.Cargo, c.CuentaId, null, c.Origen, c.Estado, null))
            .ToListAsync(ct).ConfigureAwait(false);

        var propuestas = new List<PropuestaDuplicado>();
        var yaEmparejados = new HashSet<string>(StringComparer.Ordinal);

        void Emparejar(Func<ContactoResumen, string?> clave, string motivo)
        {
            foreach (var grupo in afectados.Where(c => clave(c) is not null).GroupBy(clave!))
            {
                var miembros = grupo.ToList();
                for (var i = 0; i < miembros.Count - 1; i++)
                {
                    for (var j = i + 1; j < miembros.Count; j++)
                    {
                        var par = string.CompareOrdinal(miembros[i].Id.ToString(), miembros[j].Id.ToString()) < 0
                            ? $"{miembros[i].Id}|{miembros[j].Id}"
                            : $"{miembros[j].Id}|{miembros[i].Id}";

                        if (yaEmparejados.Add(par))
                        {
                            propuestas.Add(new PropuestaDuplicado(miembros[i], miembros[j], motivo));
                        }
                    }
                }
            }
        }

        Emparejar(c => c.Email, "Mismo correo electrónico");
        Emparejar(c => c.Telefono, "Mismo teléfono");

        return propuestas;
    }
}
