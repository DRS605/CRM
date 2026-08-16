using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Contactos.Dominio;

/// <summary>
/// Una cosa que ha pasado con un contacto. Es **append-only**: no se edita ni se borra, porque una
/// conversación es un hecho, no un campo (invariante C5). Todas juntas forman la cronología, que es
/// el corazón de la ficha.
/// </summary>
public sealed class Actividad : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaCuerpo = 4000;

    private Actividad(Guid id)
        : base(id, Guid.Empty) => Cuerpo = null!;

    private Actividad(Guid id, Guid empresaId, Guid contactoId, TipoActividad tipo, SentidoActividad sentido, string cuerpo, ResultadoLlamada? resultado, Guid? autorId, DateTimeOffset ocurridaEn)
        : base(id, empresaId)
    {
        ContactoId = contactoId;
        Tipo = tipo;
        Sentido = sentido;
        Cuerpo = cuerpo;
        Resultado = resultado;
        AutorId = autorId;
        OcurridaEn = ocurridaEn;
    }

    public Guid ContactoId { get; private set; }

    public TipoActividad Tipo { get; private set; }

    public SentidoActividad Sentido { get; private set; }

    public string Cuerpo { get; private set; }

    /// <summary>Solo en las llamadas: qué pasó, en un clic.</summary>
    public ResultadoLlamada? Resultado { get; private set; }

    /// <summary>Quién la registró. Nulo cuando la escribe el propio sistema.</summary>
    public Guid? AutorId { get; private set; }

    public DateTimeOffset OcurridaEn { get; private set; }

    public static Resultado<Actividad> Crear(Guid empresaId, Guid contactoId, TipoActividad tipo, SentidoActividad sentido, string? cuerpo, Guid? autorId, IReloj reloj, ResultadoLlamada? resultado = null)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (string.IsNullOrWhiteSpace(cuerpo))
        {
            return Nucleo.Resultados.Resultado.Fallo<Actividad>(
                Error.Validacion("actividad.cuerpo_vacio", "La actividad necesita un texto."));
        }

        if (cuerpo.Trim().Length > LongitudMaximaCuerpo)
        {
            return Nucleo.Resultados.Resultado.Fallo<Actividad>(
                Error.Validacion("actividad.cuerpo_largo", "El texto de la actividad es demasiado largo."));
        }

        if (tipo == TipoActividad.Llamada && resultado is null)
        {
            return Nucleo.Resultados.Resultado.Fallo<Actividad>(
                Error.Validacion("actividad.llamada_sin_resultado", "Una llamada necesita su resultado."));
        }

        return Nucleo.Resultados.Resultado.Ok(new Actividad(
            Guid.NewGuid(), empresaId, contactoId, tipo, sentido, cuerpo.Trim(), resultado, autorId, reloj.AhoraUtc));
    }

    /// <summary>
    /// Reasigna la actividad al contacto superviviente de una fusión. Es lo único que se le puede
    /// cambiar a una actividad, y existe para que fusionar no pierda historia (invariante C4).
    /// </summary>
    public void ReasignarA(Guid contactoId) => ContactoId = contactoId;
}
