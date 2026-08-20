using Matchketing.Contactos.Dominio;
using Matchketing.Correo.Aplicacion;
using Matchketing.Correo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioCorreo(ContextoMatchketing bd) : IRepositorioCorreo
{
    public Task<Plantilla?> PlantillaAsync(Guid id, CancellationToken ct = default) =>
        bd.Plantillas.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Plantilla>> PlantillasAsync(CancellationToken ct = default) =>
        await bd.Plantillas.ToListAsync(ct).ConfigureAwait(false);

    public void Anadir(Plantilla plantilla) => bd.Plantillas.Add(plantilla);

    public void Quitar(Plantilla plantilla) => bd.Plantillas.Remove(plantilla);

    public Task<Matchketing.Correo.Dominio.Correo?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Mensajes.FirstOrDefaultAsync(c => c.Id == id, ct);

    /// <summary>
    /// Busca por token **con el filtro de empresa puesto**, no saltándoselo.
    ///
    /// Funciona porque el endpoint del píxel fija antes la empresa que va dentro del propio token (ver
    /// <c>Correo.EmpresaDelToken</c>). Así esta consulta se comporta como cualquier otra: el filtro de
    /// EF y la RLS siguen aplicando, y un token no puede leer la fila de otra empresa ni por error de
    /// programación. Saltarse el filtro habría sido más corto y habría dejado una consulta sin ninguna
    /// de las dos barreras contra la tabla que guarda el texto de los correos.
    /// </summary>
    public Task<Matchketing.Correo.Dominio.Correo?> PorTokenAsync(string token, CancellationToken ct = default) =>
        bd.Mensajes.FirstOrDefaultAsync(c => c.TokenApertura == token, ct);

    public async Task<IReadOnlyList<Matchketing.Correo.Dominio.Correo>> PendientesAsync(
        DateTimeOffset hasta, int tope, CancellationToken ct = default) =>
        await bd.Mensajes
            .Where(c => c.Estado == EstadoCorreo.Encolado && c.ProximoIntentoEn != null && c.ProximoIntentoEn <= hasta)
            .OrderBy(c => c.CreadoEn)
            .Take(tope)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Matchketing.Correo.Dominio.Correo>> DeContactoAsync(
        Guid contactoId, int cuantos, CancellationToken ct = default) =>
        await bd.Mensajes
            .Where(c => c.ContactoId == contactoId)
            .OrderByDescending(c => c.CreadoEn)
            .Take(cuantos)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public void AnadirCorreo(Matchketing.Correo.Dominio.Correo correo) => bd.Mensajes.Add(correo);
}

/// <summary>
/// Los datos para rellenar una plantilla, en una sola consulta.
///
/// El nombre del comercial y el de la empresa salen de tablas de otros módulos, y por eso esto vive
/// aquí: la persistencia es la única capa que las conoce todas. Así el módulo de correo no referencia a
/// Contactos ni a Identidad, que es la regla del proyecto.
/// </summary>
public sealed class ConsultaDatosDelEnvio(ContextoMatchketing bd) : IConsultaDatosDelEnvio
{
    public async Task<DatosDelEnvio?> DeAsync(Guid contactoId, Guid usuarioId, CancellationToken ct = default)
    {
        var contacto = await bd.Contactos
            .Where(c => c.Id == contactoId)
            .Select(c => new { c.Nombre, c.Email, c.CuentaId, c.EmpresaId })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (contacto is null)
        {
            return null;
        }

        var cuenta = contacto.CuentaId is { } cuentaId
            ? await bd.Cuentas.Where(c => c.Id == cuentaId).Select(c => c.Nombre).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;

        // El nombre del comercial se busca sin filtro de empresa: `identidad.usuario` es una tabla
        // global —una persona puede estar en varias empresas— y no lleva `empresa_id`.
        var comercial = await bd.Usuarios
            .Where(u => u.Id == usuarioId)
            .Select(u => u.Nombre)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var empresa = await bd.Empresas
            .IgnoreQueryFilters()
            .Where(e => e.Id == contacto.EmpresaId)
            .Select(e => e.Nombre)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        // El nombre de pila y no el completo: «Hola Manolo García,» no lo escribiría nadie.
        var pila = contacto.Nombre?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return new DatosDelEnvio(pila, cuenta, comercial, empresa, contacto.Email);
    }
}
