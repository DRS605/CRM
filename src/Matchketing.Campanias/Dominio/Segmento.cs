using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Campanias.Dominio;

/// <summary>
/// Un filtro guardado con nombre: «clientes de Valencia», «leads del formulario sin contestar».
///
/// **No es una lista de contactos.** No guarda identificadores de nadie: guarda las condiciones, y los
/// contactos se buscan cada vez que se usa. Esa es toda la diferencia con una lista importada, y no es
/// un detalle de implementación:
///
/// · Una lista de 1.200 correos subida en marzo tiene en octubre gente que se fue de la empresa, gente
///   que ya compró y gente que se dio de baja. Nadie la limpia, porque limpiarla es trabajo.
/// · Un segmento no se puede quedar desfasado, porque no contiene nada que pueda desfasarse.
/// · Y cuando alguien pide que le borren sus datos, desaparece de todos los segmentos sin que nadie
///   tenga que acordarse de nada. Con listas, un borrado del artículo 17 obliga a recorrerlas todas.
///
/// Lo que sí se congela es la **audiencia de una campaña concreta**, en el momento de lanzarla. Ver
/// <see cref="Campania"/>: son dos cosas distintas y por eso son dos tablas.
/// </summary>
public sealed class Segmento : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaNombre = 60;

    private Segmento(Guid id)
        : base(id, Guid.Empty)
    {
        Nombre = null!;
        Criterios = CriteriosSegmento.Vacios;
    }

    private Segmento(Guid id, Guid empresaId, string nombre, CriteriosSegmento criterios, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Nombre = nombre;
        Criterios = criterios;
        CreadoEn = ahora;
        ActualizadoEn = ahora;
    }

    public string Nombre { get; private set; }

    public CriteriosSegmento Criterios { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public DateTimeOffset ActualizadoEn { get; private set; }

    public static Resultado<Segmento> Crear(
        Guid empresaId, string? nombre, CriteriosSegmento criterios, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(criterios);
        ArgumentNullException.ThrowIfNull(reloj);

        var limpio = Nombrar(nombre);
        if (limpio.Fallido)
        {
            return Resultado.Fallo<Segmento>(limpio.Error!);
        }

        // Los criterios llegan ya validados: se construyen con `CriteriosSegmento.Crear`, que es donde
        // vive la regla de «al menos uno». Aquí se comprueba otra vez porque un `record` se puede
        // construir con `new` y el compilador no lo impide, y un segmento sin criterios que llegue hasta
        // la base de datos es una campaña a toda la base de datos esperando su turno.
        if (criterios.Cuantos == 0)
        {
            return Resultado.Fallo<Segmento>(Error.Validacion(
                "segmento.sin_criterios", "Un segmento tiene que decir a quién apunta."));
        }

        return Resultado.Ok(new Segmento(Guid.NewGuid(), empresaId, limpio.Valor, criterios, reloj.AhoraUtc));
    }

    public Resultado Cambiar(string? nombre, CriteriosSegmento criterios, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(criterios);
        ArgumentNullException.ThrowIfNull(reloj);

        var limpio = Nombrar(nombre);
        if (limpio.Fallido)
        {
            return Resultado.Fallo(limpio.Error!);
        }

        if (criterios.Cuantos == 0)
        {
            return Resultado.Fallo(Error.Validacion(
                "segmento.sin_criterios", "Un segmento tiene que decir a quién apunta."));
        }

        Nombre = limpio.Valor;
        Criterios = criterios;
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    private static Resultado<string> Nombrar(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Resultado.Fallo<string>(Error.Validacion(
                "segmento.sin_nombre", "El segmento necesita un nombre."));
        }

        var limpio = nombre.Trim();
        return limpio.Length > LongitudMaximaNombre
            ? Resultado.Fallo<string>(Error.Validacion(
                "segmento.nombre_largo", $"El nombre no puede pasar de {LongitudMaximaNombre} caracteres."))
            : Resultado.Ok(limpio);
    }
}
