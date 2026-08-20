using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Matchketing.Webhooks.Aplicacion;
using Matchketing.Webhooks.Dominio;
using Xunit;

namespace Matchketing.Webhooks.Tests;

public sealed class PruebasServicioWebhooks
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTimeOffset Inicio = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly RelojFijo reloj = new(Inicio);
    private readonly RepositorioEnMemoria repositorio = new(Empresa);
    private readonly EmisorDePrueba emisor = new();

    private ServicioWebhooks Servicio => new(repositorio, emisor, new ContextoDePrueba(Empresa), reloj);

    private async Task<SuscripcionWebhook> AltaAsync(params string[] eventos)
    {
        var r = await Servicio.CrearAsync(
            $"https://erp.ejemplo.es/hooks/{Guid.NewGuid()}", "Pedidos al ERP",
            eventos.Length == 0 ? ["oportunidad.ganada"] : eventos);

        r.Exito.Should().BeTrue(r.Fallido ? r.Error!.Codigo : null);
        return r.Valor.Suscripcion;
    }

    // ---------- Alta ----------

    [Fact]
    public async Task El_secreto_se_devuelve_al_crear_y_solo_entonces()
    {
        var r = await Servicio.CrearAsync("https://erp.ejemplo.es/h", "Pedidos", ["oportunidad.ganada"]);

        r.Valor.Secreto.Should().StartWith("whsec_");

        // Y en el listado ya no aparece: `FichaSuscripcion` no tiene campo para él. Devolverlo en cada
        // consulta sería regalarlo a cualquier sesión abierta en un portátil sin bloquear.
        var fichas = await Servicio.ListarAsync();
        JsonSerializer.Serialize(fichas).Should().NotContain(r.Valor.Secreto);
    }

    [Fact]
    public async Task Un_evento_que_no_existe_es_un_error_y_no_se_ignora()
    {
        var r = await Servicio.CrearAsync("https://erp.ejemplo.es/h", "Pedidos", ["oportunidad.regalada"]);

        // Aceptarlo en silencio dejaría una suscripción que no dispara nunca y a alguien mirando por
        // qué durante una tarde.
        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("webhook.evento_desconocido");
        r.Error.Mensaje.Should().Contain("oportunidad.regalada");
    }

    [Fact]
    public async Task No_se_repite_la_misma_direccion()
    {
        await Servicio.CrearAsync("https://erp.ejemplo.es/h", "Pedidos", ["oportunidad.ganada"]);

        var otra = await Servicio.CrearAsync("https://ERP.ejemplo.es/h", "Pedidos otra vez", ["oportunidad.ganada"]);

        // Sin distinguir mayúsculas: es la misma URL, y dos suscripciones iguales se pagan con
        // entregas duplicadas que nadie relaciona con esto.
        otra.Fallido.Should().BeTrue();
        otra.Error!.Codigo.Should().Be("webhook.repetido");
    }

    [Fact]
    public async Task Hay_un_techo_por_empresa()
    {
        for (var i = 0; i < ServicioWebhooks.MaximoPorEmpresa; i++)
        {
            (await Servicio.CrearAsync($"https://erp.ejemplo.es/h{i}", "Pedidos", ["oportunidad.ganada"]))
                .Exito.Should().BeTrue();
        }

        var pasada = await Servicio.CrearAsync("https://erp.ejemplo.es/uno-mas", "Pedidos", ["oportunidad.ganada"]);

        pasada.Fallido.Should().BeTrue();
        pasada.Error!.Codigo.Should().Be("webhook.demasiados");
    }

    // ---------- Encolar ----------

    [Fact]
    public async Task Sin_webhooks_encolar_no_hace_nada()
    {
        var cuantas = await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 42000 }));

        // Es el caso de casi todo el mundo, así que tiene que costar cero.
        cuantas.Should().Be(0);
        repositorio.Entregas.Should().BeEmpty();
    }

    [Fact]
    public async Task Encolar_reparte_solo_a_quien_escucha_ese_evento()
    {
        var gana = await AltaAsync("oportunidad.ganada");
        var pierde = await AltaAsync("oportunidad.perdida");
        var ambas = await AltaAsync("oportunidad.ganada", "oportunidad.perdida");

        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 42000 }));

        repositorio.Entregas.Select(e => e.SuscripcionId)
            .Should().BeEquivalentTo([gana.Id, ambas.Id])
            .And.NotContain(pierde.Id);
    }

    [Fact]
    public async Task A_una_suscripcion_apagada_no_se_le_encola()
    {
        var s = await AltaAsync("oportunidad.ganada");
        for (var i = 0; i < SuscripcionWebhook.FallosParaDesactivar; i++)
        {
            s.Fallada("500");
        }

        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 1 }));

        // Si se le encolara, al reactivarla le llegaría de golpe todo lo de los últimos días.
        repositorio.Entregas.Should().BeEmpty();
    }

    [Fact]
    public async Task Cada_entrega_lleva_su_identificador_dentro_del_cuerpo()
    {
        await AltaAsync("oportunidad.ganada");
        await AltaAsync("oportunidad.ganada");

        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 42000 }));

        repositorio.Entregas.Should().HaveCount(2);

        foreach (var entrega in repositorio.Entregas)
        {
            using var json = JsonDocument.Parse(entrega.Cuerpo);
            var raiz = json.RootElement;

            // El identificador del cuerpo y el de la fila son el mismo: es lo que permite que quien
            // recibe deduplique y que nosotros sepamos de qué entrega hablaba.
            raiz.GetProperty("id").GetGuid().Should().Be(entrega.Id);
            raiz.GetProperty("tipo").GetString().Should().Be("oportunidad.ganada");
            raiz.GetProperty("empresaId").GetGuid().Should().Be(Empresa);
            raiz.GetProperty("datos").GetProperty("importe").GetInt32().Should().Be(42000);
        }

        // Dos receptores independientes, dos identificadores: cada uno deduplica por su cuenta.
        repositorio.Entregas[0].Id.Should().NotBe(repositorio.Entregas[1].Id);
    }

    // ---------- Entregar ----------

    [Fact]
    public async Task Lo_que_sale_se_marca_y_limpia_los_fallos_de_la_suscripcion()
    {
        var s = await AltaAsync();
        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 1 }));

        var r = await Servicio.EntregarPendientesAsync();

        r.Should().Be(new ResumenEntregas(1, 0, 0, 0));
        repositorio.Entregas[0].Estado.Should().Be(EstadoEntrega.Entregada);
        s.UltimaEntregaEn.Should().Be(reloj.AhoraUtc);
    }

    [Fact]
    public async Task Un_fallo_pasajero_se_reintenta_y_no_apaga_nada()
    {
        var s = await AltaAsync();
        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 1 }));
        emisor.Contesta = _ => new ResultadoEntrega(false, 503, "el servidor contestó 503");

        var r = await Servicio.EntregarPendientesAsync();

        r.Should().Be(new ResumenEntregas(0, 1, 0, 0));
        s.Activa.Should().BeTrue();
        repositorio.Entregas[0].Estado.Should().Be(EstadoEntrega.Pendiente);
    }

    [Fact]
    public async Task Lo_que_no_le_toca_no_se_intenta()
    {
        await AltaAsync();
        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 1 }));
        emisor.Contesta = _ => new ResultadoEntrega(false, 503, "no disponible");
        await Servicio.EntregarPendientesAsync();

        // Ahora está aplazada un minuto. Una pasada inmediata no debe tocarla: si no, el escalado no
        // existiría y el otro lado recibiría siete intentos en un segundo.
        emisor.Intentos.Clear();
        var r = await Servicio.EntregarPendientesAsync();

        emisor.Intentos.Should().BeEmpty();
        r.Should().Be(new ResumenEntregas(0, 0, 0, 0));
    }

    [Fact]
    public async Task Cinco_entregas_agotadas_seguidas_apagan_el_webhook()
    {
        var s = await AltaAsync();
        emisor.Contesta = _ => new ResultadoEntrega(false, 500, "error del servidor");

        // Cinco eventos, cada uno hasta agotar sus intentos: es la forma en la que se apaga de verdad.
        for (var evento = 0; evento < SuscripcionWebhook.FallosParaDesactivar; evento++)
        {
            await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = evento }));

            for (var intento = 0; intento < Entrega.IntentosMaximos; intento++)
            {
                await Servicio.EntregarPendientesAsync();
                var pendiente = repositorio.Entregas.LastOrDefault(e => e.Estado == EstadoEntrega.Pendiente);
                if (pendiente?.ProximoIntentoEn is { } cuando)
                {
                    reloj.AhoraUtc = cuando;
                }
            }
        }

        s.Activa.Should().BeFalse();

        // El motivo tiene que servirle a quien lo lea en Ajustes: cuántas fallaron y qué dijo la
        // última. Sin eso, un webhook apagado es un misterio.
        s.MotivoApagado.Should().Contain(SuscripcionWebhook.FallosParaDesactivar.ToString(CultureInfo.InvariantCulture));
        s.MotivoApagado.Should().Contain("error del servidor");
    }

    [Fact]
    public async Task Si_la_suscripcion_se_borro_la_entrega_se_abandona_sin_intentarla()
    {
        var s = await AltaAsync();
        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 1 }));

        (await Servicio.BorrarAsync(s.Id)).Exito.Should().BeTrue();
        var r = await Servicio.EntregarPendientesAsync();

        // Mandar a una URL que alguien acaba de quitar de Ajustes es justo lo que no quería quien la
        // quitó, así que ni se intenta.
        emisor.Intentos.Should().BeEmpty();
        r.Agotadas.Should().Be(1);
    }

    [Fact]
    public async Task El_historial_dice_que_paso_en_cada_intento()
    {
        var s = await AltaAsync();
        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 1 }));
        emisor.Contesta = _ => new ResultadoEntrega(false, 502, "el servidor contestó 502 puerta de enlace incorrecta");
        await Servicio.EntregarPendientesAsync();

        var r = await Servicio.HistorialAsync(s.Id);

        r.Exito.Should().BeTrue();
        var entrega = r.Valor.Single();
        entrega.Evento.Should().Be("oportunidad.ganada");
        entrega.Estado.Should().Be("pendiente");
        entrega.Intentos.Should().Be(1);
        entrega.UltimoCodigo.Should().Be(502);

        // El texto del fallo es lo que se mira cuando algo no llega, así que tiene que decir algo.
        entrega.UltimoFallo.Should().Contain("502");
        entrega.ProximoIntentoEn.Should().NotBeNull();
    }

    [Fact]
    public async Task El_listado_cuenta_lo_que_esta_esperando_salir()
    {
        var s = await AltaAsync();
        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 1 }));
        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 2 }));

        var fichas = await Servicio.ListarAsync();

        // Es el número que hace ver de un vistazo que algo está atascado.
        fichas.Single(f => f.Id == s.Id).PendientesAhora.Should().Be(2);
    }

    [Fact]
    public async Task Rotar_el_secreto_afecta_a_lo_que_sale_desde_ese_momento()
    {
        var s = await AltaAsync();
        var antes = s.Secreto;

        var r = await Servicio.RotarSecretoAsync(s.Id);

        r.Exito.Should().BeTrue();
        r.Valor.Should().NotBe(antes);

        await Servicio.EncolarAsync(new Evento(TipoEvento.OportunidadGanada, new { importe = 1 }));
        await Servicio.EntregarPendientesAsync();

        // El emisor firma con lo que tenga la suscripción **en el momento del envío**, no con lo que
        // tuviera al encolar: si no, rotar el secreto no cortaría en seco y quedaría una ventana con
        // el secreto viejo todavía válido.
        emisor.Intentos.Single().Suscripcion.Secreto.Should().Be(r.Valor);
    }

    [Fact]
    public async Task De_otra_empresa_no_se_ve_nada()
    {
        await AltaAsync();

        var otra = new ServicioWebhooks(
            new RepositorioEnMemoria(Guid.NewGuid()), emisor, new ContextoDePrueba(Guid.NewGuid()), reloj);

        (await otra.ListarAsync()).Should().BeEmpty();
    }
}
