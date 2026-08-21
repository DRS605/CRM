using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Campos.Dominio;

/// <summary>
/// Sobre qué se puede definir un campo propio.
///
/// **Dos, y falta la oportunidad a propósito.** Un campo propio solo sirve si hay una pantalla donde se
/// ve y se rellena, y las oportunidades no tienen ficha: son tarjetas en un tablero. Añadir el ámbito a
/// la API sin la pantalla habría dejado un campo que se puede definir y no se puede rellenar, que es
/// justo la clase de media funcionalidad que este proyecto ya se ha encontrado cuatro veces.
/// </summary>
public enum Ambito
{
    Contacto = 1,
    Cuenta = 2,
}

/// <summary>
/// Qué tipo de dato guarda el campo. Cinco y cerrados.
///
/// La lista es corta porque cada tipo es una forma más de que el valor guardado y el tipo declarado se
/// separen. Con estos cinco se cubre lo que de verdad pide una pyme —número de póliza, potencia
/// contratada, fecha de la última revisión, si tiene mantenimiento, tipo de instalación— y ninguno
/// necesita una pantalla especial para rellenarlo.
/// </summary>
public enum TipoCampo
{
    Texto = 1,
    Numero = 2,
    Fecha = 3,
    SiNo = 4,

    /// <summary>Una de varias opciones cerradas.</summary>
    Lista = 5,
}

/// <summary>
/// Un campo que la empresa se define para sí misma: «número de póliza», «potencia contratada», «tipo
/// de instalación».
///
/// Existe porque **todo negocio tiene un dato que este CRM no tiene**, y cuando no cabe aquí se lleva
/// en una hoja aparte. A partir de ese momento la hoja es la verdad y el CRM es una copia vieja: es la
/// forma más común de que un CRM se abandone, y no se arregla con más campos de serie, porque el dato
/// que falta es distinto en cada negocio.
///
/// Hay una tensión con la tesis del producto —«no rellenes campos, el sistema te dice qué hacer»— y se
/// resuelve limitando **para qué** sirven: un campo propio se ve en la ficha, se rellena cuando hace
/// falta y sale en la exportación. Nunca es obligatorio, nunca aparece en Hoy y nunca se pregunta por
/// él en el repaso. El sistema no va a pedir que se rellene nada.
/// </summary>
public sealed class CampoPropio : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaNombre = 40;
    public const int LongitudMaximaOpcion = 40;

    /// <summary>
    /// Diez por ámbito.
    ///
    /// Es un techo bajo y es el punto. Un CRM con cuarenta campos propios por objeto es una base de
    /// datos con una interfaz encima: la ficha deja de leerse, nadie los rellena todos y los que se
    /// quedan a medias hacen dudar de los que sí están. Diez obliga a elegir, y elegir es lo que hace
    /// que los diez signifiquen algo.
    /// </summary>
    public const int MaximoPorAmbito = 10;

    /// <summary>Doce opciones en una lista. Más que eso no es una lista, es un texto libre disfrazado.</summary>
    public const int MaximoOpciones = 12;

    private CampoPropio(Guid id)
        : base(id, Guid.Empty)
    {
        Nombre = null!;
        Clave = null!;
        Opciones = [];
    }

    private CampoPropio(
        Guid id, Guid empresaId, Ambito ambito, string nombre, string clave,
        TipoCampo tipo, IReadOnlyList<string> opciones, int orden, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Ambito = ambito;
        Nombre = nombre;
        Clave = clave;
        Tipo = tipo;
        Opciones = opciones;
        Orden = orden;
        CreadoEn = ahora;
    }

    public Ambito Ambito { get; private set; }

    /// <summary>La etiqueta que se lee en la ficha. Se puede cambiar.</summary>
    public string Nombre { get; private set; }

    /// <summary>
    /// El nombre en forma de clave: minúsculas, sin acentos y con guiones bajos.
    ///
    /// **No cambia nunca**, ni al renombrar el campo. Es lo que sale en la cabecera del CSV y lo que
    /// usaría cualquier integración, así que si cambiara al corregir una tilde, la columna de un informe
    /// que alguien tiene montado desaparecería sin aviso. El nombre es para las personas; la clave, para
    /// las máquinas, y las máquinas no perdonan.
    /// </summary>
    public string Clave { get; private set; }

    /// <summary>
    /// **No se puede cambiar.** Un campo de texto que pasa a número deja sin sentido todos los valores
    /// que ya tenía guardados, y convertirlos automáticamente sería adivinar. Para cambiar de tipo se
    /// borra el campo y se crea otro, que además obliga a decidir qué pasa con lo que había.
    /// </summary>
    public TipoCampo Tipo { get; private set; }

    /// <summary>Las opciones, si es una lista. Vacío en los demás tipos.</summary>
    public IReadOnlyList<string> Opciones { get; private set; }

    /// <summary>En qué orden se enseñan en la ficha.</summary>
    public int Orden { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public static Resultado<CampoPropio> Crear(
        Guid empresaId, Ambito ambito, string? nombre, TipoCampo tipo,
        IReadOnlyList<string>? opciones, int orden, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (!Enum.IsDefined(ambito))
        {
            return Resultado.Fallo<CampoPropio>(Error.Validacion(
                "campo.ambito_invalido", "Un campo propio se define sobre un contacto o sobre una cuenta."));
        }

        if (!Enum.IsDefined(tipo))
        {
            return Resultado.Fallo<CampoPropio>(Error.Validacion("campo.tipo_invalido", "Ese tipo no existe."));
        }

        var limpio = Nombrar(nombre);
        if (limpio.Fallido)
        {
            return Resultado.Fallo<CampoPropio>(limpio.Error!);
        }

        var clave = ClaveDe(limpio.Valor);
        if (clave.Length == 0)
        {
            // «???» o «...» dan una clave vacía. El nombre parecía válido y la clave no lo es, así que
            // hay que decirlo aquí y no dejar una fila con clave vacía que choque con la siguiente.
            return Resultado.Fallo<CampoPropio>(Error.Validacion(
                "campo.nombre_sin_letras", "El nombre tiene que llevar alguna letra o algún número."));
        }

        var listas = Validar(tipo, opciones);
        return listas.Fallido
            ? Resultado.Fallo<CampoPropio>(listas.Error!)
            : Resultado.Ok(new CampoPropio(
                Guid.NewGuid(), empresaId, ambito, limpio.Valor, clave, tipo, listas.Valor, orden, reloj.AhoraUtc));
    }

    /// <summary>Cambia la etiqueta. La clave se queda como estaba, y eso es lo importante.</summary>
    public Resultado Renombrar(string? nombre)
    {
        var limpio = Nombrar(nombre);
        if (limpio.Fallido)
        {
            return Resultado.Fallo(limpio.Error!);
        }

        Nombre = limpio.Valor;
        return Resultado.Ok();
    }

    /// <summary>
    /// Cambia las opciones de una lista.
    ///
    /// Quien llama tiene que haber comprobado antes que ningún valor guardado se queda fuera: eso vive
    /// en el servicio, porque hace falta mirar la tabla de valores y el dominio no la conoce. Aquí solo
    /// se comprueba que la lista nueva es una lista válida.
    /// </summary>
    public Resultado CambiarOpciones(IReadOnlyList<string>? opciones)
    {
        if (Tipo != TipoCampo.Lista)
        {
            return Resultado.Fallo(Error.Validacion(
                "campo.no_es_lista", "Solo un campo de lista tiene opciones."));
        }

        var listas = Validar(Tipo, opciones);
        if (listas.Fallido)
        {
            return listas;
        }

        Opciones = listas.Valor;
        return Resultado.Ok();
    }

    public void Colocar(int orden) => Orden = Math.Max(0, orden);

    /// <summary>¿Es <paramref name="valor"/> una de las opciones? Comparando sin acentos ni mayúsculas.</summary>
    public bool EsOpcion(string valor) =>
        Opciones.Any(o => Castellano.SinAcentos(o) == Castellano.SinAcentos(valor ?? string.Empty));

    /// <summary>
    /// El nombre convertido en clave. Minúsculas, sin acentos, y todo lo que no sea letra o número pasa
    /// a guion bajo, sin dejar dos seguidos ni empezar o acabar con uno.
    /// </summary>
    public static string ClaveDe(string nombre)
    {
        ArgumentNullException.ThrowIfNull(nombre);

        var sinAcentos = Castellano.SinAcentos(nombre);
        var sb = new System.Text.StringBuilder(sinAcentos.Length);

        foreach (var c in sinAcentos)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '_')
            {
                sb.Append('_');
            }
        }

        return sb.ToString().TrimEnd('_');
    }

    private static Resultado<string> Nombrar(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Resultado.Fallo<string>(Error.Validacion("campo.sin_nombre", "El campo necesita un nombre."));
        }

        var limpio = nombre.Trim();
        return limpio.Length > LongitudMaximaNombre
            ? Resultado.Fallo<string>(Error.Validacion(
                "campo.nombre_largo", $"El nombre no puede pasar de {LongitudMaximaNombre} caracteres."))
            : Resultado.Ok(limpio);
    }

    /// <summary>
    /// Las opciones de una lista, limpias. Fuera de una lista tienen que venir vacías: aceptarlas y
    /// guardarlas dejaría un campo de texto con opciones que nadie usa y que confunden al leer la fila.
    /// </summary>
    private static Resultado<IReadOnlyList<string>> Validar(TipoCampo tipo, IReadOnlyList<string>? opciones)
    {
        var limpias = (opciones ?? [])
            .Select(o => (o ?? string.Empty).Trim())
            .Where(o => o.Length > 0)
            .ToList();

        if (tipo != TipoCampo.Lista)
        {
            return limpias.Count > 0
                ? Resultado.Fallo<IReadOnlyList<string>>(Error.Validacion(
                    "campo.opciones_sin_lista", "Solo un campo de lista lleva opciones."))
                : Resultado.Ok<IReadOnlyList<string>>([]);
        }

        // Dos es el mínimo: una lista de una opción no es una elección, y de cero no se puede rellenar.
        if (limpias.Count < 2)
        {
            return Resultado.Fallo<IReadOnlyList<string>>(Error.Validacion(
                "campo.pocas_opciones", "Una lista necesita al menos dos opciones."));
        }

        if (limpias.Count > MaximoOpciones)
        {
            return Resultado.Fallo<IReadOnlyList<string>>(Error.Validacion(
                "campo.demasiadas_opciones",
                $"Una lista no puede pasar de {MaximoOpciones} opciones. Con más, es un texto libre."));
        }

        if (limpias.Any(o => o.Length > LongitudMaximaOpcion))
        {
            return Resultado.Fallo<IReadOnlyList<string>>(Error.Validacion(
                "campo.opcion_larga", $"Una opción no puede pasar de {LongitudMaximaOpcion} caracteres."));
        }

        // Repetidas comparando sin acentos ni mayúsculas: «Gas» y «gas» en el mismo desplegable son un
        // error de quien lo escribió, y dejarlas pasar hace que el dato no se pueda agrupar nunca.
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in limpias)
        {
            if (!vistas.Add(Castellano.SinAcentos(o)))
            {
                return Resultado.Fallo<IReadOnlyList<string>>(Error.Validacion(
                    "campo.opcion_repetida", $"«{o}» está dos veces en la lista."));
            }
        }

        return Resultado.Ok<IReadOnlyList<string>>(limpias);
    }
}
