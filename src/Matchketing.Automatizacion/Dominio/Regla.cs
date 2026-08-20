using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Automatizacion.Dominio;

/// <summary>
/// «Si pasa X, haz Y.» Un disparador, hasta tres condiciones que se cumplen todas, y hasta cuatro cosas
/// que hacer.
///
/// **Sin lienzo de ramas, y eso es la decisión de producto.** En cuanto hay ramas hay que dibujar, y en
/// cuanto hay que dibujar hace falta alguien que sepa dibujarlo: es la funcionalidad que convierte una
/// herramienta que se entiende sola en una que necesita un consultor. Una regla de este módulo se lee de
/// un tirón y en castellano —«si entra un lead y la provincia es Valencia, crea la tarea Llamar para
/// hoy»— y quien necesite dos caminos hace dos reglas.
///
/// Nace **apagada**. Una regla que empieza a disparar en el mismo segundo en que se guarda no da tiempo
/// a leerla, y lo que hace no se puede deshacer: las tareas creadas están creadas y los correos mandados
/// están mandados.
/// </summary>
public sealed class Regla : RaizAgregadoEmpresa<Guid>
{
    public const int MaximoCondiciones = 3;
    public const int MaximoAcciones = 4;
    public const int LongitudMaximaNombre = 100;

    private readonly List<Condicion> condiciones = [];
    private readonly List<Accion> acciones = [];

    private Regla(Guid id)
        : base(id, Guid.Empty) => Nombre = null!;

    private Regla(
        Guid id, Guid empresaId, string nombre, Disparador disparador,
        IEnumerable<Condicion> condiciones, IEnumerable<Accion> acciones, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Nombre = nombre;
        Disparador = disparador;
        this.condiciones.AddRange(condiciones);
        this.acciones.AddRange(acciones);
        CreadaEn = ahora;
        Activa = false;
    }

    public string Nombre { get; private set; }

    public Disparador Disparador { get; private set; }

    public IReadOnlyList<Condicion> Condiciones => condiciones;

    public IReadOnlyList<Accion> Acciones => acciones;

    /// <summary>Apagada al nacer. Se enciende a mano después de leerla.</summary>
    public bool Activa { get; private set; }

    public DateTimeOffset CreadaEn { get; private set; }

    public DateTimeOffset? UltimaVezEn { get; private set; }

    public int Veces { get; private set; }

    public static Resultado<Regla> Crear(
        Guid empresaId, string? nombre, Disparador disparador,
        IReadOnlyCollection<Condicion>? condiciones, IReadOnlyCollection<Accion>? acciones, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var comprobado = Comprobar(nombre, disparador, condiciones, acciones);
        if (comprobado.Fallido)
        {
            return Resultado.Fallo<Regla>(comprobado.Error!);
        }

        return Resultado.Ok(new Regla(
            Guid.NewGuid(), empresaId, nombre!.Trim(), disparador, condiciones!, acciones!, reloj.AhoraUtc));
    }

    public Resultado Cambiar(
        string? nombre, Disparador disparador,
        IReadOnlyCollection<Condicion>? condiciones, IReadOnlyCollection<Accion>? acciones)
    {
        var comprobado = Comprobar(nombre, disparador, condiciones, acciones);
        if (comprobado.Fallido)
        {
            return comprobado;
        }

        Nombre = nombre!.Trim();
        Disparador = disparador;
        this.condiciones.Clear();
        this.condiciones.AddRange(condiciones!);
        this.acciones.Clear();
        this.acciones.AddRange(acciones!);

        // Cambiar una regla la **apaga**. Es lo mismo que al crearla y por lo mismo: lo que hace no se
        // deshace, y un cambio a medias que empiece a disparar mientras se está editando es la forma más
        // rápida de mandar cien correos que nadie quería.
        Activa = false;
        return Resultado.Ok();
    }

    public void Encender() => Activa = true;

    public void Apagar() => Activa = false;

    public void Disparada(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        Veces++;
        UltimaVezEn = reloj.AhoraUtc;
    }

    /// <summary>
    /// ¿Le toca a este sujeto? Todas las condiciones o ninguna: no hay «o». Una regla sin condiciones
    /// se cumple siempre, y eso es válido —«cuando entre un lead, mándale el acuse de recibo»—.
    /// </summary>
    public bool Aplica(Disparador disparador, Hechos hechos) =>
        Activa && Disparador == disparador && condiciones.All(c => c.Cumple(hechos));

    /// <summary>La regla entera en una frase, en castellano. Es lo que se enseña en la pantalla.</summary>
    public string Leer()
    {
        var si = condiciones.Count == 0
            ? $"Si pasa «{Textos.De(Disparador)}»"
            : $"Si pasa «{Textos.De(Disparador)}» y {string.Join(" y ", condiciones.Select(c => c.Leer()))}";

        return $"{si}, entonces {string.Join(", y ", acciones.Select(a => a.Leer()))}.";
    }

    private static Resultado Comprobar(
        string? nombre, Disparador disparador,
        IReadOnlyCollection<Condicion>? condiciones, IReadOnlyCollection<Accion>? acciones)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > LongitudMaximaNombre)
        {
            return Resultado.Fallo(Error.Validacion(
                "regla.nombre_invalido", $"Ponle un nombre de hasta {LongitudMaximaNombre} caracteres."));
        }

        if (condiciones is { Count: > MaximoCondiciones })
        {
            return Resultado.Fallo(Error.Validacion(
                "regla.demasiadas_condiciones",
                $"Como mucho {MaximoCondiciones} condiciones. Si necesitas más, probablemente necesitas dos reglas."));
        }

        // Sin acciones no hay regla. Una regla que no hace nada es una fila que parece que hace algo.
        if (acciones is null || acciones.Count == 0)
        {
            return Resultado.Fallo(Error.Validacion(
                "regla.sin_acciones", "Dile qué tiene que hacer."));
        }

        if (acciones.Count > MaximoAcciones)
        {
            return Resultado.Fallo(Error.Validacion(
                "regla.demasiadas_acciones", $"Como mucho {MaximoAcciones} acciones."));
        }

        foreach (var condicion in condiciones ?? [])
        {
            var valida = condicion.Validar();
            if (valida.Fallido)
            {
                return valida;
            }

            // El importe y el motivo de pérdida no existen cuando el disparador es de contacto: una regla
            // así no se cumpliría nunca y no hay forma de darse cuenta mirándola.
            var deOportunidad = disparador is Disparador.OportunidadGanada or Disparador.OportunidadPerdida
                or Disparador.OportunidadMovida;

            if (!deOportunidad && condicion.Campo is Campo.Importe or Campo.MotivoPerdida)
            {
                return Resultado.Fallo(Error.Validacion(
                    "regla.condicion_imposible",
                    $"«{Textos.De(condicion.Campo)}» no existe cuando el disparador es «{Textos.De(disparador)}»: " +
                    "esa regla no se cumpliría nunca."));
            }

            if (condicion.Campo == Campo.MotivoPerdida && disparador != Disparador.OportunidadPerdida)
            {
                return Resultado.Fallo(Error.Validacion(
                    "regla.condicion_imposible",
                    "El motivo de pérdida solo existe cuando se pierde una oportunidad."));
            }
        }

        foreach (var accion in acciones)
        {
            var valida = accion.Validar();
            if (valida.Fallido)
            {
                return valida;
            }
        }

        return Resultado.Ok();
    }
}
