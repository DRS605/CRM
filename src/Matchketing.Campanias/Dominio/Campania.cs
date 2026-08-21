using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Campanias.Dominio;

public enum EstadoCampania
{
    /// <summary>Escrita y no lanzada. Se puede editar y borrar. No ha salido nada.</summary>
    Borrador = 1,

    /// <summary>Lanzada: la audiencia ya está congelada y los correos se van encolando por lotes.</summary>
    Enviando = 2,

    /// <summary>Ya se ha decidido qué pasa con cada destinatario. No queda nadie pendiente.</summary>
    Enviada = 3,

    /// <summary>
    /// Alguien la paró a mitad. Lo que ya estaba encolado sale igual —un correo encolado no se puede
    /// recoger—; lo que quedaba pendiente se descarta y queda escrito por qué.
    /// </summary>
    Detenida = 4,
}

/// <summary>
/// Un envío a un segmento, hecho una vez.
///
/// La decisión de fondo, y la que separa esto de una plataforma de envío masivo: **una campaña no
/// manda correos, encola correos de uno en uno por el mismo camino que un correo escrito a mano**. El
/// permiso de cada persona se comprueba dos veces igual que siempre —al encolar y justo antes de que
/// salga— y quien no lo tenga se queda fuera con el motivo escrito. No hay un camino rápido para
/// campañas, porque un camino rápido para campañas es exactamente el agujero por el que se manda
/// publicidad a quien no la ha pedido.
///
/// De ahí salen tres cosas que aquí son normales y en una herramienta de mailing serían raras:
///
/// 1. Lanzar una campaña **no promete un número**. Promete que se va a intentar con cada uno de los
///    que estaban en el segmento, y luego dice a cuántos se llegó y a cuántos no y por qué.
/// 2. La audiencia se **congela al lanzar**. Un contacto creado esta tarde no se cuela en la campaña
///    de esta mañana. Sin eso, «¿a quién le llegó esto?» no tiene respuesta.
/// 3. La plantilla tiene que ser **comercial**. Mandar a quinientas personas una plantilla escrita para
///    atender una solicitud es mentir sobre la base legal del envío, y además se nota al leerla.
/// </summary>
public sealed class Campania : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaNombre = 80;

    /// <summary>
    /// El techo de destinatarios de una sola campaña.
    ///
    /// No es una limitación de la base de datos: es el tamaño a partir del cual esto ya no es
    /// «escribirle a mis clientes» sino mailing masivo, y para eso hacen falta cosas que aquí no hay a
    /// propósito —reputación de IP, calentamiento de dominio, gestión de rebotes a escala—. Preferimos
    /// decir «hasta aquí llegamos» que hacerlo mal y quemar el dominio de correo del cliente.
    /// </summary>
    public const int MaximoDestinatarios = 2000;

    private Campania(Guid id)
        : base(id, Guid.Empty)
    {
        Nombre = null!;
    }

    private Campania(Guid id, Guid empresaId, string nombre, Guid segmentoId, Guid plantillaId, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Nombre = nombre;
        SegmentoId = segmentoId;
        PlantillaId = plantillaId;
        Estado = EstadoCampania.Borrador;
        CreadaEn = ahora;
    }

    public string Nombre { get; private set; }

    public Guid SegmentoId { get; private set; }

    /// <summary>La plantilla de correo con la que se manda. Tiene que ser de las comerciales.</summary>
    public Guid PlantillaId { get; private set; }

    public EstadoCampania Estado { get; private set; }

    public DateTimeOffset CreadaEn { get; private set; }

    public DateTimeOffset? LanzadaEn { get; private set; }

    /// <summary>
    /// Quién la lanzó. Los correos salen **en su nombre**, con su firma en el hueco `{{comercial}}`, y
    /// es la persona a la que le van a contestar. Que una campaña la firme alguien y no «el sistema» no
    /// es cosmética: es lo que hace que alguien se lo piense antes de darle al botón.
    /// </summary>
    public Guid? LanzadaPor { get; private set; }

    public DateTimeOffset? TerminadaEn { get; private set; }

    /// <summary>Cuántos había en el segmento al lanzar. No cambia después.</summary>
    public int Destinatarios { get; private set; }

    public int Encolados { get; private set; }

    public int Excluidos { get; private set; }

    /// <summary>
    /// El segmento dicho en una frase, tal como estaba **al lanzar**. Se copia aquí a propósito: el
    /// segmento se puede editar o borrar después, y entonces la campaña se quedaría sin poder explicar
    /// a quién apuntaba. Es el mismo motivo por el que cada correo guarda su propio texto.
    /// </summary>
    public string? SegmentoAlLanzar { get; private set; }

    public int Pendientes => Math.Max(0, Destinatarios - Encolados - Excluidos);

    /// <summary>Solo un borrador se puede tocar. Lo lanzado es historia.</summary>
    public bool EsBorrador => Estado == EstadoCampania.Borrador;

    public static Resultado<Campania> Crear(
        Guid empresaId, string? nombre, Guid segmentoId, Guid plantillaId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var limpio = Nombrar(nombre);
        if (limpio.Fallido)
        {
            return Resultado.Fallo<Campania>(limpio.Error!);
        }

        if (segmentoId == Guid.Empty)
        {
            return Resultado.Fallo<Campania>(Error.Validacion(
                "campania.sin_segmento", "Hay que elegir a quién se le manda."));
        }

        if (plantillaId == Guid.Empty)
        {
            return Resultado.Fallo<Campania>(Error.Validacion(
                "campania.sin_plantilla", "Hay que elegir qué se le manda."));
        }

        return Resultado.Ok(new Campania(
            Guid.NewGuid(), empresaId, limpio.Valor, segmentoId, plantillaId, reloj.AhoraUtc));
    }

    public Resultado Cambiar(string? nombre, Guid segmentoId, Guid plantillaId)
    {
        if (!EsBorrador)
        {
            return Resultado.Fallo(Error.Conflicto(
                "campania.ya_lanzada", "Una campaña lanzada no se edita. Duplícala si quieres cambiar algo."));
        }

        var limpio = Nombrar(nombre);
        if (limpio.Fallido)
        {
            return Resultado.Fallo(limpio.Error!);
        }

        if (segmentoId == Guid.Empty || plantillaId == Guid.Empty)
        {
            return Resultado.Fallo(Error.Validacion(
                "campania.incompleta", "Hay que decir a quién y qué se le manda."));
        }

        Nombre = limpio.Valor;
        SegmentoId = segmentoId;
        PlantillaId = plantillaId;
        return Resultado.Ok();
    }

    /// <summary>
    /// La lanza. <paramref name="destinatarios"/> es el tamaño de la audiencia ya resuelta y congelada.
    ///
    /// Se rechaza una audiencia vacía, y eso es deliberado: una campaña lanzada a cero personas queda en
    /// la lista como «enviada» y nadie vuelve a mirarla. Es mejor que falle en la cara de quien la
    /// lanza, mientras todavía se acuerda de qué segmento eligió.
    /// </summary>
    public Resultado Lanzar(Guid usuarioId, int destinatarios, string? segmentoEnPalabras, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (!EsBorrador)
        {
            return Resultado.Fallo(Error.Conflicto(
                "campania.ya_lanzada", "Esta campaña ya se lanzó."));
        }

        if (usuarioId == Guid.Empty)
        {
            return Resultado.Fallo(Error.NoAutorizado(
                "campania.sin_firma", "Una campaña la tiene que lanzar alguien."));
        }

        if (destinatarios <= 0)
        {
            return Resultado.Fallo(Error.Validacion(
                "campania.segmento_vacio",
                "Ahora mismo no hay nadie en ese segmento. Revisa los criterios antes de lanzarla."));
        }

        if (destinatarios > MaximoDestinatarios)
        {
            return Resultado.Fallo(Error.Validacion(
                "campania.demasiados",
                $"Ese segmento tiene {destinatarios} contactos y el máximo por campaña es {MaximoDestinatarios}. " +
                "Acota el segmento y lánzala en varias."));
        }

        Estado = EstadoCampania.Enviando;
        LanzadaEn = reloj.AhoraUtc;
        LanzadaPor = usuarioId;
        Destinatarios = destinatarios;
        SegmentoAlLanzar = string.IsNullOrWhiteSpace(segmentoEnPalabras) ? null : segmentoEnPalabras.Trim();
        return Resultado.Ok();
    }

    /// <summary>Uno más resuelto. Se llama una vez por destinatario, cuando se decide qué pasa con él.</summary>
    public void Anotar(bool encolado, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (encolado)
        {
            Encolados++;
        }
        else
        {
            Excluidos++;
        }

        // La campaña se cierra sola cuando no queda nadie pendiente. No hace falta que nadie la cierre,
        // y por eso no hay un método `Terminar` público: un estado final que depende de que alguien
        // pulse algo es un estado que se queda a medias.
        if (Estado == EstadoCampania.Enviando && Pendientes == 0)
        {
            Estado = EstadoCampania.Enviada;
            TerminadaEn = reloj.AhoraUtc;
        }
    }

    /// <summary>
    /// La para. Lo ya encolado **sale igual**: un correo en el buzón de salida está a un minuto de salir
    /// y prometer que se puede recoger sería mentir. Lo que hace es que no se encole nada más.
    /// </summary>
    public Resultado Detener(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (Estado != EstadoCampania.Enviando)
        {
            return Resultado.Fallo(Error.Conflicto(
                "campania.no_en_marcha", "Solo se puede detener una campaña que esté enviando."));
        }

        Estado = EstadoCampania.Detenida;
        TerminadaEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>Los que quedaron sin resolver al detenerla cuentan como excluidos, no como pendientes.</summary>
    public void DescartarPendientes(int cuantos)
    {
        if (cuantos > 0)
        {
            Excluidos += cuantos;
        }
    }

    /// <summary>Ya no hay nada que hacer con ella: ni encolar ni detener.</summary>
    public bool Cerrada => Estado is EstadoCampania.Enviada or EstadoCampania.Detenida;

    private static Resultado<string> Nombrar(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Resultado.Fallo<string>(Error.Validacion(
                "campania.sin_nombre", "La campaña necesita un nombre."));
        }

        var limpio = nombre.Trim();
        return limpio.Length > LongitudMaximaNombre
            ? Resultado.Fallo<string>(Error.Validacion(
                "campania.nombre_largo", $"El nombre no puede pasar de {LongitudMaximaNombre} caracteres."))
            : Resultado.Ok(limpio);
    }
}
