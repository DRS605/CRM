using FluentAssertions;
using Matchketing.Avisos.Aplicacion;
using Matchketing.Avisos.Dominio;
using Xunit;

namespace Matchketing.Avisos.Tests;

public sealed class PruebasServicioAvisos
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/aparato-1";
    private const string Publica = "BM6oFunqnW-q5Rz-laNO3Mao2nF9eQ7cLPaW6ltwuhLqSdgz0awOs05RnQPmw-Koucpiqg71PjrZVmLkxjujuuU";
    private const string Secreto = "v96B8cq6_hyHop4iU0iZKg";

    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Marta = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Pau = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly SuscripcionesEnMemoria suscripciones = new();
    private readonly PendientesDePrueba pendientes = new();
    private readonly EmisorDePrueba emisor = new();

    /// <summary>Un viernes a las 18:00 en hora local (16:00 UTC en verano).</summary>
    private readonly RelojFijo reloj = new(new DateTimeOffset(2026, 8, 21, 16, 0, 0, TimeSpan.Zero));

    private ServicioAvisos Servicio(Guid? usuario = null) =>
        new(suscripciones, pendientes, emisor, new ContextoDePrueba(Empresa, usuario ?? Marta), reloj);

    private async Task<SuscripcionAviso> DarDeAltaAsync(Guid usuario, string endpoint)
    {
        var r = await Servicio(usuario).SuscribirAsync(endpoint, Publica, Secreto);
        r.Exito.Should().BeTrue(r.Error?.Mensaje);
        return r.Valor;
    }

    // ---- Suscribirse ---------------------------------------------------------------------

    [Fact]
    public async Task Suscribirse_guarda_el_aparato()
    {
        await DarDeAltaAsync(Marta, Endpoint);

        suscripciones.Todas.Should().ContainSingle()
            .Which.Should().Match<SuscripcionAviso>(s => s.UsuarioId == Marta && s.Endpoint == Endpoint);
    }

    [Fact]
    public async Task El_navegador_puede_reenviar_la_misma_suscripcion_sin_duplicarla()
    {
        // Lo hace cada vez que se abre la aplicación. Sin esto, al mes hay diez filas del mismo móvil y
        // le llegan diez avisos.
        await DarDeAltaAsync(Marta, Endpoint);
        await DarDeAltaAsync(Marta, Endpoint);

        suscripciones.Todas.Should().HaveCount(1);
    }

    [Fact]
    public async Task Las_claves_rotadas_se_actualizan_en_su_sitio()
    {
        var suscripcion = await DarDeAltaAsync(Marta, Endpoint);
        var otraPublica = "BJuLOs50oycgCnV_RdJiRpI2W2lyGje_iPvSMZ5mtAp__gUIA9YvpcvKdfSrFg5vPDgCzWw0qU8u-uLNxOMKVh0";

        // El secreto tiene que medir 16 bytes exactos, o sea 22 caracteres en base64url. Escribiendo
        // este test puse uno de 17 y la renovación falló en silencio, porque no comprobaba el resultado.
        var r = await Servicio().SuscribirAsync(Endpoint, otraPublica, "AAAAAAAAAAAAAAAAAAAAAA");

        r.Exito.Should().BeTrue(r.Error?.Mensaje);
        suscripciones.Todas.Should().HaveCount(1);
        suscripcion.ClavePublica.Should().Be(otraPublica);
    }

    [Theory]
    [InlineData(null, "suscripcion.endpoint_invalido")]
    [InlineData("no-es-una-url", "suscripcion.endpoint_invalido")]
    [InlineData("http://fcm.googleapis.com/x", "suscripcion.endpoint_invalido")]
    public async Task Un_endpoint_que_no_es_https_se_rechaza(string? endpoint, string codigo)
    {
        (await Servicio().SuscribirAsync(endpoint, Publica, Secreto)).Error!.Codigo.Should().Be(codigo);
    }

    [Fact]
    public async Task Las_claves_se_validan_al_suscribirse_y_no_al_mandar_el_aviso()
    {
        // Si no, una suscripción rota se guardaría bien y fallaría en silencio el viernes por la tarde,
        // que es justo cuando nadie va a mirar los registros.
        (await Servicio().SuscribirAsync(Endpoint, "QQ", Secreto)).Error!.Codigo.Should().Be("suscripcion.p256dh_invalida");
        (await Servicio().SuscribirAsync(Endpoint, Publica, "corto")).Error!.Codigo.Should().Be("suscripcion.auth_invalida");
        suscripciones.Todas.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_empresa_activa_no_se_puede_suscribir()
    {
        var sinEmpresa = new ServicioAvisos(suscripciones, pendientes, emisor, new ContextoDePrueba(null, Marta), reloj);

        (await sinEmpresa.SuscribirAsync(Endpoint, Publica, Secreto)).Error!.Codigo.Should().Be("empresa.sin_seleccionar");
    }

    [Fact]
    public async Task Apagar_los_avisos_funciona_aunque_el_aparato_no_estuviera_dado_de_alta()
    {
        // Quien dice «no quiero avisos» no puede recibir un error por respuesta.
        (await Servicio().DesuscribirAsync("https://fcm.googleapis.com/fcm/send/no-existe")).Exito.Should().BeTrue();
        (await Servicio().DesuscribirAsync(null)).Exito.Should().BeTrue();
    }

    [Fact]
    public async Task Cada_uno_ve_solo_sus_aparatos()
    {
        await DarDeAltaAsync(Marta, Endpoint);
        await DarDeAltaAsync(Pau, "https://fcm.googleapis.com/fcm/send/aparato-2");

        (await Servicio(Marta).MisAparatosAsync()).Should().ContainSingle().Which.UsuarioId.Should().Be(Marta);
    }

    // ---- El aviso del viernes -----------------------------------------------------------

    [Fact]
    public async Task Avisa_a_quien_tiene_decisiones_pendientes()
    {
        await DarDeAltaAsync(Marta, Endpoint);
        pendientes.Cuantas[Marta] = 11;

        var resumen = await Servicio().AvisarDelRepasoAsync();

        resumen.Enviados.Should().Be(1);
        emisor.Enviados.Should().ContainSingle();
        emisor.Enviados[0].Aviso.Titulo.Should().Be("Cierra la semana");
        emisor.Enviados[0].Aviso.Ruta.Should().Be("/?ir=repaso");
    }

    [Fact]
    public async Task El_texto_dice_cuantas_son_y_cuanto_cuesta()
    {
        // Son las dos cosas que deciden si alguien lo abre ahora o lo desliza. «Tienes tareas
        // pendientes» no dice ninguna de las dos.
        await DarDeAltaAsync(Marta, Endpoint);
        pendientes.Cuantas[Marta] = 11;

        await Servicio().AvisarDelRepasoAsync();

        emisor.Enviados[0].Aviso.Cuerpo.Should().Be("11 decisiones te separan de tenerlo al día. Un minuto.");
        emisor.Enviados[0].Aviso.Cuantas.Should().Be(11);
    }

    [Fact]
    public async Task Si_no_hay_nada_pendiente_no_se_manda_nada()
    {
        // Un aviso que dice «no tienes nada» es un aviso que enseña a ignorar los avisos.
        await DarDeAltaAsync(Marta, Endpoint);

        (await Servicio().AvisarDelRepasoAsync()).Enviados.Should().Be(0);
        emisor.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Por_una_o_dos_decisiones_no_se_molesta_a_nadie()
    {
        await DarDeAltaAsync(Marta, Endpoint);
        pendientes.Cuantas[Marta] = ServicioAvisos.MinimoParaAvisar - 1;

        (await Servicio().AvisarDelRepasoAsync()).Enviados.Should().Be(0);
    }

    [Fact]
    public async Task No_llegan_dos_avisos_si_el_trabajo_corre_dos_veces()
    {
        // Dos instancias, un reintento, un despliegue a mitad: el control es el último aviso mandado, no
        // el calendario.
        await DarDeAltaAsync(Marta, Endpoint);
        pendientes.Cuantas[Marta] = 11;
        var servicio = Servicio();

        (await servicio.AvisarDelRepasoAsync()).Enviados.Should().Be(1);
        (await servicio.AvisarDelRepasoAsync()).Enviados.Should().Be(0);

        emisor.Enviados.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_la_semana_siguiente_vuelve_a_avisar()
    {
        await DarDeAltaAsync(Marta, Endpoint);
        pendientes.Cuantas[Marta] = 11;
        var servicio = Servicio();
        await servicio.AvisarDelRepasoAsync();

        reloj.AhoraUtc = reloj.AhoraUtc.AddDays(7);

        (await servicio.AvisarDelRepasoAsync()).Enviados.Should().Be(1);
    }

    [Fact]
    public async Task Una_suscripcion_muerta_se_borra_en_vez_de_reintentarse()
    {
        // Insistir contra endpoints caducados es lo que hace que un servicio de push empiece a limitar
        // todo lo que mandamos.
        await DarDeAltaAsync(Marta, Endpoint);
        pendientes.Cuantas[Marta] = 11;
        emisor.Respuestas[Endpoint] = ResultadoEnvio.SuscripcionMuerta;

        var resumen = await Servicio().AvisarDelRepasoAsync();

        resumen.Borrados.Should().Be(1);
        resumen.Enviados.Should().Be(0);
        suscripciones.Todas.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_fallo_pasajero_deja_la_suscripcion_y_no_marca_el_aviso_como_dado()
    {
        await DarDeAltaAsync(Marta, Endpoint);
        pendientes.Cuantas[Marta] = 11;
        emisor.Respuestas[Endpoint] = ResultadoEnvio.FalloPasajero;

        var resumen = await Servicio().AvisarDelRepasoAsync();

        resumen.Fallidos.Should().Be(1);
        suscripciones.Todas.Should().HaveCount(1);

        // Y en la siguiente pasada se vuelve a intentar: si se hubiera marcado como avisado, la persona
        // se quedaría sin aviso esa semana por un 500 pasajero de Google.
        emisor.Respuestas.Remove(Endpoint);
        (await Servicio().AvisarDelRepasoAsync()).Enviados.Should().Be(1);
    }

    [Fact]
    public async Task A_quien_tiene_dos_aparatos_le_llega_a_los_dos()
    {
        // Está en el coche con el móvil y luego abre el portátil: el aviso tiene que estar donde esté.
        await DarDeAltaAsync(Marta, Endpoint);
        await DarDeAltaAsync(Marta, "https://updates.push.services.mozilla.com/wpush/v2/portatil");
        pendientes.Cuantas[Marta] = 11;

        (await Servicio().AvisarDelRepasoAsync()).Enviados.Should().Be(2);
    }

    [Fact]
    public async Task Solo_avisa_a_quien_le_toca()
    {
        await DarDeAltaAsync(Marta, Endpoint);
        await DarDeAltaAsync(Pau, "https://fcm.googleapis.com/fcm/send/pau");
        pendientes.Cuantas[Pau] = 11;

        var resumen = await Servicio().AvisarDelRepasoAsync();

        resumen.Enviados.Should().Be(1);
        emisor.Enviados.Should().ContainSingle().Which.Endpoint.Should().Contain("pau");
    }
}
