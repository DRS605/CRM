using Matchketing.Campanias.Aplicacion;
using Matchketing.Campanias.Dominio;
using Matchketing.Contactos.Dominio;
using Matchketing.Nucleo.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

/// <summary>
/// Traduce los criterios de un segmento a una consulta.
///
/// Vive aquí porque los criterios tocan cuatro tablas de módulos distintos —contactos, cuentas,
/// puntuaciones de match y oportunidades— y ningún módulo de negocio conoce a los otros. El módulo de
/// campañas dice qué significa un criterio; esto lo convierte en SQL.
///
/// Y hace **dos exclusiones que no se pueden pedir ni quitar**, porque no son filtros, son la ley:
///
/// · Quien está de baja no entra. Nunca. Ni con el criterio de estado puesto a otra cosa. Que quede
///   fuera aquí, y no más adelante en la comprobación de permiso, es defensa en profundidad: si algún
///   día alguien se salta el permiso, la audiencia ya no lo contenía.
/// · Quien no tiene dirección de correo tampoco. No es un destinatario, y meterlo en la audiencia solo
///   serviría para inflar el número de excluidos con gente que nunca pudo estar dentro.
///
/// Lo que **no** hace es filtrar por consentimiento. Es deliberado y es lo contrario de lo que parece:
/// el consentimiento se comprueba persona a persona en el momento de encolar cada correo, y así el que
/// no lo tiene aparece en la ficha de la campaña con su motivo escrito. Si se filtrara aquí, esa gente
/// desaparecería del informe y nadie sabría nunca cuánta base de datos tiene sin permiso, que es
/// justamente el número que hace falta para arreglarlo.
/// </summary>
public sealed class ConsultaSegmentos(ContextoMatchketing bd, IReloj reloj) : IBuscaContactosDelSegmento
{
    public async Task<IReadOnlyList<Guid>> ResolverAsync(
        CriteriosSegmento criterios, int tope, CancellationToken ct = default) =>
        await Consulta(criterios)
            .OrderBy(c => c.CreadoEn)
            .Select(c => c.Id)
            .Take(tope)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public Task<int> ContarAsync(CriteriosSegmento criterios, CancellationToken ct = default) =>
        Consulta(criterios).CountAsync(ct);

    public async Task<IReadOnlyList<QuienRecibe>> MuestraAsync(
        CriteriosSegmento criterios, int cuantos, CancellationToken ct = default) =>
        await Consulta(criterios)
            .OrderBy(c => c.Nombre)
            .Take(cuantos)
            .Select(c => new QuienRecibe(c.Id, c.Nombre, c.Email))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<string?> NombreDeEtapaAsync(Guid etapaId, CancellationToken ct = default) =>
        await bd.Etapas
            .Where(e => e.Id == etapaId)
            .Select(e => e.Nombre)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    private IQueryable<Contacto> Consulta(CriteriosSegmento criterios)
    {
        ArgumentNullException.ThrowIfNull(criterios);

        // El punto de partida ya lleva las dos exclusiones inamovibles. Empezar por aquí y no por
        // `bd.Contactos` a secas es lo que hace imposible olvidarse de ellas al añadir un criterio nuevo.
        var consulta = bd.Contactos
            .Where(c => c.Activo
                && c.Estado != EstadoContacto.Baja
                && c.Email != null
                && c.Email != string.Empty);

        if (criterios.Estado is { } estado)
        {
            var buscado = Traducir(estado);
            consulta = consulta.Where(c => c.Estado == buscado);
        }

        if (!string.IsNullOrWhiteSpace(criterios.Provincia))
        {
            // La provincia está en la **cuenta**, no en el contacto. Así que un contacto sin cuenta —una
            // persona particular— nunca cumple un criterio de provincia, y eso es correcto: no sabemos
            // dónde está. Lo raro sería incluirlo por si acaso.
            //
            // `ILike` y no `ToLower() ==`: con `InvariantGlobalization` activo, comparar en minúsculas en
            // el servidor no es de fiar, y `ILike` lo resuelve PostgreSQL con su propia colación.
            var provincia = criterios.Provincia.Trim();
            consulta = consulta.Where(c => bd.Cuentas
                .Any(cu => cu.Id == c.CuentaId && cu.Provincia != null && EF.Functions.ILike(cu.Provincia, provincia)));
        }

        if (!string.IsNullOrWhiteSpace(criterios.Origen))
        {
            var origen = criterios.Origen.Trim();
            consulta = consulta.Where(c => EF.Functions.ILike(c.Origen, origen));
        }

        if (criterios.MatchMinimo is { } minimo)
        {
            // Con `Match` a nulo no entra. Un contacto sin puntuación no es un contacto con puntuación
            // baja: es uno del que todavía no se sabe, y no se le manda una campaña «a los de match alto».
            consulta = consulta.Where(c => bd.Puntuaciones
                .Any(p => p.ContactoId == c.Id && p.Match != null && p.Match >= minimo));
        }

        if (criterios.SinActividadDias is { } dias)
        {
            var limite = reloj.AhoraUtc.AddDays(-dias);

            // Sin **ninguna** actividad después del límite. Y quien no tiene ninguna actividad en absoluto
            // también cuenta: un contacto que entró hace ocho meses y con el que nadie ha hecho nada es el
            // caso más claro de «sin actividad», y con un `Max()` sobre una tabla vacía se habría quedado
            // fuera sin que nadie lo notase.
            consulta = consulta.Where(c => !bd.Actividades
                .Any(a => a.ContactoId == c.Id && a.OcurridaEn > limite));
        }

        if (criterios.EtapaId is { } etapaId)
        {
            // Abierta: ni ganada ni perdida. Una oportunidad cerrada en «Propuesta» no dice que el
            // contacto esté hoy en propuesta, dice que lo estuvo.
            consulta = consulta.Where(c => bd.Oportunidades
                .Any(o => o.ContactoId == c.Id && o.EtapaId == etapaId && o.CerradaEn == null));
        }

        return consulta;
    }

    /// <summary>
    /// Del estado que se puede buscar al estado real del contacto.
    ///
    /// Es un `switch` a mano y no un cast de enteros. Los valores coinciden hoy, y un cast habría
    /// funcionado; también habría convertido cualquier renumeración futura de uno de los dos enumerados en
    /// un segmento que apunta silenciosamente a otra gente. El `switch` no compila si aparece un valor
    /// nuevo sin decidir qué es.
    /// </summary>
    private static EstadoContacto Traducir(EstadoBuscado estado) => estado switch
    {
        EstadoBuscado.Lead => EstadoContacto.Lead,
        EstadoBuscado.Cliente => EstadoContacto.Cliente,
        EstadoBuscado.Perdido => EstadoContacto.Perdido,
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, "Estado buscado sin traducción."),
    };
}
