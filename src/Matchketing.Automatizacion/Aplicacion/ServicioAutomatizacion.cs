using Matchketing.Automatizacion.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Automatizacion.Aplicacion;

public sealed class ServicioAutomatizacion(
    IRepositorioReglas repositorio,
    IConsultaHechos hechos,
    IAccionesAutomatizacion acciones,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    /// <summary>
    /// Cuántas reglas puede tener una empresa. Veinte es mucho más de lo que nadie va a poder tener en la
    /// cabeza: pasada esa cifra, nadie sabe ya por qué el CRM hace lo que hace.
    /// </summary>
    public const int MaximoPorEmpresa = 20;

    public const int HistorialVisible = 20;

    // ---------- Gestión ----------

    public async Task<Resultado<Regla>> CrearAsync(
        string? nombre, Disparador disparador,
        IReadOnlyCollection<Condicion>? condiciones, IReadOnlyCollection<Accion>? acciones,
        CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Regla>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var todas = await repositorio.DeLaEmpresaAsync(ct).ConfigureAwait(false);
        if (todas.Count >= MaximoPorEmpresa)
        {
            return Resultado.Fallo<Regla>(Error.Conflicto(
                "regla.demasiadas", $"No se pueden tener más de {MaximoPorEmpresa} reglas."));
        }

        var creada = Regla.Crear(empresaId, nombre, disparador, condiciones, acciones, reloj);
        if (creada.Exito)
        {
            repositorio.Anadir(creada.Valor);
        }

        return creada;
    }

    public async Task<IReadOnlyList<FichaRegla>> ListarAsync(CancellationToken ct = default)
    {
        var todas = await repositorio.DeLaEmpresaAsync(ct).ConfigureAwait(false);

        // Las activas primero: son las que están haciendo algo ahora mismo, y son las que hay que poder
        // ver de un vistazo cuando el CRM hace algo que no esperabas.
        return todas
            .OrderByDescending(r => r.Activa)
            .ThenBy(r => r.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(Ficha)
            .ToArray();
    }

    public async Task<Resultado> CambiarAsync(
        Guid id, string? nombre, Disparador disparador,
        IReadOnlyCollection<Condicion>? condiciones, IReadOnlyCollection<Accion>? acciones,
        CancellationToken ct = default)
    {
        var regla = await repositorio.PorIdAsync(id, ct).ConfigureAwait(false);
        return regla is null
            ? Resultado.Fallo(Error.NoEncontrado("regla.no_encontrada", "Esa regla no existe."))
            : regla.Cambiar(nombre, disparador, condiciones, acciones);
    }

    public async Task<Resultado<bool>> EncenderAsync(Guid id, bool encender, CancellationToken ct = default)
    {
        var regla = await repositorio.PorIdAsync(id, ct).ConfigureAwait(false);
        if (regla is null)
        {
            return Resultado.Fallo<bool>(Error.NoEncontrado("regla.no_encontrada", "Esa regla no existe."));
        }

        if (encender)
        {
            regla.Encender();
        }
        else
        {
            regla.Apagar();
        }

        return Resultado.Ok(regla.Activa);
    }

    public async Task<Resultado> BorrarAsync(Guid id, CancellationToken ct = default)
    {
        var regla = await repositorio.PorIdAsync(id, ct).ConfigureAwait(false);
        if (regla is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("regla.no_encontrada", "Esa regla no existe."));
        }

        repositorio.Quitar(regla);
        return Resultado.Ok();
    }

    public async Task<Resultado<IReadOnlyList<FichaEjecucion>>> HistorialAsync(Guid id, CancellationToken ct = default)
    {
        if (await repositorio.PorIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Resultado.Fallo<IReadOnlyList<FichaEjecucion>>(
                Error.NoEncontrado("regla.no_encontrada", "Esa regla no existe."));
        }

        var lista = await repositorio.UltimasDeAsync(id, HistorialVisible, ct).ConfigureAwait(false);

        return Resultado.Ok<IReadOnlyList<FichaEjecucion>>(lista
            .Select(e => new FichaEjecucion(e.Id, e.SujetoId, e.ContactoId, e.QueHizo, e.CuandoEn))
            .ToArray());
    }

    /// <summary>
    /// Qué haría esta regla con este contacto, **sin hacerlo**.
    ///
    /// Existe porque una regla no se puede probar de otra forma: lo que hace es irreversible y encenderla
    /// «para ver qué pasa» es exactamente lo que no se debe hacer. Con esto se coge un contacto de verdad,
    /// se mira si cumpliría y se lee lo que haría.
    /// </summary>
    public async Task<Resultado<Ensayo>> EnsayarAsync(Guid id, Guid contactoId, CancellationToken ct = default)
    {
        var regla = await repositorio.PorIdAsync(id, ct).ConfigureAwait(false);
        if (regla is null)
        {
            return Resultado.Fallo<Ensayo>(Error.NoEncontrado("regla.no_encontrada", "Esa regla no existe."));
        }

        var suyos = await hechos.DeContactoAsync(contactoId, ct).ConfigureAwait(false);
        if (suyos is null)
        {
            return Resultado.Fallo<Ensayo>(Error.NoEncontrado("contacto.no_encontrado", "Ese contacto no existe."));
        }

        var incumplida = regla.Condiciones.FirstOrDefault(c => !c.Cumple(suyos));
        var haria = regla.Acciones.Select(a => a.Leer()).ToArray();

        // Con el ensayo se ignora si la regla está encendida: la pregunta es «¿cumpliría?», no «¿está
        // funcionando?». Y si ya actuó sobre este contacto se dice, porque es el motivo más común de que
        // alguien crea que su regla no funciona.
        if (incumplida is not null)
        {
            return Resultado.Ok(new Ensayo(false, $"No cumple: {incumplida.Leer()}.", haria));
        }

        var yaFue = await repositorio.YaActuoAsync(id, contactoId, ct).ConfigureAwait(false);
        return Resultado.Ok(yaFue
            ? new Ensayo(false, "Cumple, pero esta regla ya actuó sobre él: actúa una sola vez por contacto.", haria)
            : new Ensayo(true, null, haria));
    }

    // ---------- Disparo ----------

    /// <summary>
    /// Ejecuta las reglas que apliquen. La llama el despachador de eventos, dentro de la misma
    /// transacción que el cambio que las provocó.
    ///
    /// **No devuelve fallo nunca.** Si esto pudiera tumbar la operación, una regla mal escrita impediría
    /// ganar una oportunidad. Una automatización es un extra; el negocio no depende de ella.
    /// </summary>
    /// <summary>
    /// ¿Hay alguna regla encendida que pueda aplicar a esto? Se pregunta **antes de guardar**, para no
    /// abrir una transacción ni hacer un segundo guardado cuando no hay ninguna regla, que es el caso de
    /// casi todo el mundo. Las reglas ya están en la base, así que esta consulta sí se puede hacer antes.
    /// </summary>
    public async Task<bool> HayReglasParaAsync(IReadOnlyCollection<Ocurrencia> ocurrencias, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ocurrencias);

        foreach (var disparador in ocurrencias.Select(o => o.Disparador).Distinct())
        {
            if ((await repositorio.ActivasParaAsync(disparador, ct).ConfigureAwait(false)).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<int> DispararAsync(IReadOnlyCollection<Ocurrencia> ocurrencias, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ocurrencias);

        if (contexto.EmpresaId is not { } empresaId || ocurrencias.Count == 0)
        {
            return 0;
        }

        var hechas = 0;

        // Agrupado por disparador: una consulta de reglas por tipo de evento y no una por evento. Cuando
        // no hay ninguna regla activa —el caso de casi todo el mundo— esto es una consulta por índice que
        // devuelve cero filas.
        foreach (var grupo in ocurrencias.GroupBy(o => o.Disparador))
        {
            var reglas = await repositorio.ActivasParaAsync(grupo.Key, ct).ConfigureAwait(false);
            if (reglas.Count == 0)
            {
                continue;
            }

            foreach (var ocurrencia in grupo)
            {
                var suyos = await HechosDeAsync(ocurrencia, ct).ConfigureAwait(false);
                if (suyos is null)
                {
                    continue;
                }

                foreach (var regla in reglas.Where(r => r.Aplica(grupo.Key, suyos)))
                {
                    if (await repositorio.YaActuoAsync(regla.Id, ocurrencia.SujetoId, ct).ConfigureAwait(false))
                    {
                        continue;
                    }

                    var hecho = await EjecutarAsync(regla, ocurrencia, empresaId, ct).ConfigureAwait(false);
                    if (hecho)
                    {
                        hechas++;
                    }
                }
            }
        }

        return hechas;
    }

    private async Task<bool> EjecutarAsync(Regla regla, Ocurrencia ocurrencia, Guid empresaId, CancellationToken ct)
    {
        // Sin contacto no hay nada que hacer: las cuatro acciones actúan sobre una persona.
        if (ocurrencia.ContactoId is not { } contactoId)
        {
            return false;
        }

        var hechas = new List<string>();

        foreach (var accion in regla.Acciones)
        {
            var hecho = accion.Tipo switch
            {
                TipoAccion.CrearTarea =>
                    await acciones.CrearTareaAsync(contactoId, accion.Texto!, accion.Numero ?? 0, ct).ConfigureAwait(false),

                TipoAccion.AsignarComercial =>
                    await acciones.AsignarAsync(contactoId, accion.Referencia!.Value, ct).ConfigureAwait(false),

                TipoAccion.MandarCorreo =>
                    await acciones.MandarCorreoAsync(contactoId, accion.Referencia!.Value, ct).ConfigureAwait(false),

                _ => await acciones.ApuntarNotaAsync(contactoId, accion.Texto!, ct).ConfigureAwait(false),
            };

            // Una acción que no se pudo hacer **no cancela las demás**. El caso real: una regla que manda
            // un correo y crea una tarea, sobre alguien que no ha dado su consentimiento. El correo no
            // sale —y es correcto que no salga— pero la tarea de llamarle sí tiene que crearse.
            hechas.Add(hecho ?? $"no se pudo {accion.Leer()}");
        }

        // Se apunta aunque todo haya fallado. Es lo que impide que la regla lo reintente para siempre
        // contra el mismo sujeto, y lo que deja escrito por qué no hizo nada.
        repositorio.AnadirEjecucion(Ejecucion.Crear(
            empresaId, regla.Id, ocurrencia.SujetoId, contactoId, string.Join("; ", hechas), reloj));

        regla.Disparada(reloj);
        return true;
    }

    private Task<Hechos?> HechosDeAsync(Ocurrencia ocurrencia, CancellationToken ct) =>
        ocurrencia.Disparador is Disparador.LeadCreado or Disparador.ContactoBaja
            ? hechos.DeContactoAsync(ocurrencia.SujetoId, ct)
            : hechos.DeOportunidadAsync(ocurrencia.SujetoId, ct);

    private static FichaRegla Ficha(Regla r) => new(
        r.Id, r.Nombre, Textos.De(r.Disparador), r.Leer(), r.Activa, r.Veces, r.CreadaEn, r.UltimaVezEn);
}
