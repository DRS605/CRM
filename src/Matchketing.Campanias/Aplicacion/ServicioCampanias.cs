using Matchketing.Campanias.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Campanias.Aplicacion;

public sealed class ServicioCampanias(
    IRepositorioCampanias repositorio,
    IBuscaContactosDelSegmento busca,
    IPlantillaDeCampania plantillas,
    IEncolaCorreoDeCampania encola,
    IConsultaEnviosDeCampania consulta,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    /// <summary>
    /// Veinte segmentos. Es un techo bajo a propósito: el valor de un segmento es que alguien lo mire y
    /// lo entienda, y una lista de ochenta filtros parecidos no la mira nadie. Además la lista cuenta
    /// cuántos contactos tiene cada uno, y eso son tantas consultas como segmentos.
    /// </summary>
    public const int MaximoSegmentos = 20;

    /// <summary>Cuántos caben en la vista previa antes de lanzar.</summary>
    public const int MuestraPrevia = 12;

    /// <summary>
    /// Cuántos correos se encolan por pasada del trabajo, y por qué tan pocos.
    ///
    /// No es por la base de datos: es por el servidor de correo del cliente. Una PYME manda su correo
    /// por el SMTP de su proveedor, que tiene un límite por hora y lo aplica cortando la conexión. Cien
    /// correos encolados de golpe cada minuto se convierten en un bloqueo temporal del dominio, y de ahí
    /// se sale peor que de una campaña lenta. Cincuenta por minuto son tres mil por hora, más de lo que
    /// admite una campaña entera.
    /// </summary>
    public const int PorPasada = 50;

    /// <summary>Cuántos excluidos se enseñan en la ficha. Con los motivos agrupados aparte.</summary>
    public const int ExcluidosVisibles = 100;

    // ================= Segmentos =================

    public async Task<Resultado<Segmento>> CrearSegmentoAsync(
        string? nombre, CriteriosSegmento criterios, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Segmento>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var todos = await repositorio.SegmentosAsync(ct).ConfigureAwait(false);
        if (todos.Count >= MaximoSegmentos)
        {
            return Resultado.Fallo<Segmento>(Error.Conflicto(
                "segmento.demasiados", $"No se pueden tener más de {MaximoSegmentos} segmentos."));
        }

        var creado = Segmento.Crear(empresaId, nombre, criterios, reloj);
        if (creado.Fallido)
        {
            return creado;
        }

        repositorio.Anadir(creado.Valor);
        return creado;
    }

    public async Task<Resultado> CambiarSegmentoAsync(
        Guid id, string? nombre, CriteriosSegmento criterios, CancellationToken ct = default)
    {
        var segmento = await repositorio.SegmentoAsync(id, ct).ConfigureAwait(false);

        // Editar un segmento que ya ha lanzado campañas está permitido, y no rompe nada: cada campaña
        // se quedó con su audiencia congelada y con la frase del segmento tal como estaba al lanzar.
        // Prohibirlo obligaría a duplicar el segmento cada vez que cambia una provincia.
        return segmento is null
            ? Resultado.Fallo(Error.NoEncontrado("segmento.no_encontrado", "Ese segmento no existe."))
            : segmento.Cambiar(nombre, criterios, reloj);
    }

    public async Task<Resultado> BorrarSegmentoAsync(Guid id, CancellationToken ct = default)
    {
        var segmento = await repositorio.SegmentoAsync(id, ct).ConfigureAwait(false);
        if (segmento is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("segmento.no_encontrado", "Ese segmento no existe."));
        }

        // Un segmento que ha lanzado campañas no se borra. La campaña guarda la frase, sí, pero también
        // guarda el identificador, y una ficha que dice «segmento: (borrado)» es la clase de agujero que
        // hace inútil un historial. Cuesta más explicar por qué no se puede borrar que arreglarlo después.
        var usos = await repositorio.CuantasUsanAsync(id, ct).ConfigureAwait(false);
        if (usos > 0)
        {
            return Resultado.Fallo(Error.Conflicto(
                "segmento.en_uso",
                usos == 1
                    ? "Hay una campaña que usa este segmento. No se puede borrar."
                    : $"Hay {usos} campañas que usan este segmento. No se puede borrar."));
        }

        repositorio.Quitar(segmento);
        return Resultado.Ok();
    }

    public async Task<IReadOnlyList<FichaSegmento>> SegmentosAsync(CancellationToken ct = default)
    {
        var todos = await repositorio.SegmentosAsync(ct).ConfigureAwait(false);
        var fichas = new List<FichaSegmento>(todos.Count);

        foreach (var s in todos.OrderBy(s => s.Nombre, StringComparer.OrdinalIgnoreCase))
        {
            var cuantos = await busca.ContarAsync(s.Criterios, ct).ConfigureAwait(false);
            var usos = await repositorio.CuantasUsanAsync(s.Id, ct).ConfigureAwait(false);

            fichas.Add(new FichaSegmento(
                s.Id, s.Nombre, await FraseAsync(s.Criterios, ct).ConfigureAwait(false), cuantos,
                s.Criterios.Estado, s.Criterios.Provincia, s.Criterios.Origen,
                s.Criterios.MatchMinimo, s.Criterios.SinActividadDias, s.Criterios.EtapaId,
                usos > 0, s.CreadoEn));
        }

        return fichas;
    }

    /// <summary>
    /// A quién le va a llegar, antes de lanzar nada.
    ///
    /// Existe porque un segmento es un filtro y un filtro se escribe mal. Ver doce nombres reconocibles
    /// es lo que convierte «creo que esto son mis clientes de Valencia» en «sí, son estos». Sin esta
    /// pantalla, la primera vez que alguien comprueba a quién apuntaba su segmento es leyendo la
    /// respuesta de alguien a quien no quería escribir.
    /// </summary>
    public async Task<Resultado<VistaPreviaSegmento>> VistaPreviaAsync(Guid segmentoId, CancellationToken ct = default)
    {
        var segmento = await repositorio.SegmentoAsync(segmentoId, ct).ConfigureAwait(false);
        if (segmento is null)
        {
            return Resultado.Fallo<VistaPreviaSegmento>(
                Error.NoEncontrado("segmento.no_encontrado", "Ese segmento no existe."));
        }

        var cuantos = await busca.ContarAsync(segmento.Criterios, ct).ConfigureAwait(false);
        var muestra = await busca.MuestraAsync(segmento.Criterios, MuestraPrevia, ct).ConfigureAwait(false);

        return Resultado.Ok(new VistaPreviaSegmento(
            segmento.Id, segmento.Nombre,
            await FraseAsync(segmento.Criterios, ct).ConfigureAwait(false),
            cuantos, muestra));
    }

    // ================= Campañas =================

    public async Task<Resultado<Campania>> CrearAsync(
        string? nombre, Guid segmentoId, Guid plantillaId, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Campania>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var comprobado = await ComprobarPiezasAsync(segmentoId, plantillaId, ct).ConfigureAwait(false);
        if (comprobado.Fallido)
        {
            return Resultado.Fallo<Campania>(comprobado.Error!);
        }

        var creada = Campania.Crear(empresaId, nombre, segmentoId, plantillaId, reloj);
        if (creada.Fallido)
        {
            return creada;
        }

        repositorio.Anadir(creada.Valor);
        return creada;
    }

    public async Task<Resultado> CambiarAsync(
        Guid id, string? nombre, Guid segmentoId, Guid plantillaId, CancellationToken ct = default)
    {
        var campania = await repositorio.CampaniaAsync(id, ct).ConfigureAwait(false);
        if (campania is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("campania.no_encontrada", "Esa campaña no existe."));
        }

        var comprobado = await ComprobarPiezasAsync(segmentoId, plantillaId, ct).ConfigureAwait(false);
        return comprobado.Fallido ? comprobado : campania.Cambiar(nombre, segmentoId, plantillaId);
    }

    public async Task<Resultado> BorrarAsync(Guid id, CancellationToken ct = default)
    {
        var campania = await repositorio.CampaniaAsync(id, ct).ConfigureAwait(false);
        if (campania is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("campania.no_encontrada", "Esa campaña no existe."));
        }

        // Solo un borrador. Una campaña lanzada es un hecho —hay correos en buzones ajenos— y borrar la
        // fila no los recoge; lo único que consigue es que nadie pueda contestar a quién se le mandó.
        if (!campania.EsBorrador)
        {
            return Resultado.Fallo(Error.Conflicto(
                "campania.ya_lanzada", "Una campaña lanzada no se borra: es la prueba de a quién se le escribió."));
        }

        repositorio.Quitar(campania);
        return Resultado.Ok();
    }

    /// <summary>
    /// La lanza: resuelve el segmento, **congela la audiencia** escribiendo una fila por persona, y deja
    /// la campaña enviando. Aquí no sale ningún correo todavía.
    ///
    /// Separar «congelar» de «encolar» es lo que hace que esto se pueda contestar después: la fila existe
    /// desde el primer momento, así que si mañana alguien pregunta por una persona concreta, la respuesta
    /// es «estaba en la audiencia y se le excluyó por esto» o «estaba y se le mandó», nunca «no sé».
    /// </summary>
    public async Task<Resultado<Campania>> LanzarAsync(Guid id, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId || contexto.UsuarioId is not { } usuarioId)
        {
            return Resultado.Fallo<Campania>(Error.NoAutorizado("sesion.sin_usuario", "No hay sesión."));
        }

        var campania = await repositorio.CampaniaAsync(id, ct).ConfigureAwait(false);
        if (campania is null)
        {
            return Resultado.Fallo<Campania>(Error.NoEncontrado("campania.no_encontrada", "Esa campaña no existe."));
        }

        if (!campania.EsBorrador)
        {
            return Resultado.Fallo<Campania>(Error.Conflicto("campania.ya_lanzada", "Esta campaña ya se lanzó."));
        }

        // Las dos piezas se vuelven a comprobar **al lanzar** y no solo al crear la campaña: entre una
        // cosa y otra pueden pasar días, y en esos días alguien puede haber borrado el segmento o haber
        // cambiado la plantilla de comercial a «atender una solicitud».
        var comprobado = await ComprobarPiezasAsync(campania.SegmentoId, campania.PlantillaId, ct).ConfigureAwait(false);
        if (comprobado.Fallido)
        {
            return Resultado.Fallo<Campania>(comprobado.Error!);
        }

        var segmento = await repositorio.SegmentoAsync(campania.SegmentoId, ct).ConfigureAwait(false);
        if (segmento is null)
        {
            return Resultado.Fallo<Campania>(
                Error.NoEncontrado("segmento.no_encontrado", "El segmento de esta campaña ya no existe."));
        }

        // Se pide uno más del máximo. Así se distingue «tiene exactamente el máximo» de «tiene más», que
        // es lo que decide si el mensaje de error es un techo alcanzado o un segmento demasiado ancho.
        var contactos = await busca
            .ResolverAsync(segmento.Criterios, Campania.MaximoDestinatarios + 1, ct)
            .ConfigureAwait(false);

        var unicos = contactos.Distinct().ToArray();

        var frase = await FraseAsync(segmento.Criterios, ct).ConfigureAwait(false);
        var lanzada = campania.Lanzar(usuarioId, unicos.Length, segmento.Nombre + ": " + frase, reloj);
        if (lanzada.Fallido)
        {
            return Resultado.Fallo<Campania>(lanzada.Error!);
        }

        repositorio.Anadir(unicos.Select(c => EnvioCampania.Crear(empresaId, campania.Id, c)).ToArray());
        return Resultado.Ok(campania);
    }

    /// <summary>
    /// La para. Lo que estaba encolado sale igual; lo que quedaba pendiente se descarta con el motivo
    /// escrito, para que la suma siga cuadrando y la ficha no deje a nadie sin explicación.
    /// </summary>
    public async Task<Resultado> DetenerAsync(Guid id, CancellationToken ct = default)
    {
        var campania = await repositorio.CampaniaAsync(id, ct).ConfigureAwait(false);
        if (campania is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("campania.no_encontrada", "Esa campaña no existe."));
        }

        var detenida = campania.Detener(reloj);
        if (detenida.Fallido)
        {
            return detenida;
        }

        var pendientes = await repositorio.TodosLosPendientesAsync(id, ct).ConfigureAwait(false);
        var descartados = 0;
        foreach (var envio in pendientes)
        {
            if (envio.Excluir("La campaña se detuvo antes de llegarle el turno.", reloj))
            {
                descartados++;
            }
        }

        campania.DescartarPendientes(descartados);
        return Resultado.Ok();
    }

    public async Task<IReadOnlyList<FichaCampania>> CampaniasAsync(CancellationToken ct = default)
    {
        var todas = await repositorio.CampaniasAsync(ct).ConfigureAwait(false);
        var segmentos = (await repositorio.SegmentosAsync(ct).ConfigureAwait(false)).ToDictionary(s => s.Id);
        var fichas = new List<FichaCampania>(todas.Count);

        // Las lanzadas primero y las más recientes arriba; los borradores al final. Un borrador es
        // trabajo a medias, y lo que interesa mirar es lo que ya salió.
        foreach (var c in todas.OrderByDescending(c => c.LanzadaEn ?? DateTimeOffset.MinValue).ThenByDescending(c => c.CreadaEn))
        {
            fichas.Add(await FichaAsync(c, segmentos, ct).ConfigureAwait(false));
        }

        return fichas;
    }

    public async Task<Resultado<DetalleCampania>> DetalleAsync(Guid id, CancellationToken ct = default)
    {
        var campania = await repositorio.CampaniaAsync(id, ct).ConfigureAwait(false);
        if (campania is null)
        {
            return Resultado.Fallo<DetalleCampania>(
                Error.NoEncontrado("campania.no_encontrada", "Esa campaña no existe."));
        }

        var segmentos = (await repositorio.SegmentosAsync(ct).ConfigureAwait(false)).ToDictionary(s => s.Id);
        var contadores = await consulta.ContadoresAsync(id, ct).ConfigureAwait(false);
        var excluidos = await repositorio.ExcluidosAsync(id, ExcluidosVisibles, ct).ConfigureAwait(false);

        // Agrupados por motivo, de más a menos. Ciento veinte filas que dicen lo mismo no enseñan nada;
        // «94 sin consentimiento comercial» enseña qué hay que arreglar antes de la próxima campaña.
        var motivos = excluidos
            .GroupBy(e => e.Motivo ?? "Sin motivo apuntado.", StringComparer.Ordinal)
            .Select(g => new MotivoExclusion(g.Key, g.Count()))
            .OrderByDescending(m => m.Cuantos)
            .ThenBy(m => m.Motivo, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Resultado.Ok(new DetalleCampania(
            await FichaAsync(campania, segmentos, ct).ConfigureAwait(false),
            campania.SegmentoAlLanzar, contadores, motivos));
    }

    // ================= El trabajo que encola =================

    /// <summary>
    /// Encola el siguiente lote de cada campaña en marcha.
    ///
    /// Aquí es donde se cumple la promesa del módulo: **se pregunta por cada persona, una por una**. No
    /// hay un camino que compruebe el permiso «del segmento», porque el permiso no es del segmento; es
    /// de cada persona, y cambia entre el momento de lanzar y el momento de encolar.
    ///
    /// Y el correo se encola en nombre de quien lanzó la campaña, no del sistema: es quien firma el
    /// texto y a quien le van a contestar.
    /// </summary>
    public async Task<PasadaCampanias> EncolarLoteAsync(CancellationToken ct = default)
    {
        var enMarcha = await repositorio.EnMarchaAsync(ct).ConfigureAwait(false);
        if (enMarcha.Count == 0)
        {
            return new PasadaCampanias(0, 0, 0, 0);
        }

        int encolados = 0, excluidos = 0, cerradas = 0, tocadas = 0;

        foreach (var campania in enMarcha)
        {
            if (campania.LanzadaPor is not { } firma)
            {
                // No debería pasar —lanzar exige usuario— pero si pasara, encolar en nombre de nadie
                // haría salir correos sin remitente reconocible. Se para la campaña en vez de improvisar.
                continue;
            }

            var lote = await repositorio.PendientesAsync(campania.Id, PorPasada, ct).ConfigureAwait(false);
            if (lote.Count == 0)
            {
                continue;
            }

            tocadas++;

            foreach (var envio in lote)
            {
                var r = await encola
                    .EncolarAsync(envio.ContactoId, campania.PlantillaId, firma, ct)
                    .ConfigureAwait(false);

                if (r.Exito)
                {
                    if (envio.Encolar(r.Valor, reloj))
                    {
                        campania.Anotar(encolado: true, reloj);
                        encolados++;
                    }
                }
                else if (envio.Excluir(r.Error!.Mensaje, reloj))
                {
                    // Un fallo aquí es casi siempre lo correcto, no una avería: «no ha dado su
                    // consentimiento comercial», «se dio de baja», «no tiene ese dato para la plantilla».
                    // Por eso se guarda el mensaje tal cual y no un código: es lo que hay que leer.
                    campania.Anotar(encolado: false, reloj);
                    excluidos++;
                }
            }

            if (campania.Cerrada)
            {
                cerradas++;
            }
        }

        return new PasadaCampanias(tocadas, encolados, excluidos, cerradas);
    }

    // ================= Cocina =================

    /// <summary>
    /// Que el segmento exista y que la plantilla exista y sea comercial. Es la única comprobación que
    /// impide el error más caro de todos: mandar a quinientas personas un texto escrito para contestar a
    /// una sola, que además de sonar raro se apoyaría en una base legal que no cubre el envío.
    /// </summary>
    private async Task<Resultado> ComprobarPiezasAsync(Guid segmentoId, Guid plantillaId, CancellationToken ct)
    {
        if (await repositorio.SegmentoAsync(segmentoId, ct).ConfigureAwait(false) is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("segmento.no_encontrado", "Ese segmento no existe."));
        }

        var plantilla = await plantillas.DeAsync(plantillaId, ct).ConfigureAwait(false);
        if (plantilla is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("plantilla.no_encontrada", "Esa plantilla no existe."));
        }

        return plantilla.EsComercial
            ? Resultado.Ok()
            : Resultado.Fallo(Error.Validacion(
                "campania.plantilla_no_comercial",
                "Esa plantilla está escrita para atender una solicitud, no para una campaña. " +
                "Una campaña necesita una plantilla comercial, que es la que exige el consentimiento."));
    }

    private async Task<string> FraseAsync(CriteriosSegmento criterios, CancellationToken ct) =>
        criterios.EtapaId is { } etapa
            ? criterios.Frase(await busca.NombreDeEtapaAsync(etapa, ct).ConfigureAwait(false))
            : criterios.Frase();

    private async Task<FichaCampania> FichaAsync(
        Campania c, IReadOnlyDictionary<Guid, Segmento> segmentos, CancellationToken ct)
    {
        var plantilla = await plantillas.DeAsync(c.PlantillaId, ct).ConfigureAwait(false);

        return new FichaCampania(
            c.Id, c.Nombre, Texto(c.Estado), c.SegmentoId,
            segmentos.TryGetValue(c.SegmentoId, out var s) ? s.Nombre : null,
            c.PlantillaId, plantilla?.Nombre,
            c.Destinatarios, c.Encolados, c.Excluidos, c.Pendientes, c.CreadaEn, c.LanzadaEn);
    }

    private static string Texto(EstadoCampania estado) => estado switch
    {
        EstadoCampania.Enviando => "enviando",
        EstadoCampania.Enviada => "enviada",
        EstadoCampania.Detenida => "detenida",
        _ => "borrador",
    };
}
