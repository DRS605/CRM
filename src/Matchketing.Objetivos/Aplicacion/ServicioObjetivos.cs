using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Objetivos.Dominio;

namespace Matchketing.Objetivos.Aplicacion;

/// <summary>Cómo va una persona este mes. Nulo en <c>Avance</c> cuando no tiene objetivo puesto.</summary>
public sealed record ComoVa(Guid UsuarioId, string Nombre, DateOnly Mes, decimal Logrado, Avance? Avance);

/// <summary>
/// El mes del equipo entero. <paramref name="Objetivo"/> es la suma de los que hay puestos, no una cifra
/// aparte: un objetivo de empresa que no cuadre con la suma de los de su gente son dos verdades.
/// </summary>
public sealed record MesDelEquipo(
    DateOnly Mes,
    decimal Objetivo,
    decimal Logrado,
    int DiasLaborablesRestantes,
    IReadOnlyList<ComoVa> Personas)
{
    /// <summary>Nulo si nadie tiene objetivo: sin objetivo no hay porcentaje que dar.</summary>
    public int? Porcentaje => Objetivo <= 0m ? null : (int)Math.Round(Logrado * 100m / Objetivo);

    public bool HayObjetivos => Objetivo > 0m;
}

/// <summary>El objetivo de un mes ya pasado, con lo que se logró. Para el histórico de una persona.</summary>
public sealed record MesCerrado(DateOnly Mes, decimal Objetivo, decimal Logrado, int Porcentaje);

public sealed class ServicioObjetivos(
    IRepositorioObjetivos repositorio,
    IConsultaLogrado logrado,
    IConsultaEquipoObjetivos equipo,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    /// <summary>Cuántos meses de histórico se enseñan. Un año es lo que se compara con «el año pasado».</summary>
    public const int MesesDeHistorico = 12;

    /// <summary>
    /// Fija o cambia el objetivo de alguien para un mes. Si ya había uno, se cambia; no se crea otro.
    ///
    /// Es una sola operación y no un crear más un editar a propósito: quien pone objetivos rellena una
    /// tabla del equipo entero y no le importa cuáles existían ya. Dos endpoints le habrían obligado a
    /// saberlo.
    /// </summary>
    public async Task<Resultado<Objetivo>> FijarAsync(
        Guid usuarioId, DateOnly mes, decimal importe, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Objetivo>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        // Que la persona esté en el equipo se comprueba aquí y no en el dominio: el dominio no conoce la
        // identidad. Sin esto se podría poner objetivo a un identificador inventado, y aparecería una
        // fila sin nombre en la tabla del equipo.
        var gente = await equipo.ActivosAsync(ct).ConfigureAwait(false);
        if (!gente.Any(q => q.UsuarioId == usuarioId))
        {
            return Resultado.Fallo<Objetivo>(Error.NoEncontrado(
                "objetivo.persona_no_esta", "Esa persona no está en el equipo de esta empresa."));
        }

        var normalizado = Objetivo.MesDe(mes);
        var existente = await repositorio.DeAsync(usuarioId, normalizado, ct).ConfigureAwait(false);

        if (existente is not null)
        {
            var cambiado = existente.Cambiar(importe, reloj);
            return cambiado.Fallido ? Resultado.Fallo<Objetivo>(cambiado.Error!) : Resultado.Ok(existente);
        }

        var creado = Objetivo.Fijar(empresaId, usuarioId, normalizado, importe, reloj);
        if (creado.Exito)
        {
            repositorio.Anadir(creado.Valor);
        }

        return creado;
    }

    /// <summary>
    /// Lo quita. Quitar un objetivo no es ponerlo a cero: es decir «esta persona no tiene objetivo este
    /// mes», y entonces las pantallas dejan de enseñar la línea en vez de enseñar un 0 % permanente.
    /// </summary>
    public async Task<Resultado> QuitarAsync(Guid usuarioId, DateOnly mes, CancellationToken ct = default)
    {
        var objetivo = await repositorio.DeAsync(usuarioId, Objetivo.MesDe(mes), ct).ConfigureAwait(false);
        if (objetivo is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("objetivo.no_encontrado", "Ahí no hay ningún objetivo."));
        }

        if (Objetivo.MesDe(mes) < Objetivo.MesDe(reloj.AhoraUtc))
        {
            return Resultado.Fallo(Error.Validacion(
                "objetivo.mes_pasado", "El objetivo de un mes que ya pasó no se puede tocar."));
        }

        repositorio.Quitar(objetivo);
        return Resultado.Ok();
    }

    /// <summary>
    /// Cómo va **quien pregunta**, este mes. Es lo que se enseña en Hoy, y por eso devuelve nulo en vez
    /// de un error cuando no hay objetivo: no tener objetivo es normal, no es un fallo.
    /// </summary>
    public async Task<ComoVa?> MioAsync(CancellationToken ct = default)
    {
        if (contexto.UsuarioId is not { } usuarioId)
        {
            return null;
        }

        var mes = Objetivo.MesDe(reloj.AhoraUtc);
        var conseguido = await logrado.GanadoDeAsync(usuarioId, mes, ct).ConfigureAwait(false);
        var objetivo = await repositorio.DeAsync(usuarioId, mes, ct).ConfigureAwait(false);

        // Si no hay objetivo no se enseña nada, ni siquiera lo logrado: el número solo dice algo al lado
        // del compromiso. «Has ganado 12.400 € este mes» sin objetivo es una curiosidad, y Hoy no es
        // sitio para curiosidades.
        return objetivo is null
            ? null
            : new ComoVa(usuarioId, string.Empty, mes, conseguido, Avance.De(objetivo.Importe, conseguido, Hoy(), mes));
    }

    /// <summary>
    /// El mes del equipo: una fila por persona que vende, con o sin objetivo puesto.
    ///
    /// Salen **todos** los que venden, también los que no tienen objetivo, porque esta pantalla es donde
    /// se ponen: si solo apareciera quien ya tiene uno, no habría forma de darle objetivo a nadie nuevo.
    /// </summary>
    public async Task<MesDelEquipo> EquipoAsync(DateOnly? cual = null, CancellationToken ct = default)
    {
        var mes = Objetivo.MesDe(cual ?? Objetivo.MesDe(reloj.AhoraUtc));

        var gente = await equipo.ActivosAsync(ct).ConfigureAwait(false);
        var objetivos = (await repositorio.DelMesAsync(mes, ct).ConfigureAwait(false))
            .ToDictionary(o => o.UsuarioId);
        var ganado = await logrado.GanadoPorPersonaAsync(mes, ct).ConfigureAwait(false);

        var filas = gente
            .Where(q => q.Vende)
            .OrderBy(q => q.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(q =>
            {
                var suyo = ganado.TryGetValue(q.UsuarioId, out var cuanto) ? cuanto : 0m;
                var objetivo = objetivos.TryGetValue(q.UsuarioId, out var o) ? o : null;

                return new ComoVa(
                    q.UsuarioId, q.Nombre, mes, suyo,
                    objetivo is null ? null : Avance.De(objetivo.Importe, suyo, Hoy(), mes));
            })
            .ToArray();

        return new MesDelEquipo(
            mes,
            filas.Sum(f => f.Avance?.Objetivo ?? 0m),

            // Lo logrado suma **solo el de quien tiene objetivo**. Sumar lo de todos y compararlo con la
            // suma de unos pocos objetivos daría porcentajes de más del cien por cien sin que nadie
            // hubiera vendido más de lo previsto, que es la clase de número que hace inútil un panel.
            filas.Where(f => f.Avance is not null).Sum(f => f.Logrado),
            Avance.DiasLaborablesQueQuedan(Hoy(), mes),
            filas);
    }

    /// <summary>El histórico de una persona: qué se le pidió cada mes y qué hizo.</summary>
    public async Task<IReadOnlyList<MesCerrado>> HistoricoAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var objetivos = await repositorio
            .DePersonaAsync(usuarioId, MesesDeHistorico, ct)
            .ConfigureAwait(false);

        var cerrados = new List<MesCerrado>(objetivos.Count);
        foreach (var o in objetivos)
        {
            var conseguido = await logrado.GanadoDeAsync(usuarioId, o.Mes, ct).ConfigureAwait(false);
            cerrados.Add(new MesCerrado(
                o.Mes, o.Importe, conseguido,
                (int)Math.Round(conseguido * 100m / o.Importe)));
        }

        return cerrados;
    }

    private DateOnly Hoy() => HorasLaborables.DiaDeTrabajo(reloj.AhoraUtc);
}
