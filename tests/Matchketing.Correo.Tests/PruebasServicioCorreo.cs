using FluentAssertions;
using Matchketing.Correo.Aplicacion;
using Matchketing.Correo.Dominio;
using Matchketing.Nucleo.Resultados;
using Xunit;

namespace Matchketing.Correo.Tests;

public sealed class PruebasServicioCorreo
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Contacto = Guid.NewGuid();
    private static readonly DateTimeOffset Inicio = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly RelojFijo reloj = new(Inicio);
    private readonly RepositorioEnMemoria repositorio = new();
    private readonly PermisoDePrueba permiso = new();
    private readonly DatosDePrueba datos = new();
    private readonly EmisorDePrueba emisor = new();
    private readonly CronologiaDePrueba cronologia = new();

    private ServicioCorreo Servicio => new(
        repositorio, permiso, datos, emisor, cronologia, new ContextoDePrueba(Empresa, Usuario), reloj);

    private async Task<Plantilla> PlantillaAsync(ParaQue paraQue = ParaQue.AtenderSolicitud)
    {
        var r = await Servicio.CrearPlantillaAsync(
            $"Seguimiento {Guid.NewGuid():N}", "Sobre {{cuenta}}", "Hola {{nombre}}.", paraQue);

        r.Exito.Should().BeTrue(r.Fallido ? r.Error!.Codigo : null);
        return r.Valor;
    }

    // ---------- Plantillas ----------

    [Fact]
    public async Task Las_plantillas_se_ordenan_por_uso_y_no_por_fecha()
    {
        var poco = await PlantillaAsync();
        var mucho = await PlantillaAsync();
        mucho.Usada();
        mucho.Usada();

        var lista = await Servicio.PlantillasAsync();

        // En una lista de cuarenta, la que se usa todos los días tiene que estar arriba: es la
        // diferencia entre encontrarla y buscarla.
        lista[0].Id.Should().Be(mucho.Id);
        lista[1].Id.Should().Be(poco.Id);
    }

    [Fact]
    public async Task Hay_un_techo_de_plantillas()
    {
        for (var i = 0; i < ServicioCorreo.MaximoPlantillas; i++)
        {
            await PlantillaAsync();
        }

        var pasada = await Servicio.CrearPlantillaAsync("Una más", "Hola", "Qué tal", ParaQue.Comercial);

        pasada.Error!.Codigo.Should().Be("plantilla.demasiadas");
    }

    [Fact]
    public async Task Borrar_una_plantilla_no_borra_los_correos_ya_mandados()
    {
        var plantilla = await PlantillaAsync();
        await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);

        (await Servicio.BorrarPlantillaAsync(plantilla.Id)).Exito.Should().BeTrue();

        // Cada correo guarda su propio texto. Es la mitad del valor de guardarlos: poder ver qué se le
        // mandó exactamente, sin depender de que la plantilla siga existiendo.
        var historial = await Servicio.DeContactoAsync(Contacto);
        historial.Should().HaveCount(1);
        historial[0].Cuerpo.Should().Be("Hola Manolo.");
    }

    // ---------- Borrador ----------

    [Fact]
    public async Task El_borrador_ensena_el_texto_ya_relleno_y_sin_enviar_nada()
    {
        var plantilla = await PlantillaAsync();

        var r = await Servicio.PrepararAsync(Contacto, plantilla.Id);

        r.Exito.Should().BeTrue();
        r.Valor.Asunto.Should().Be("Sobre Bar Casa Manolo");
        r.Valor.Cuerpo.Should().Be("Hola Manolo.");
        r.Valor.Para.Should().Be("manolo@casamanolo.es");
        r.Valor.SePuede.Should().BeTrue();

        // Y no ha mandado ni encolado nada: un correo es irreversible y hay que verlo antes.
        repositorio.Mensajes.Should().BeEmpty();
        emisor.Intentos.Should().BeEmpty();
    }

    [Fact]
    public async Task El_borrador_dice_por_que_no_se_puede_enviar()
    {
        var plantilla = await PlantillaAsync(ParaQue.Comercial);
        permiso.Niega = Error.Prohibido("cumplimiento.de_baja", "El contacto pidió no recibir más comunicaciones.");

        var r = await Servicio.PrepararAsync(Contacto, plantilla.Id);

        // Se enseña el texto igual, pero con el motivo: quien lo lea tiene que entender qué falta, no
        // encontrarse un botón gris.
        r.Valor.SePuede.Should().BeFalse();
        r.Valor.PorQueNo.Should().Contain("no recibir");
        r.Valor.Cuerpo.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Un_contacto_sin_correo_lo_dice_en_el_borrador()
    {
        var plantilla = await PlantillaAsync();
        datos.Datos = datos.Datos! with { Correo = null };

        var r = await Servicio.PrepararAsync(Contacto, plantilla.Id);

        r.Valor.SePuede.Should().BeFalse();
        r.Valor.PorQueNo.Should().Contain("no tiene correo");
    }

    // ---------- Enviar ----------

    [Fact]
    public async Task Enviar_encola_y_lo_apunta_en_la_cronologia()
    {
        var plantilla = await PlantillaAsync();

        var r = await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);

        r.Exito.Should().BeTrue();
        r.Valor.Estado.Should().Be(EstadoCorreo.Encolado);

        // Se apunta al **encolar**, no al enviar: si no, el comercial vería su ficha sin rastro del
        // correo que acaba de mandar y volvería a mandarlo.
        cronologia.Correos.Should().ContainSingle().Which.Should().Contain("Sobre Bar Casa Manolo");
        plantilla.Usos.Should().Be(1);
    }

    [Fact]
    public async Task Sin_permiso_no_se_encola_nada()
    {
        var plantilla = await PlantillaAsync(ParaQue.Comercial);
        permiso.Niega = Error.Prohibido("cumplimiento.sin_base_legal", "No hay base legal vigente.");

        var r = await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("cumplimiento.sin_base_legal");

        // Ni fila, ni apunte en la cronología: no ha pasado nada.
        repositorio.Mensajes.Should().BeEmpty();
        cronologia.Correos.Should().BeEmpty();
    }

    [Fact]
    public async Task El_para_que_lo_manda_la_plantilla_y_no_el_cliente()
    {
        var plantilla = await PlantillaAsync(ParaQue.Comercial);

        // El cliente dice «atender una solicitud», que es el permiso más fácil de tener. Si se le
        // hiciera caso, bastaría con mentir aquí para saltarse el consentimiento comercial.
        await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);

        permiso.Preguntas.Should().OnlyContain(p => p.ParaQue == ParaQue.Comercial);
        repositorio.Mensajes.Single().ParaQue.Should().Be(ParaQue.Comercial);
    }

    [Fact]
    public async Task El_texto_de_la_plantilla_manda_sobre_lo_que_llegue_por_parametro()
    {
        var plantilla = await PlantillaAsync();

        await Servicio.EnviarAsync(Contacto, plantilla.Id, "Otro asunto", "Otro cuerpo", ParaQue.AtenderSolicitud);

        // La pantalla enseña el borrador y luego envía. Si el parámetro pudiera cambiar el texto, lo que
        // se ve y lo que sale podrían separarse sin que nadie lo note.
        var correo = repositorio.Mensajes.Single();
        correo.Asunto.Should().Be("Sobre Bar Casa Manolo");
        correo.Cuerpo.Should().Be("Hola Manolo.");
    }

    [Fact]
    public async Task Sin_plantilla_se_puede_escribir_a_mano()
    {
        var r = await Servicio.EnviarAsync(Contacto, null, "Te confirmo la visita", "Mañana a las 10.", ParaQue.AtenderSolicitud);

        r.Exito.Should().BeTrue();
        r.Valor.PlantillaId.Should().BeNull();
        r.Valor.Asunto.Should().Be("Te confirmo la visita");
    }

    // ---------- Enviar de verdad ----------

    [Fact]
    public async Task Lo_que_sale_queda_enviado()
    {
        var plantilla = await PlantillaAsync();
        await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);

        var r = await Servicio.EnviarPendientesAsync("https://mk.ejemplo.es");

        r.Should().Be(new ResumenEnvios(1, 0, 0, 0));
        repositorio.Mensajes.Single().Estado.Should().Be(EstadoCorreo.Enviado);
    }

    [Fact]
    public async Task El_permiso_se_vuelve_a_comprobar_justo_antes_de_salir()
    {
        var plantilla = await PlantillaAsync(ParaQue.Comercial);
        await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);

        // **Esta es la prueba que justifica el estado `Cancelado`.** Entre encolar y enviar pasan
        // minutos, y en esos minutos la persona se ha dado de baja. Un webhook que sale tarde no molesta
        // a nadie; un correo comercial a quien acaba de pedir que no le escriban es una infracción.
        permiso.Niega = Error.Prohibido("cumplimiento.de_baja", "El contacto pidió no recibir más comunicaciones.");

        var r = await Servicio.EnviarPendientesAsync(null);

        r.Cancelados.Should().Be(1);
        emisor.Intentos.Should().BeEmpty("no se llega a hablar con el servidor de correo");
        repositorio.Mensajes.Single().Estado.Should().Be(EstadoCorreo.Cancelado);
    }

    [Fact]
    public async Task Un_fallo_pasajero_se_reintenta_y_uno_definitivo_no()
    {
        var plantilla = await PlantillaAsync();
        await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);
        emisor.Contesta = _ => new ResultadoEnvioCorreo(false, "no contesta", false);

        (await Servicio.EnviarPendientesAsync(null)).Reintentar.Should().Be(1);

        reloj.AhoraUtc = repositorio.Mensajes.Single().ProximoIntentoEn!.Value;
        emisor.Contesta = _ => new ResultadoEnvioCorreo(false, "buzón inexistente", true);

        (await Servicio.EnviarPendientesAsync(null)).Fallidos.Should().Be(1);
        repositorio.Mensajes.Single().Estado.Should().Be(EstadoCorreo.Fallido);
    }

    [Fact]
    public async Task Con_seguimiento_apagado_el_correo_va_sin_pixel()
    {
        var plantilla = await PlantillaAsync();
        await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);

        await Servicio.EnviarPendientesAsync(null);

        // Y sin píxel el correo sale **solo en texto plano**: sin parte HTML y sin nada que cargar. Es
        // la diferencia entre medir el comportamiento de alguien y no medirlo.
        emisor.Intentos.Single().UrlPixel.Should().BeNull();
    }

    [Fact]
    public async Task Con_seguimiento_encendido_la_url_del_pixel_lleva_el_token()
    {
        var plantilla = await PlantillaAsync();
        await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);

        await Servicio.EnviarPendientesAsync("https://mk.ejemplo.es/");

        var correo = repositorio.Mensajes.Single();
        emisor.Intentos.Single().UrlPixel.Should().Be($"https://mk.ejemplo.es/e/{correo.TokenApertura}.gif");
    }

    // ---------- Aperturas ----------

    [Fact]
    public async Task La_primera_apertura_se_apunta_en_la_cronologia()
    {
        var plantilla = await PlantillaAsync();
        await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);
        await Servicio.EnviarPendientesAsync("https://mk.ejemplo.es");
        var token = repositorio.Mensajes.Single().TokenApertura;

        (await Servicio.AnotarAperturaAsync(token)).Should().BeTrue();
        (await Servicio.AnotarAperturaAsync(token)).Should().BeFalse();

        cronologia.Aperturas.Should().ContainSingle().Which.Should().Contain("Ha abierto");
        repositorio.Mensajes.Single().Aperturas.Should().Be(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("inventado")]
    public async Task Un_token_que_no_existe_no_apunta_nada(string? token)
    {
        (await Servicio.AnotarAperturaAsync(token)).Should().BeFalse();
        cronologia.Aperturas.Should().BeEmpty();
    }

    [Fact]
    public async Task El_historial_de_un_contacto_trae_el_texto_y_las_aperturas()
    {
        var plantilla = await PlantillaAsync();
        await Servicio.EnviarAsync(Contacto, plantilla.Id, null, null, ParaQue.AtenderSolicitud);
        await Servicio.EnviarPendientesAsync("https://mk.ejemplo.es");
        await Servicio.AnotarAperturaAsync(repositorio.Mensajes.Single().TokenApertura);

        var historial = await Servicio.DeContactoAsync(Contacto);

        var correo = historial.Single();
        correo.Estado.Should().Be("enviado");
        correo.Cuerpo.Should().Be("Hola Manolo.");
        correo.Aperturas.Should().Be(1);
        correo.PrimeraAperturaEn.Should().NotBeNull();
    }
}
