using Matchketing.Campanias.Aplicacion;
using Matchketing.Campanias.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioCampanias(ContextoMatchketing bd) : IRepositorioCampanias
{
    public Task<Segmento?> SegmentoAsync(Guid id, CancellationToken ct = default) =>
        bd.Segmentos.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Segmento>> SegmentosAsync(CancellationToken ct = default) =>
        await bd.Segmentos.ToListAsync(ct).ConfigureAwait(false);

    public void Anadir(Segmento segmento) => bd.Segmentos.Add(segmento);

    public void Quitar(Segmento segmento) => bd.Segmentos.Remove(segmento);

    public Task<Campania?> CampaniaAsync(Guid id, CancellationToken ct = default) =>
        bd.Campanias.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Campania>> CampaniasAsync(CancellationToken ct = default) =>
        await bd.Campanias.ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<Campania>> EnMarchaAsync(CancellationToken ct = default) =>
        await bd.Campanias
            .Where(c => c.Estado == EstadoCampania.Enviando)
            .OrderBy(c => c.LanzadaEn)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public Task<int> CuantasUsanAsync(Guid segmentoId, CancellationToken ct = default) =>
        bd.Campanias.CountAsync(c => c.SegmentoId == segmentoId, ct);

    public void Anadir(Campania campania) => bd.Campanias.Add(campania);

    public void Quitar(Campania campania) => bd.Campanias.Remove(campania);

    public void Anadir(IReadOnlyList<EnvioCampania> envios)
    {
        ArgumentNullException.ThrowIfNull(envios);

        // `AddRange` y no un `Add` por fila: dos mil llamadas a `Add` hacen que el rastreador de cambios
        // recorra su grafo dos mil veces y lanzar una campaña grande pasa de un segundo a un minuto.
        bd.EnviosCampania.AddRange(envios);
    }

    /// <summary>
    /// El siguiente lote de pendientes, en el orden en que se congelaron.
    ///
    /// El orden es `id` y no `resuelto_en`, que está a nulo mientras están pendientes: sin un orden
    /// estable, dos pasadas seguidas podrían traer las mismas filas y dejar a las de la cola sin
    /// atender nunca. Con orden estable, cada pasada empieza donde acabó la anterior.
    /// </summary>
    public async Task<IReadOnlyList<EnvioCampania>> PendientesAsync(
        Guid campaniaId, int tope, CancellationToken ct = default) =>
        await bd.EnviosCampania
            .Where(e => e.CampaniaId == campaniaId && e.Estado == EstadoEnvio.Pendiente)
            .OrderBy(e => e.Id)
            .Take(tope)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<EnvioCampania>> TodosLosPendientesAsync(
        Guid campaniaId, CancellationToken ct = default) =>
        await bd.EnviosCampania
            .Where(e => e.CampaniaId == campaniaId && e.Estado == EstadoEnvio.Pendiente)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<EnvioCampania>> ExcluidosAsync(
        Guid campaniaId, int tope, CancellationToken ct = default) =>
        await bd.EnviosCampania
            .Where(e => e.CampaniaId == campaniaId && e.Estado == EstadoEnvio.Excluido)
            .OrderBy(e => e.ResueltoEn)
            .Take(tope)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}

/// <summary>
/// Qué fue de los correos de una campaña: junta la tabla de envíos con el buzón de salida.
///
/// Vive aquí y no en el módulo de campañas porque cruza dos esquemas —`campania.envio` y
/// `correo.mensaje`— y ningún módulo de negocio conoce al otro. Es el mismo sitio y el mismo motivo que
/// <c>ConsultaInformes</c> o <c>ConsultaRepaso</c>: los modelos de lectura que cruzan módulos son cosa
/// de la capa que puede mirar todas las tablas.
/// </summary>
public sealed class ConsultaCampanias(ContextoMatchketing bd) : IConsultaEnviosDeCampania
{
    public async Task<ContadoresCorreo> ContadoresAsync(Guid campaniaId, CancellationToken ct = default)
    {
        // Una sola consulta con `join`, no cinco cuentas. Y por el identificador del correo que guardó el
        // envío, no por «los correos de esos contactos»: ese contacto puede haber recibido otros correos
        // que no son de esta campaña, y contarlos aquí infla las aperturas de la campaña con lecturas de
        // mensajes que no eran suyos. Es la clase de número inflado que hace inútil un informe.
        var filas = await (
            from envio in bd.EnviosCampania
            where envio.CampaniaId == campaniaId && envio.CorreoId != null
            join mensaje in bd.Mensajes on envio.CorreoId equals mensaje.Id
            select new { mensaje.Estado, Abierto = mensaje.PrimeraAperturaEn != null })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new ContadoresCorreo(
            filas.Count(f => f.Estado == Correo.Dominio.EstadoCorreo.Enviado),
            filas.Count(f => f.Estado == Correo.Dominio.EstadoCorreo.Encolado),
            filas.Count(f => f.Estado == Correo.Dominio.EstadoCorreo.Fallido),
            filas.Count(f => f.Estado == Correo.Dominio.EstadoCorreo.Cancelado),
            filas.Count(f => f.Abierto));
    }
}
