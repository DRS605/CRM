using FluentAssertions;
using Matchketing.Repaso.Aplicacion;
using Matchketing.Repaso.Dominio;
using Xunit;

namespace Matchketing.Repaso.Tests;

public sealed class PruebasServicioRepaso
{
    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Usuario = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ConsultaDePrueba consulta = new();
    private readonly PospuestasEnMemoria pospuestas = new();
    private readonly AccionesDePrueba acciones = new();

    /// <summary>Un martes, para que «el siguiente día laborable» sea el miércoles y no el lunes.</summary>
    private readonly RelojFijo reloj = new(new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero));

    private ServicioRepaso Servicio(bool sinEmpresa = false) =>
        new(consulta, pospuestas, acciones, new ContextoDePrueba(sinEmpresa ? null : Empresa, Usuario), reloj);

    private Hallazgo Hallazgo(TipoPregunta tipo, int dias = 5, Guid? id = null, decimal? importe = null, int? match = null)
    {
        var referencia = id ?? Guid.NewGuid();
        return new Hallazgo(
            tipo, referencia,
            ContactoId: referencia, NombreContacto: "Manolo García", Telefono: "+34961234567",
            OportunidadId: tipo is TipoPregunta.CierrePasado or TipoPregunta.OportunidadEstancada ? referencia : null,
            TareaId: tipo == TipoPregunta.TareaVencida ? referencia : null,
            Titulo: "Instalación de cocina",
            Importe: importe, Match: match, Dias: dias,
            Fecha: tipo == TipoPregunta.CierrePasado ? new DateOnly(2026, 8, 10) : null);
    }

    // ---- La pila ---------------------------------------------------------------------------

    [Fact]
    public async Task Sin_nada_pendiente_lo_dice_y_se_calla()
    {
        var pila = await Servicio().PilaAsync();

        pila.AlDia.Should().BeTrue();
        pila.Preguntas.Should().BeEmpty();
        pila.SegundosEstimados.Should().Be(0);
    }

    [Fact]
    public async Task Toda_pregunta_lleva_su_motivo_escrito()
    {
        // La misma regla que en Hoy: una tarjeta sin motivo no se enseña. Se comprueba de **todos** los
        // tipos de una vez, y el recuento se saca del propio enumerado en vez de escribirlo a mano: así
        // añadir un tipo nuevo sin darle redacción hace que esto se caiga, que es lo que se quiere.
        // (Con el número escrito a mano se cayó al añadir el séptimo, y funcionó igual de bien.)
        var tipos = Enum.GetValues<TipoPregunta>();
        foreach (var tipo in tipos)
        {
            consulta.Hallazgos.Add(Hallazgo(tipo, importe: 8400m, match: 82));
        }

        var pila = await Servicio().PilaAsync();

        pila.Preguntas.Should().HaveCount(tipos.Length);
        pila.Preguntas.Should().OnlyContain(p => p.Titular.Length > 0 && p.Detalle.Length > 0);
        pila.Preguntas.Should().OnlyContain(p => p.Opciones.Count >= 3);
    }

    [Fact]
    public async Task Lo_que_rompe_una_promesa_se_pregunta_primero()
    {
        // El orden no es estético: si lo primero que ves es un aviso de «este contacto está callado»
        // mientras tienes tres tareas vencidas, el repaso parece ruido.
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.ClienteSinSiguientePaso));
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.SilencioCaliente, match: 80));
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.TareaVencida));

        var pila = await Servicio().PilaAsync();

        pila.Preguntas.Select(p => p.Tipo).Should().ContainInOrder(
            TipoPregunta.TareaVencida, TipoPregunta.SilencioCaliente, TipoPregunta.ClienteSinSiguientePaso);
    }

    [Fact]
    public async Task Dentro_de_un_tipo_manda_lo_mas_atrasado()
    {
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.TareaVencida, dias: 2));
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.TareaVencida, dias: 30));
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.TareaVencida, dias: 9));

        var pila = await Servicio().PilaAsync();

        pila.Preguntas.Select(p => p.Detalle).First().Should().Contain("4 semanas");
    }

    [Fact]
    public async Task La_pila_se_corta_pero_dice_cuantas_hay()
    {
        // Servir doscientas tarjetas y dejar que la persona descubra sola que esto no se acaba es la
        // forma más rápida de que no vuelva.
        for (var i = 0; i < 45; i++)
        {
            consulta.Hallazgos.Add(Hallazgo(TipoPregunta.TareaVencida, dias: i + 1));
        }

        var pila = await Servicio().PilaAsync();

        pila.Preguntas.Should().HaveCount(ServicioRepaso.TopePila);
        pila.Total.Should().Be(45);
        pila.SegundosEstimados.Should().Be(45 * ServicioRepaso.SegundosPorPregunta);
    }

    [Fact]
    public async Task Una_pregunta_aparcada_no_vuelve_a_salir()
    {
        var id = Guid.NewGuid();
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.OportunidadEstancada, id: id, importe: 5000m));
        var servicio = Servicio();

        (await servicio.PilaAsync()).Total.Should().Be(1);
        await servicio.ResponderAsync($"oportunidad-estancada:{id}", Respuesta.SigueViva);

        // El hallazgo sigue ahí —la oportunidad sigue estancada— y aun así la pila está vacía. Eso es
        // lo que permite acabar el repaso en vez de encontrarse mañana lo mismo.
        (await servicio.PilaAsync()).AlDia.Should().BeTrue();
    }

    // ---- R1 y R2 --------------------------------------------------------------------------

    [Fact]
    public async Task Una_respuesta_que_no_es_de_esa_pregunta_se_rechaza()
    {
        // R1. Sin esto, «Ganada» sobre una tarea buscaría una oportunidad que no existe.
        var r = await Servicio().ResponderAsync($"tarea-vencida:{Guid.NewGuid()}", Respuesta.Ganada);

        r.Error!.Codigo.Should().Be("repaso.respuesta_no_valida");
        acciones.Hechas.Should().BeEmpty();
    }

    [Fact]
    public async Task Perder_una_oportunidad_no_se_hace_de_un_toque()
    {
        // R2. Es lo que permite tocar rápido sin miedo: lo irreversible pide su dato.
        var id = Guid.NewGuid();

        var sinMotivo = await Servicio().ResponderAsync($"oportunidad-estancada:{id}", Respuesta.Perdida);

        sinMotivo.Error!.Codigo.Should().Be("repaso.falta_motivo");
        acciones.Hechas.Should().BeEmpty();

        var conMotivo = await Servicio().ResponderAsync($"oportunidad-estancada:{id}", Respuesta.Perdida, motivo: 1);

        conMotivo.Exito.Should().BeTrue();
        acciones.Hechas.Should().Contain($"perder:{id}:1");
    }

    [Fact]
    public async Task Ganar_si_es_de_un_toque_y_dice_cuanto()
    {
        var id = Guid.NewGuid();

        var r = await Servicio().ResponderAsync($"oportunidad-estancada:{id}", Respuesta.Ganada);

        r.Valor.Efecto.Should().Contain("8.400");
        acciones.Hechas.Should().Contain($"ganar:{id}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sin-dos-puntos")]
    [InlineData("tipo-inventado:11111111-1111-1111-1111-111111111111")]
    [InlineData("tarea-vencida:no-es-un-guid")]
    public async Task Una_clave_que_no_existe_se_rechaza(string? clave)
    {
        (await Servicio().ResponderAsync(clave, Respuesta.Hecha)).Error!.Codigo.Should().Be("repaso.clave_no_valida");
    }

    [Fact]
    public async Task Sin_empresa_activa_no_se_responde_nada()
    {
        var r = await Servicio(sinEmpresa: true).ResponderAsync($"tarea-vencida:{Guid.NewGuid()}", Respuesta.Hecha);

        r.Error!.Codigo.Should().Be("empresa.sin_seleccionar");
        acciones.Hechas.Should().BeEmpty();
    }

    // ---- Qué hace cada respuesta ---------------------------------------------------------

    [Fact]
    public async Task Hecha_cierra_la_tarea()
    {
        var id = Guid.NewGuid();

        (await Servicio().ResponderAsync($"tarea-vencida:{id}", Respuesta.Hecha)).Exito.Should().BeTrue();

        acciones.Hechas.Should().ContainSingle().Which.Should().Be($"completar:{id}");
    }

    [Fact]
    public async Task Aun_no_manda_la_tarea_al_siguiente_dia_laborable()
    {
        // Contestado un viernes, «mañana» sería sábado: aparecería en una pantalla que nadie abre y
        // volvería vencida el lunes, que es justo la sensación de que el CRM no te escucha.
        reloj.AhoraUtc = new DateTimeOffset(2026, 8, 21, 17, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();

        await Servicio().ResponderAsync($"tarea-vencida:{id}", Respuesta.AunNo);

        acciones.Hechas.Should().ContainSingle().Which.Should().Be($"aplazar:{id}:2026-08-24");
    }

    [Fact]
    public async Task No_le_interesa_apunta_la_llamada_y_descarta_el_contacto()
    {
        // Dejarlo como lead haría que volviera a salir en Hoy la semana siguiente.
        var id = Guid.NewGuid();

        await Servicio().ResponderAsync($"lead-sin-tocar:{id}", Respuesta.NoLeInteresa);

        acciones.Hechas.Should().Equal($"llamada:{id}:NoInteresa", $"descartar-contacto:{id}");
    }

    [Fact]
    public async Task Contactado_solo_apunta_la_llamada()
    {
        var id = Guid.NewGuid();

        await Servicio().ResponderAsync($"lead-sin-tocar:{id}", Respuesta.Contactado);

        acciones.Hechas.Should().Equal($"llamada:{id}:Contactado");
    }

    [Fact]
    public async Task Sigue_viva_no_toca_el_embudo()
    {
        // Mover la fecha de entrada en la etapa para que dejara de contar como estancada sería falsear
        // el histórico del embudo para arreglar un problema de pantalla.
        var id = Guid.NewGuid();

        var r = await Servicio().ResponderAsync($"oportunidad-estancada:{id}", Respuesta.SigueViva);

        r.Exito.Should().BeTrue();
        acciones.Hechas.Should().BeEmpty();
        pospuestas.Todas.Should().ContainSingle();
    }

    [Fact]
    public async Task Otra_fecha_retrasa_el_cierre_dos_semanas()
    {
        var id = Guid.NewGuid();

        await Servicio().ResponderAsync($"cierre-pasado:{id}", Respuesta.OtraFecha);

        acciones.Hechas.Should().ContainSingle().Which.Should().Be($"mover-cierre:{id}:2026-09-01");
    }

    [Fact]
    public async Task Le_llamo_hoy_crea_la_tarea_para_hoy()
    {
        var id = Guid.NewGuid();

        await Servicio().ResponderAsync($"silencio-caliente:{id}", Respuesta.LlamarHoy);

        acciones.Hechas.Should().ContainSingle().Which.Should().Be($"tarea:{id}:2026-08-18");
    }

    [Fact]
    public async Task Dejarlo_estar_no_hace_nada_pero_calla_el_aviso_un_mes()
    {
        var id = Guid.NewGuid();

        await Servicio().ResponderAsync($"silencio-caliente:{id}", Respuesta.DejarloEstar);

        acciones.Hechas.Should().BeEmpty();
        pospuestas.Todas.Single().Hasta.Should().Be(new DateOnly(2026, 9, 17));
    }

    [Fact]
    public async Task Ahora_no_aparca_poco_para_que_no_sirva_de_escondite()
    {
        var id = Guid.NewGuid();

        await Servicio().ResponderAsync($"tarea-vencida:{id}", Respuesta.Saltar);

        acciones.Hechas.Should().BeEmpty();
        pospuestas.Todas.Single().Hasta.Should().Be(new DateOnly(2026, 8, 21));
    }

    [Fact]
    public async Task Si_la_accion_falla_no_se_aparca_la_pregunta()
    {
        // Lo importante: si cerrar la tarea falla, la pregunta tiene que seguir ahí. Aparcarla habría
        // hecho desaparecer de la pantalla algo que no se hizo.
        acciones.Falla = true;
        var id = Guid.NewGuid();

        var r = await Servicio().ResponderAsync($"tarea-vencida:{id}", Respuesta.Hecha);

        r.Fallido.Should().BeTrue();
        pospuestas.Todas.Should().BeEmpty();
    }

    // ---- El resumen ----------------------------------------------------------------------

    [Fact]
    public async Task Abrir_una_campania_y_no_contestar_es_una_pregunta_propia()
    {
        // La pregunta que convierte una campaña en dinero. Lo que se comprueba aquí es que se **nombra la
        // campaña**: sin el nombre, la frase sería «abrió un correo» y quien llame no sabría de qué le
        // van a hablar.
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.AbrioLaCampania, dias: 2, match: 3));

        var pila = await Servicio().PilaAsync();

        var pregunta = pila.Preguntas.Should().ContainSingle().Subject;
        pregunta.Clave.Should().StartWith("abrio-campania:");
        pregunta.Detalle.Should().Contain("Instalación de cocina", "hay que decir qué campaña abrió");
        pregunta.Detalle.Should().Contain("3 veces");
        pregunta.Detalle.Should().Contain("no ha contestado");
    }

    [Fact]
    public async Task Una_campania_abierta_una_sola_vez_no_dice_el_numero()
    {
        // «Abrió «Oferta» 1 veces» es la marca de que el texto lo escribió una plantilla, y esto lo lee
        // el comercial todas las semanas.
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.AbrioLaCampania, dias: 1, match: 1));

        var pila = await Servicio().PilaAsync();

        pila.Preguntas[0].Detalle.Should().NotContain("1 veces");
        pila.Preguntas[0].Detalle.Should().Contain("hace un día");
    }

    [Fact]
    public async Task La_tarea_de_una_campania_abierta_no_dice_que_le_escribiste_tu()
    {
        // Mañana el comercial lee la tarea sin acordarse de esta pantalla. «Le escribí y no contestó»
        // sobre alguien a quien no escribió él sería desconcertante: lo que pasó es que abrió un envío.
        var id = Guid.NewGuid();

        await Servicio().ResponderAsync($"abrio-campania:{id}", Respuesta.LlamarHoy);

        acciones.Hechas.Should().ContainSingle().Which.Should().Be($"tarea:{id}:2026-08-18");
        acciones.Titulos.Should().ContainSingle().Which.Should().Be("Llamar: abrió la campaña y no contestó");
    }

    [Fact]
    public async Task Aparcar_una_campania_abierta_la_calla_un_mes_y_no_dos_semanas()
    {
        // Más que un correo personal sin contestar: aquí no hay una conversación empezada que se pueda
        // enfriar —él abrió un envío, no escribió—, y volver a sacarlo a los catorce días es ruido.
        var id = Guid.NewGuid();

        await Servicio().ResponderAsync($"abrio-campania:{id}", Respuesta.DejarloEstar);

        acciones.Hechas.Should().BeEmpty();
        pospuestas.Todas.Single().Hasta.Should().Be(new DateOnly(2026, 9, 17));
    }

    [Fact]
    public async Task Una_campania_abierta_se_pregunta_despues_de_todo_lo_demas()
    {
        // El orden del enum es el orden de prioridad, y esta va última a propósito: una campaña abierta
        // interesa, pero menos que una promesa incumplida o que una venta con dinero encima. Importa
        // porque la pila se corta en treinta: si esto fuera primero, una campaña con ochenta aperturas
        // dejaría fuera la tarea que vencía ayer.
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.AbrioLaCampania, dias: 90));
        consulta.Hallazgos.Add(Hallazgo(TipoPregunta.TareaVencida, dias: 1));

        var pila = await Servicio().PilaAsync();

        pila.Preguntas[0].Tipo.Should().Be(TipoPregunta.TareaVencida);
        pila.Preguntas[1].Tipo.Should().Be(TipoPregunta.AbrioLaCampania);
    }

    [Fact]
    public async Task El_resumen_habla_de_ventas_cuando_hay_ventas()
    {
        consulta.Resumen = new ResumenSemana(7, 14, 9, 3, 4, 2, 12400m, 1, 11, 18);

        var resumen = await Servicio().ResumenAsync();

        resumen.Titular.Should().Be("Has cerrado 2 ventas por 12.400 €.");
        resumen.RatioCierre.Should().Be(67);
    }

    [Fact]
    public async Task Una_semana_floja_se_cuenta_sin_regañar()
    {
        // Un CRM que echa la culpa se cierra y no se vuelve a abrir.
        consulta.Resumen = new ResumenSemana(7, 6, 11, 1, 0, 0, 0m, 0, 3, 7);

        var resumen = await Servicio().ResumenAsync();

        resumen.Titular.Should().Be("6 llamadas esta semana. Ninguna cerrada todavía.");
        resumen.RatioCierre.Should().BeNull();
    }

    [Fact]
    public async Task Sin_nada_que_contar_no_se_inventa_nada()
    {
        (await Servicio().ResumenAsync()).Titular.Should().Be("Semana tranquila.");
    }
}
