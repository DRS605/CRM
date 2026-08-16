using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Tareas.Dominio;

namespace Matchketing.Tareas.Aplicacion;

public sealed class ServicioTareas(
    IRepositorioTareas tareas,
    IConsultaHoy consulta,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    public Task<PilaHoy> HoyAsync(CancellationToken ct = default) => consulta.PilaAsync(ct);

    public Task<IReadOnlyList<TareaVista>> ListarAsync(bool soloPendientes = true, CancellationToken ct = default) =>
        consulta.ListarAsync(soloPendientes, ct);

    public Resultado<Tarea> Crear(string? titulo, Guid? contactoId, Guid? oportunidadId, DateOnly? venceEl, OrigenTarea origen = OrigenTarea.Manual)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Tarea>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var creada = Tarea.Crear(empresaId, titulo, contactoId, oportunidadId, venceEl, contexto.UsuarioId, reloj, origen);
        if (creada.Exito)
        {
            tareas.Anadir(creada.Valor);
        }

        return creada;
    }

    /// <summary>
    /// La tarea que el sistema crea solo cuando una llamada acaba en «volver a llamar». Si ya hay
    /// una pendiente para ese contacto con el mismo título, no se duplica: Hoy debe ser una lista
    /// corta, no un montón de recordatorios repetidos.
    /// </summary>
    public async Task<Resultado<Tarea>> CrearSeguimientoLlamadaAsync(Guid contactoId, CancellationToken ct = default)
    {
        const string titulo = "Volver a llamar";

        var pendientes = await tareas.PendientesDeContactoAsync(contactoId, ct).ConfigureAwait(false);
        if (pendientes.Any(t => t.Titulo == titulo))
        {
            return Resultado.Fallo<Tarea>(Error.Conflicto("tarea.ya_existe", "Ya hay un seguimiento pendiente para este contacto."));
        }

        var manana = DateOnly.FromDateTime(reloj.AhoraUtc.UtcDateTime).AddDays(1);
        return Crear(titulo, contactoId, null, manana, OrigenTarea.Automatica);
    }

    public async Task<Resultado> CompletarAsync(Guid id, CancellationToken ct = default)
    {
        var tarea = await tareas.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        return tarea is null
            ? Resultado.Fallo(Error.NoEncontrado("tarea.no_encontrada", "La tarea no existe."))
            : tarea.Completar(reloj);
    }

    public async Task<Resultado> DescartarAsync(Guid id, CancellationToken ct = default)
    {
        var tarea = await tareas.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        return tarea is null
            ? Resultado.Fallo(Error.NoEncontrado("tarea.no_encontrada", "La tarea no existe."))
            : tarea.Descartar(reloj);
    }

    public async Task<Resultado> AplazarAsync(Guid id, DateOnly? hasta, CancellationToken ct = default)
    {
        var tarea = await tareas.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        return tarea is null
            ? Resultado.Fallo(Error.NoEncontrado("tarea.no_encontrada", "La tarea no existe."))
            : tarea.Aplazar(hasta, reloj);
    }

    public async Task<Resultado> ActualizarAsync(Guid id, string? titulo, DateOnly? venceEl, Guid? responsableId, CancellationToken ct = default)
    {
        var tarea = await tareas.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        return tarea is null
            ? Resultado.Fallo(Error.NoEncontrado("tarea.no_encontrada", "La tarea no existe."))
            : tarea.Actualizar(titulo, venceEl, responsableId, reloj);
    }
}
