using Matchketing.Captacion.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Captacion.Aplicacion;

public sealed class ServicioFormularios(
    IRepositorioFormularios formularios,
    IRepositorioEnvios envios,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    public Resultado<Formulario> Crear(string? nombre, string? textoConsentimiento, bool pideTelefono, bool pideEmpresa, bool pideMensaje, string? paginaGracias, string? origen)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Formulario>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var creado = Formulario.Crear(empresaId, nombre, textoConsentimiento, pideTelefono, pideEmpresa, pideMensaje, paginaGracias, origen, reloj);
        if (creado.Exito)
        {
            formularios.Anadir(creado.Valor);
        }

        return creado;
    }

    public async Task<IReadOnlyList<ResumenFormulario>> ListarAsync(CancellationToken ct = default)
    {
        var lista = await formularios.ActivosAsync(ct).ConfigureAwait(false);
        var resumenes = new List<ResumenFormulario>(lista.Count);

        foreach (var f in lista)
        {
            resumenes.Add(new ResumenFormulario(
                f.Id, f.Nombre, f.Clave, f.TextoConsentimiento, f.PideTelefono, f.PideEmpresa,
                f.PideMensaje, f.PaginaGracias, f.Origen,
                await envios.ContarDeFormularioAsync(f.Id, ct).ConfigureAwait(false)));
        }

        return resumenes;
    }

    public async Task<Resultado> ActualizarAsync(Guid id, string? nombre, string? textoConsentimiento, bool pideTelefono, bool pideEmpresa, bool pideMensaje, string? paginaGracias, CancellationToken ct = default)
    {
        var f = await formularios.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        return f is null
            ? Resultado.Fallo(Error.NoEncontrado("formulario.no_encontrado", "El formulario no existe."))
            : f.Actualizar(nombre, textoConsentimiento, pideTelefono, pideEmpresa, pideMensaje, paginaGracias);
    }

    public async Task<Resultado> DesactivarAsync(Guid id, CancellationToken ct = default)
    {
        var f = await formularios.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        if (f is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("formulario.no_encontrado", "El formulario no existe."));
        }

        f.Desactivar();
        return Resultado.Ok();
    }

    public Task<Formulario?> PorClaveAsync(string clave, CancellationToken ct = default) =>
        formularios.BuscarPorClaveAsync(clave, ct);

    public void RegistrarEnvio(Guid empresaId, Guid formularioId, string datos, string? ip, string? agente, Guid? contactoId) =>
        envios.Anadir(EnvioFormulario.Crear(empresaId, formularioId, datos, ip, agente, contactoId, reloj));
}
