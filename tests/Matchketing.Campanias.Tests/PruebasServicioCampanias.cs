using FluentAssertions;
using Matchketing.Campanias.Aplicacion;
using Matchketing.Campanias.Dominio;
using Matchketing.Nucleo.Resultados;
using Xunit;

namespace Matchketing.Campanias.Tests;

public sealed class PruebasServicioCampanias
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();

    private readonly RelojFijo reloj = new(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly RepositorioEnMemoria repositorio = new();
    private readonly BuscadorDePrueba busca = new();
    private readonly PlantillasDePrueba plantillas = new();
    private readonly EncoladorDePrueba encola = new();
    private readonly ContadoresDePrueba contadores = new();

    private ServicioCampanias Servicio => new(
        repositorio, busca, plantillas, encola, contadores,
        new ContextoDePrueba(Empresa, Usuario), reloj);

    private static CriteriosSegmento Clientes =>
        CriteriosSegmento.Crear(EstadoBuscado.Cliente, null, null, null, null, null).Valor;

    private async Task<(Guid SegmentoId, Guid CampaniaId)> PreparadaAsync(int cuantos)
    {
        var s = await Servicio.CrearSegmentoAsync("Clientes", Clientes);
        busca.Contactos = Enumerable.Range(0, cuantos).Select(_ => Guid.NewGuid()).ToList();

        var c = await Servicio.CrearAsync("Oferta", s.Valor.Id, plantillas.Plantilla!.Id);

        return (s.Valor.Id, c.Valor.Id);
    }

    // ---------- Segmentos ----------

    [Fact]
    public async Task Hay_un_techo_de_segmentos()
    {
        for (var i = 0; i < ServicioCampanias.MaximoSegmentos; i++)
        {
            (await Servicio.CrearSegmentoAsync("S" + i, Clientes))
                .Exito.Should().BeTrue();
        }

        var r = await Servicio.CrearSegmentoAsync("Uno más", Clientes);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("segmento.demasiados");
    }

    [Fact]
    public async Task Un_segmento_que_ya_ha_lanzado_campanias_no_se_borra()
    {
        var (segmentoId, _) = await PreparadaAsync(3);

        var r = await Servicio.BorrarSegmentoAsync(segmentoId);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("segmento.en_uso");

        // Y el mensaje cuenta bien en singular: es la clase de detalle que delata un texto de plantilla.
        r.Error!.Mensaje.Should().Contain("Hay una campaña");
    }

    [Fact]
    public async Task Un_segmento_sin_campanias_si_se_borra()
    {
        var s = await Servicio.CrearSegmentoAsync("Clientes", Clientes);

        (await Servicio.BorrarSegmentoAsync(s.Valor.Id)).Exito.Should().BeTrue();
        repositorio.Segmentos.Should().BeEmpty();
    }

    [Fact]
    public async Task La_lista_de_segmentos_cuenta_cuantos_hay_ahora_y_no_un_numero_guardado()
    {
        await Servicio.CrearSegmentoAsync("Clientes", Clientes);

        busca.Contactos = [Guid.NewGuid(), Guid.NewGuid()];
        (await Servicio.SegmentosAsync())[0].Cuantos.Should().Be(2);

        // Entra uno nuevo por el formulario y el número de la lista cambia sin tocar el segmento. Eso es
        // toda la diferencia con una lista importada.
        busca.Contactos.Add(Guid.NewGuid());
        (await Servicio.SegmentosAsync())[0].Cuantos.Should().Be(3);
    }

    [Fact]
    public async Task La_vista_previa_ensena_a_quien_se_le_va_a_escribir()
    {
        var s = await Servicio.CrearSegmentoAsync("Clientes", Clientes);
        busca.Contactos = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToList();

        var r = await Servicio.VistaPreviaAsync(s.Valor.Id);

        r.Exito.Should().BeTrue();
        r.Valor.Cuantos.Should().Be(40, "el total es el total");
        r.Valor.Muestra.Should().HaveCount(ServicioCampanias.MuestraPrevia, "la muestra es una muestra");
        r.Valor.Frase.Should().Be("clientes");
    }

    [Fact]
    public async Task La_frase_del_segmento_nombra_la_etapa_preguntando_al_embudo()
    {
        // El módulo no conoce el embudo: el nombre de la etapa lo trae el puerto. Esta prueba fija que se
        // le pregunta, porque sin preguntar la frase saldría genérica y la ficha de la campaña no diría
        // en qué etapa estaban los que la recibieron.
        busca.NombreEtapa = "Propuesta";
        var criterios = CriteriosSegmento.Crear(null, null, null, null, null, Guid.NewGuid()).Valor;
        var s = await Servicio.CrearSegmentoAsync("En propuesta", criterios);

        var lista = await Servicio.SegmentosAsync();

        lista.Should().ContainSingle();
        lista[0].Frase.Should().Be("contactos, con una oportunidad abierta en «Propuesta»");
        s.Exito.Should().BeTrue();
    }

    // ---------- Crear y lanzar ----------

    [Fact]
    public async Task Una_campania_no_acepta_una_plantilla_de_atender_solicitudes()
    {
        // El error más caro del módulo: mandar a quinientas personas un texto escrito para contestar a
        // una sola. Además de sonar raro, se apoyaría en una base legal que no cubre el envío.
        var s = await Servicio.CrearSegmentoAsync("Clientes", Clientes);
        plantillas.Plantilla = new DatosPlantilla(Guid.NewGuid(), "Te contesto", "Tu consulta", EsComercial: false);

        var r = await Servicio.CrearAsync("Oferta", s.Valor.Id, plantillas.Plantilla.Id);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campania.plantilla_no_comercial");
    }

    [Fact]
    public async Task Si_la_plantilla_deja_de_ser_comercial_entre_crear_y_lanzar_no_se_lanza()
    {
        // Entre crear la campaña y lanzarla pueden pasar días. Comprobarlo solo al crear dejaría un hueco
        // por el que se cuela justo lo que la comprobación existe para impedir.
        var (_, campaniaId) = await PreparadaAsync(3);
        plantillas.Plantilla = new DatosPlantilla(plantillas.Plantilla!.Id, "Te contesto", "Tu consulta", false);

        var r = await Servicio.LanzarAsync(campaniaId);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campania.plantilla_no_comercial");
        repositorio.Envios.Should().BeEmpty("no se congela ninguna audiencia si no se puede lanzar");
    }

    [Fact]
    public async Task Lanzar_congela_la_audiencia_y_no_manda_nada_todavia()
    {
        var (_, campaniaId) = await PreparadaAsync(5);

        var r = await Servicio.LanzarAsync(campaniaId);

        r.Exito.Should().BeTrue();
        r.Valor.Estado.Should().Be(EstadoCampania.Enviando);
        r.Valor.Destinatarios.Should().Be(5);

        repositorio.Envios.Should().HaveCount(5);
        repositorio.Envios.Should().OnlyContain(e => e.Estado == EstadoEnvio.Pendiente);
        encola.Encolados.Should().BeEmpty("lanzar no manda correos: solo congela a quién se le va a mandar");
    }

    [Fact]
    public async Task El_segmento_se_resuelve_al_lanzar_y_no_al_crear_la_campania()
    {
        var (_, campaniaId) = await PreparadaAsync(2);

        // Entra alguien nuevo entre crear la campaña y lanzarla. Tiene que entrar en la audiencia: el
        // segmento es un filtro, no una foto de cuando se guardó.
        busca.Contactos.Add(Guid.NewGuid());

        var r = await Servicio.LanzarAsync(campaniaId);

        r.Valor.Destinatarios.Should().Be(3);
    }

    [Fact]
    public async Task Un_contacto_repetido_por_el_segmento_solo_recibe_una_vez()
    {
        // Un contacto con dos oportunidades abiertas en la misma etapa saldría dos veces de una consulta
        // con `join`. Sin este `Distinct`, recibiría el correo dos veces y el número de destinatarios
        // mentiría.
        var repetido = Guid.NewGuid();
        var (_, campaniaId) = await PreparadaAsync(0);
        busca.Contactos = [repetido, Guid.NewGuid(), repetido];

        var r = await Servicio.LanzarAsync(campaniaId);

        r.Valor.Destinatarios.Should().Be(2);
        repositorio.Envios.Should().HaveCount(2);
    }

    [Fact]
    public async Task No_se_lanza_a_un_segmento_que_hoy_no_tiene_a_nadie()
    {
        var (_, campaniaId) = await PreparadaAsync(0);
        busca.Contactos = [];

        var r = await Servicio.LanzarAsync(campaniaId);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campania.segmento_vacio");
    }

    [Fact]
    public async Task Sin_sesion_no_se_lanza_una_campania()
    {
        var (_, campaniaId) = await PreparadaAsync(3);
        var sinUsuario = new ServicioCampanias(
            repositorio, busca, plantillas, encola, contadores,
            new ContextoDePrueba(Empresa, null), reloj);

        var r = await sinUsuario.LanzarAsync(campaniaId);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("sesion.sin_usuario");
    }

    [Fact]
    public async Task Una_campania_lanzada_no_se_borra()
    {
        var (_, campaniaId) = await PreparadaAsync(3);
        await Servicio.LanzarAsync(campaniaId);

        var r = await Servicio.BorrarAsync(campaniaId);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campania.ya_lanzada");
    }

    // ---------- El lote ----------

    [Fact]
    public async Task El_lote_pregunta_por_el_permiso_de_cada_persona_una_por_una()
    {
        // Es la promesa del módulo. No hay un camino que compruebe «el permiso del segmento», porque el
        // permiso no es del segmento: es de cada persona, y puede haber cambiado desde que se lanzó.
        var (_, campaniaId) = await PreparadaAsync(3);
        await Servicio.LanzarAsync(campaniaId);

        var sinPermiso = repositorio.Envios[1].ContactoId;
        encola.Niega = c => c == sinPermiso
            ? Error.Validacion("cumplimiento.sin_consentimiento", "No ha dado su consentimiento comercial.")
            : null;

        var pasada = await Servicio.EncolarLoteAsync();

        pasada.Encolados.Should().Be(2);
        pasada.Excluidos.Should().Be(1);

        var fuera = repositorio.Envios.Single(e => e.ContactoId == sinPermiso);
        fuera.Estado.Should().Be(EstadoEnvio.Excluido);
        fuera.Motivo.Should().Be("No ha dado su consentimiento comercial.");
        fuera.CorreoId.Should().BeNull();
    }

    [Fact]
    public async Task Los_correos_salen_en_nombre_de_quien_lanzo_la_campania()
    {
        // No los manda «el sistema»: los firma una persona, que es a quien le van a contestar. Y que
        // haya una firma es parte de que alguien se lo piense antes de darle al botón.
        var (_, campaniaId) = await PreparadaAsync(2);
        await Servicio.LanzarAsync(campaniaId);

        await Servicio.EncolarLoteAsync();

        encola.Encolados.Should().OnlyContain(e => e.EnNombreDe == Usuario);
    }

    [Fact]
    public async Task El_lote_no_pasa_del_tope_por_pasada()
    {
        // El tope no es por la base de datos: es por el SMTP del cliente, que corta la conexión al pasarse
        // de su límite por hora. Una campaña lenta es mejor que un dominio bloqueado.
        var (_, campaniaId) = await PreparadaAsync(ServicioCampanias.PorPasada + 20);
        await Servicio.LanzarAsync(campaniaId);

        var primera = await Servicio.EncolarLoteAsync();

        primera.Encolados.Should().Be(ServicioCampanias.PorPasada);
        (await repositorio.CampaniaAsync(campaniaId))!
            .Estado.Should().Be(EstadoCampania.Enviando);

        var segunda = await Servicio.EncolarLoteAsync();

        segunda.Encolados.Should().Be(20);
        segunda.Cerradas.Should().Be(1);
        (await repositorio.CampaniaAsync(campaniaId))!
            .Estado.Should().Be(EstadoCampania.Enviada);
    }

    [Fact]
    public async Task Una_pasada_sin_campanias_en_marcha_no_hace_nada()
    {
        var pasada = await Servicio.EncolarLoteAsync();

        pasada.Should().Be(new PasadaCampanias(0, 0, 0, 0));
        encola.Encolados.Should().BeEmpty();
    }

    [Fact]
    public async Task Detener_descarta_los_pendientes_con_el_motivo_escrito_y_la_suma_cuadra()
    {
        var (_, campaniaId) = await PreparadaAsync(ServicioCampanias.PorPasada + 10);
        await Servicio.LanzarAsync(campaniaId);
        await Servicio.EncolarLoteAsync();

        (await Servicio.DetenerAsync(campaniaId)).Exito.Should().BeTrue();

        var c = (await repositorio.CampaniaAsync(campaniaId))!;
        c.Estado.Should().Be(EstadoCampania.Detenida);
        c.Encolados.Should().Be(ServicioCampanias.PorPasada);
        c.Excluidos.Should().Be(10);
        (c.Encolados + c.Excluidos).Should().Be(c.Destinatarios, "en la ficha no puede faltar nadie");

        repositorio.Envios
            .Where(e => e.Estado == EstadoEnvio.Excluido)
            .Should().OnlyContain(e => e.Motivo == "La campaña se detuvo antes de llegarle el turno.");
    }

    [Fact]
    public async Task Detenida_no_se_encola_nada_mas()
    {
        var (_, campaniaId) = await PreparadaAsync(10);
        await Servicio.LanzarAsync(campaniaId);
        await Servicio.DetenerAsync(campaniaId);

        var pasada = await Servicio.EncolarLoteAsync();

        pasada.Encolados.Should().Be(0);
        encola.Encolados.Should().BeEmpty();
    }

    // ---------- La ficha ----------

    [Fact]
    public async Task La_ficha_agrupa_los_motivos_de_exclusion_de_mas_a_menos()
    {
        // Ciento veinte filas que dicen lo mismo no enseñan nada. «94 sin consentimiento comercial»
        // enseña qué hay que arreglar antes de la próxima campaña, que es para lo que sirve el informe.
        var (_, campaniaId) = await PreparadaAsync(6);
        await Servicio.LanzarAsync(campaniaId);

        var contactos = repositorio.Envios.Select(e => e.ContactoId).ToList();
        encola.Niega = c =>
        {
            if (contactos.IndexOf(c) < 3)
            {
                return Error.Validacion("x", "No ha dado su consentimiento comercial.");
            }

            return contactos.IndexOf(c) == 3 ? Error.Validacion("y", "Se dio de baja.") : null;
        };

        await Servicio.EncolarLoteAsync();
        contadores.Contadores = new ContadoresCorreo(2, 0, 0, 0, 1);

        var r = await Servicio.DetalleAsync(campaniaId);

        r.Exito.Should().BeTrue();
        r.Valor.PorQueNoLlego.Should().HaveCount(2);
        r.Valor.PorQueNoLlego[0].Should().Be(new MotivoExclusion("No ha dado su consentimiento comercial.", 3));
        r.Valor.PorQueNoLlego[1].Should().Be(new MotivoExclusion("Se dio de baja.", 1));
        r.Valor.Correos.Abiertos.Should().Be(1);
        r.Valor.Campania.Encolados.Should().Be(2);
    }

    [Fact]
    public async Task La_ficha_sigue_diciendo_a_quien_apuntaba_aunque_el_segmento_haya_cambiado()
    {
        var (segmentoId, campaniaId) = await PreparadaAsync(2);
        await Servicio.LanzarAsync(campaniaId);

        // Se edita el segmento después de lanzar. La campaña tiene que seguir contando a quién le escribió
        // el día que salió, no a quién apuntaría el segmento hoy.
        var otros = CriteriosSegmento.Crear(EstadoBuscado.Perdido, "Teruel", null, null, null, null).Valor;
        (await Servicio.CambiarSegmentoAsync(segmentoId, "Otra cosa", otros))
            .Exito.Should().BeTrue();

        var r = await Servicio.DetalleAsync(campaniaId);

        r.Valor.SegmentoAlLanzar.Should().Be("Clientes: clientes");
    }
}
