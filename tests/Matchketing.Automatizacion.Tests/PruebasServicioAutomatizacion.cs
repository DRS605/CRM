using FluentAssertions;
using Matchketing.Automatizacion.Aplicacion;
using Matchketing.Automatizacion.Dominio;
using Matchketing.Nucleo.Comun;
using Xunit;

namespace Matchketing.Automatizacion.Tests;

public sealed class ContextoDePrueba(Guid? empresaId) : IContextoEmpresa
{
    public Guid? EmpresaId { get; } = empresaId;

    public Guid? UsuarioId { get; } = Guid.NewGuid();

    public IReadOnlyCollection<string> Permisos => [];

    public bool Tiene(string permiso) => true;
}

public sealed class RepositorioEnMemoria : IRepositorioReglas
{
    public List<Regla> Reglas { get; } = [];

    public List<Ejecucion> Ejecuciones { get; } = [];

    public Task<Regla?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Reglas.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<Regla>> DeLaEmpresaAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Regla>>(Reglas.ToList());

    public Task<IReadOnlyList<Regla>> ActivasParaAsync(Disparador disparador, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Regla>>(Reglas.Where(r => r.Activa && r.Disparador == disparador).ToList());

    public void Anadir(Regla regla) => Reglas.Add(regla);

    public void Quitar(Regla regla) => Reglas.Remove(regla);

    public Task<bool> YaActuoAsync(Guid reglaId, Guid sujetoId, CancellationToken ct = default) =>
        Task.FromResult(Ejecuciones.Any(e => e.ReglaId == reglaId && e.SujetoId == sujetoId));

    public Task<IReadOnlyList<Ejecucion>> UltimasDeAsync(Guid reglaId, int cuantas, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Ejecucion>>(Ejecuciones
            .Where(e => e.ReglaId == reglaId).OrderByDescending(e => e.CuandoEn).Take(cuantas).ToList());

    public void AnadirEjecucion(Ejecucion ejecucion) => Ejecuciones.Add(ejecucion);
}

public sealed class HechosDePrueba : IConsultaHechos
{
    public Hechos? DeContacto { get; set; } = new("Valencia", "feria", "Hostelería", null, null);

    public Hechos? DeOportunidad { get; set; } = new("Valencia", "feria", "Hostelería", 18400m, null);

    public Task<Hechos?> DeContactoAsync(Guid contactoId, CancellationToken ct = default) => Task.FromResult(DeContacto);

    public Task<Hechos?> DeOportunidadAsync(Guid oportunidadId, CancellationToken ct = default) => Task.FromResult(DeOportunidad);
}

public sealed class AccionesDePrueba : IAccionesAutomatizacion
{
    public List<string> Hechas { get; } = [];

    /// <summary>Qué acciones fallan. Devolver nulo es un caso normal, no un error.</summary>
    public HashSet<TipoAccion> Fallan { get; } = [];

    public Task<string?> CrearTareaAsync(Guid contactoId, string titulo, int dias, CancellationToken ct = default) =>
        Apuntar(TipoAccion.CrearTarea, $"tarea:{titulo}:{dias}");

    public Task<string?> AsignarAsync(Guid contactoId, Guid usuarioId, CancellationToken ct = default) =>
        Apuntar(TipoAccion.AsignarComercial, $"asignar:{usuarioId}");

    public Task<string?> MandarCorreoAsync(Guid contactoId, Guid plantillaId, CancellationToken ct = default) =>
        Apuntar(TipoAccion.MandarCorreo, $"correo:{plantillaId}");

    public Task<string?> ApuntarNotaAsync(Guid contactoId, string texto, CancellationToken ct = default) =>
        Apuntar(TipoAccion.ApuntarNota, $"nota:{texto}");

    private Task<string?> Apuntar(TipoAccion tipo, string que)
    {
        if (Fallan.Contains(tipo))
        {
            return Task.FromResult<string?>(null);
        }

        Hechas.Add(que);
        return Task.FromResult<string?>(que);
    }
}

public sealed class PruebasServicioAutomatizacion
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Contacto = Guid.NewGuid();
    private static readonly Guid Oportunidad = Guid.NewGuid();

    private readonly RelojFijo reloj = new(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly RepositorioEnMemoria repositorio = new();
    private readonly HechosDePrueba hechos = new();
    private readonly AccionesDePrueba acciones = new();

    private ServicioAutomatizacion Servicio => new(
        repositorio, hechos, acciones, new ContextoDePrueba(Empresa), reloj);

    private async Task<Regla> ReglaAsync(
        Disparador disparador = Disparador.LeadCreado,
        IReadOnlyCollection<Condicion>? condiciones = null,
        IReadOnlyCollection<Accion>? acciones = null,
        bool encendida = true)
    {
        var r = await Servicio.CrearAsync(
            $"Regla {Guid.NewGuid():N}", disparador,
            condiciones ?? [], acciones ?? [Accion.Tarea("Llamar", 0)]);

        r.Exito.Should().BeTrue(r.Fallido ? r.Error!.Codigo : null);
        if (encendida) { r.Valor.Encender(); }
        return r.Valor;
    }

    // ---------- Gestión ----------

    [Fact]
    public async Task Hay_un_techo_de_reglas()
    {
        for (var i = 0; i < ServicioAutomatizacion.MaximoPorEmpresa; i++)
        {
            await ReglaAsync();
        }

        // Pasada esa cifra nadie sabe ya por qué el CRM hace lo que hace.
        (await Servicio.CrearAsync("Una más", Disparador.LeadCreado, [], [Accion.Nota("x")]))
            .Error!.Codigo.Should().Be("regla.demasiadas");
    }

    [Fact]
    public async Task El_listado_pone_las_encendidas_primero()
    {
        var apagada = await ReglaAsync(encendida: false);
        var encendida = await ReglaAsync();

        var lista = await Servicio.ListarAsync();

        // Son las que están haciendo algo ahora mismo, y son las que hay que ver cuando el CRM hace algo
        // que no esperabas.
        lista[0].Id.Should().Be(encendida.Id);
        lista[1].Id.Should().Be(apagada.Id);
        lista[0].Leida.Should().StartWith("Si pasa");
    }

    // ---------- Disparo ----------

    [Fact]
    public async Task Una_regla_apagada_no_hace_nada()
    {
        await ReglaAsync(encendida: false);

        var hechas = await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, Contacto, Contacto)]);

        hechas.Should().Be(0);
        acciones.Hechas.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_reglas_no_se_consulta_nada_ni_se_hace_nada()
    {
        // Es el caso de casi todo el mundo, así que tiene que costar cero.
        (await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, Contacto, Contacto)])).Should().Be(0);
        repositorio.Ejecuciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Una_regla_que_cumple_hace_sus_acciones_y_lo_apunta()
    {
        var regla = await ReglaAsync(
            condiciones: [new Condicion(Campo.Provincia, Operador.Es, "Valencia")],
            acciones: [Accion.Tarea("Llamar", 0), Accion.Nota("Lead de feria")]);

        await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, Contacto, Contacto)]);

        acciones.Hechas.Should().Equal("tarea:Llamar:0", "nota:Lead de feria");
        regla.Veces.Should().Be(1);

        // El registro dice qué hizo, en castellano: es lo que permite auditar una automatización.
        var ejecucion = repositorio.Ejecuciones.Single();
        ejecucion.ReglaId.Should().Be(regla.Id);
        ejecucion.SujetoId.Should().Be(Contacto);
        ejecucion.QueHizo.Should().Contain("tarea:Llamar").And.Contain("nota:");
    }

    [Fact]
    public async Task Si_no_cumple_las_condiciones_no_pasa_nada()
    {
        await ReglaAsync(condiciones: [new Condicion(Campo.Provincia, Operador.Es, "Alicante")]);

        await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, Contacto, Contacto)]);

        acciones.Hechas.Should().BeEmpty();
        repositorio.Ejecuciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Actua_una_sola_vez_por_sujeto()
    {
        await ReglaAsync();

        await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, Contacto, Contacto)]);
        await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, Contacto, Contacto)]);

        // Sin esto, un evento que se reprocese crearía la tarea dos veces o mandaría el correo dos veces,
        // y eso no se puede deshacer.
        acciones.Hechas.Should().HaveCount(1);
        repositorio.Ejecuciones.Should().HaveCount(1);
    }

    [Fact]
    public async Task Pero_sobre_otro_sujeto_si_actua()
    {
        await ReglaAsync();
        var otro = Guid.NewGuid();

        await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, Contacto, Contacto)]);
        await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, otro, otro)]);

        acciones.Hechas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Una_accion_que_no_se_puede_hacer_no_cancela_las_demas()
    {
        await ReglaAsync(acciones: [Accion.Correo(Guid.NewGuid()), Accion.Tarea("Llamar", 0)]);

        // El caso real: una regla que manda un correo y crea una tarea, sobre alguien que no ha dado su
        // consentimiento. El correo no sale —y es correcto que no salga— pero la tarea de llamarle sí
        // tiene que crearse: es justo entonces cuando más hay que llamar.
        acciones.Fallan.Add(TipoAccion.MandarCorreo);

        await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, Contacto, Contacto)]);

        acciones.Hechas.Should().Equal("tarea:Llamar:0");

        // Y queda escrito lo que no se pudo hacer, para que se pueda averiguar por qué.
        repositorio.Ejecuciones.Single().QueHizo.Should().Contain("no se pudo");
    }

    [Fact]
    public async Task Si_todo_falla_tambien_se_apunta_para_no_reintentar_para_siempre()
    {
        await ReglaAsync(acciones: [Accion.Nota("x")]);
        acciones.Fallan.Add(TipoAccion.ApuntarNota);

        await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, Contacto, Contacto)]);

        repositorio.Ejecuciones.Should().HaveCount(1);
        repositorio.Ejecuciones.Single().QueHizo.Should().StartWith("no se pudo");
    }

    [Fact]
    public async Task Una_ocurrencia_sin_contacto_no_hace_nada()
    {
        await ReglaAsync(Disparador.OportunidadGanada);

        // Las cuatro acciones actúan sobre una persona. Sin contacto no hay nada que hacer y no se apunta
        // una ejecución vacía.
        await Servicio.DispararAsync([new Ocurrencia(Disparador.OportunidadGanada, Oportunidad, null)]);

        acciones.Hechas.Should().BeEmpty();
        repositorio.Ejecuciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Los_disparadores_de_oportunidad_miran_los_hechos_de_la_oportunidad()
    {
        await ReglaAsync(
            Disparador.OportunidadGanada,
            condiciones: [new Condicion(Campo.Importe, Operador.MayorQue, "10000")],
            acciones: [Accion.Tarea("Pedir referencia", 30)]);

        await Servicio.DispararAsync([new Ocurrencia(Disparador.OportunidadGanada, Oportunidad, Contacto)]);

        acciones.Hechas.Should().Equal("tarea:Pedir referencia:30");
    }

    // ---------- Ensayo ----------

    [Fact]
    public async Task El_ensayo_dice_que_haria_sin_hacerlo()
    {
        var regla = await ReglaAsync(
            condiciones: [new Condicion(Campo.Provincia, Operador.Es, "Valencia")],
            acciones: [Accion.Tarea("Llamar", 0)]);

        var r = await Servicio.EnsayarAsync(regla.Id, Contacto);

        r.Valor.Aplicaria.Should().BeTrue();
        r.Valor.Haria.Should().Equal("crear la tarea «Llamar» para hoy");

        // Y no ha hecho nada. Una regla no se puede probar de otra forma: lo que hace es irreversible, y
        // encenderla «para ver qué pasa» es exactamente lo que no se debe hacer.
        acciones.Hechas.Should().BeEmpty();
        repositorio.Ejecuciones.Should().BeEmpty();
    }

    [Fact]
    public async Task El_ensayo_dice_cual_es_la_condicion_que_no_cumple()
    {
        var regla = await ReglaAsync(condiciones: [new Condicion(Campo.Provincia, Operador.Es, "Alicante")]);

        var r = await Servicio.EnsayarAsync(regla.Id, Contacto);

        r.Valor.Aplicaria.Should().BeFalse();
        r.Valor.PorQueNo.Should().Contain("provincia es «Alicante»");
    }

    [Fact]
    public async Task El_ensayo_avisa_de_que_ya_actuo_sobre_ese_contacto()
    {
        var regla = await ReglaAsync();
        await Servicio.DispararAsync([new Ocurrencia(Disparador.LeadCreado, Contacto, Contacto)]);

        var r = await Servicio.EnsayarAsync(regla.Id, Contacto);

        // Es el motivo más común de que alguien crea que su regla no funciona.
        r.Valor.Aplicaria.Should().BeFalse();
        r.Valor.PorQueNo.Should().Contain("una sola vez");
    }

    [Fact]
    public async Task El_ensayo_funciona_con_la_regla_apagada()
    {
        var regla = await ReglaAsync(encendida: false);

        // La pregunta es «¿cumpliría?», no «¿está funcionando?». Si hiciera falta encenderla para
        // probarla, el ensayo no serviría para nada.
        (await Servicio.EnsayarAsync(regla.Id, Contacto)).Valor.Aplicaria.Should().BeTrue();
    }
}
