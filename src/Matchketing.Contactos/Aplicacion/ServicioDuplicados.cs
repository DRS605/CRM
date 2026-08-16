using Matchketing.Contactos.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Contactos.Aplicacion;

/// <summary>
/// Fusión de duplicados. El sistema **propone**, la persona **aprueba** (invariante C3): fusionar
/// automáticamente es la forma más rápida de destrozar una base de datos de clientes.
/// </summary>
public sealed class ServicioDuplicados(
    IRepositorioContactos contactos,
    IRepositorioActividades actividades,
    IConsultaContactos consulta,
    IReloj reloj)
{
    public Task<IReadOnlyList<PropuestaDuplicado>> ProponerAsync(CancellationToken ct = default) =>
        consulta.DuplicadosAsync(ct);

    /// <summary>
    /// Fusiona <paramref name="absorbidoId"/> dentro de <paramref name="supervivienteId"/>: rellena
    /// los huecos, mueve **todas** las actividades y deja el rastro. No se borra nada (C4).
    /// </summary>
    public async Task<Resultado<int>> FusionarAsync(Guid supervivienteId, Guid absorbidoId, CancellationToken ct = default)
    {
        var superviviente = await contactos.BuscarPorIdAsync(supervivienteId, ct).ConfigureAwait(false);
        if (superviviente is null)
        {
            return Resultado.Fallo<int>(Error.NoEncontrado("contacto.no_encontrado", "El contacto que se queda no existe."));
        }

        var absorbido = await contactos.BuscarPorIdAsync(absorbidoId, ct).ConfigureAwait(false);
        if (absorbido is null)
        {
            return Resultado.Fallo<int>(Error.NoEncontrado("contacto.no_encontrado", "El contacto que se absorbe no existe."));
        }

        var fusion = superviviente.Absorber(absorbido, reloj);
        if (fusion.Fallido)
        {
            return Resultado.Fallo<int>(fusion.Error!);
        }

        var movidas = await actividades.ReasignarAsync(absorbidoId, supervivienteId, ct).ConfigureAwait(false);

        var apunte = Actividad.Crear(
            superviviente.EmpresaId, supervivienteId, TipoActividad.Sistema, SentidoActividad.Interna,
            $"Fusionado con «{absorbido.Nombre}». Se han traído {movidas} actividades.", null, reloj);
        if (apunte.Exito)
        {
            actividades.Anadir(apunte.Valor);
        }

        return Resultado.Ok(movidas);
    }
}
