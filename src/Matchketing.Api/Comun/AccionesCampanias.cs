using Matchketing.Campanias.Aplicacion;
using Matchketing.Correo.Aplicacion;
using Matchketing.Correo.Dominio;

namespace Matchketing.Api.Comun;

/// <summary>
/// El gancho entre campañas y correo. Vive en la API porque es la única capa que conoce a los dos,
/// igual que <c>AccionesAutomatizacion</c> y <c>AccionesRepaso</c>.
///
/// Lo que hace es una sola cosa y es la que importa: **manda el correo de campaña por el mismo camino
/// que un correo escrito a mano**. Mismo `ServicioCorreo`, misma comprobación de permiso, mismo buzón
/// de salida, misma anotación en la cronología del contacto. La campaña no tiene un atajo, y por eso el
/// consentimiento se sigue comprobando dos veces por persona: aquí y otra vez justo antes de que salga.
///
/// Un fallo aquí es casi siempre lo correcto y no una avería: «no ha dado su consentimiento comercial»,
/// «se dio de baja», «no tiene ese dato para rellenar la plantilla». Por eso el mensaje se devuelve tal
/// cual: es lo que se guarda en la exclusión y lo que se lee en la ficha de la campaña.
/// </summary>
public sealed class AccionesCampanias(ServicioCorreo correo, IRepositorioCorreo repositorio)
    : IEncolaCorreoDeCampania, IPlantillaDeCampania
{
    public async Task<Nucleo.Resultados.Resultado<Guid>> EncolarAsync(
        Guid contactoId, Guid plantillaId, Guid enNombreDe, CancellationToken ct = default)
    {
        var r = await correo
            .EnviarEnNombreDeAsync(enNombreDe, contactoId, plantillaId, ct)
            .ConfigureAwait(false);

        return r.Fallido
            ? Nucleo.Resultados.Resultado.Fallo<Guid>(r.Error!)
            : Nucleo.Resultados.Resultado.Ok(r.Valor.Id);
    }

    public async Task<DatosPlantilla?> DeAsync(Guid plantillaId, CancellationToken ct = default)
    {
        var plantilla = await repositorio.PlantillaAsync(plantillaId, ct).ConfigureAwait(false);

        return plantilla is null
            ? null
            : new DatosPlantilla(
                plantilla.Id, plantilla.Nombre, plantilla.Asunto,
                EsComercial: plantilla.ParaQue == ParaQue.Comercial);
    }
}
