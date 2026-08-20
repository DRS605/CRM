namespace Matchketing.Automatizacion.Dominio;

/// <summary>
/// Qué puede arrancar una regla. Son los **mismos eventos de dominio** que ya usan los webhooks, y eso
/// no es casualidad: significa que una regla se dispara igual si la oportunidad se gana desde el
/// tablero, desde el repaso o desde la API, sin que ninguno de esos sitios sepa que existen las reglas.
/// </summary>
public enum Disparador
{
    LeadCreado = 1,
    OportunidadGanada = 2,
    OportunidadPerdida = 3,
    OportunidadMovida = 4,
    ContactoBaja = 5,
}

/// <summary>
/// Sobre qué se puede poner una condición. Cerrado a propósito y corto: cada campo de más es una
/// consulta más en el camino caliente y una forma más de escribir una regla que no dispara nunca.
/// </summary>
public enum Campo
{
    Provincia = 1,
    Origen = 2,
    Sector = 3,

    /// <summary>Solo tiene valor en los disparadores de oportunidad.</summary>
    Importe = 4,

    /// <summary>Solo en <see cref="Disparador.OportunidadPerdida"/>.</summary>
    MotivoPerdida = 5,
}

public enum Operador
{
    Es = 1,
    NoEs = 2,
    Contiene = 3,
    MayorQue = 4,
    MenorQue = 5,
}

/// <summary>
/// Lo que una regla puede hacer. **Cuatro, y ninguna toca el embudo.**
///
/// Los candidatos que se han quedado fuera lo han hecho por el mismo motivo: mover una oportunidad de
/// etapa, cambiar el estado de un contacto o cerrar una venta son cosas que **cambian el embudo a
/// espaldas del comercial**, y un CRM que mueve tus oportunidades solo es un CRM del que dejas de
/// fiarte. Todo lo que hay aquí, o crea trabajo (una tarea), o deja constancia (una nota), o manda algo
/// que pasa por el permiso de la persona (un correo), o cambia de dueño algo que no tenía dueño claro.
///
/// Tampoco hay «avisar al móvil»: el módulo de [avisos] tiene una regla —**uno a la semana y nada
/// más**— y dejar que las reglas manden avisos la rompería el primer día.
/// </summary>
public enum TipoAccion
{
    /// <summary>Crear una tarea para el propietario, a los N días.</summary>
    CrearTarea = 1,

    /// <summary>Asignar el contacto a un comercial concreto.</summary>
    AsignarComercial = 2,

    /// <summary>Mandar un correo con una plantilla. **Pasa por el permiso**, como cualquier otro.</summary>
    MandarCorreo = 3,

    /// <summary>Apuntar una nota en la cronología. La más aburrida y la que más se usa.</summary>
    ApuntarNota = 4,
}

public static class Textos
{
    // Los nombres son los mismos que los eventos públicos de los webhooks, y a propósito: quien lee
    // «oportunidad.ganada» en una regla y en la carga útil de un webhook está viendo lo mismo.
    private static readonly (Disparador Valor, string Texto)[] Disparadores =
    [
        (Dominio.Disparador.LeadCreado, "lead.creado"),
        (Dominio.Disparador.OportunidadGanada, "oportunidad.ganada"),
        (Dominio.Disparador.OportunidadPerdida, "oportunidad.perdida"),
        (Dominio.Disparador.OportunidadMovida, "oportunidad.movida"),
        (Dominio.Disparador.ContactoBaja, "contacto.baja"),
    ];

    public static IReadOnlyList<Disparador> TodosLosDisparadores { get; } = Disparadores.Select(d => d.Valor).ToArray();

    public static string De(Disparador disparador) =>
        Disparadores.FirstOrDefault(d => d.Valor == disparador).Texto ?? "desconocido";

    public static Disparador? DisparadorDe(string? texto)
    {
        var encontrado = Disparadores.FirstOrDefault(d => d.Texto == texto);
        return encontrado.Texto is null ? null : encontrado.Valor;
    }

    /// <summary>Cómo se lee una condición en la pantalla. En castellano y sin jerga.</summary>
    public static string De(Campo campo) => campo switch
    {
        Campo.Provincia => "provincia",
        Campo.Origen => "origen",
        Campo.Sector => "sector",
        Campo.Importe => "importe",
        _ => "motivo de pérdida",
    };

    public static string De(Operador operador) => operador switch
    {
        Operador.Es => "es",
        Operador.NoEs => "no es",
        Operador.Contiene => "contiene",
        Operador.MayorQue => "es mayor que",
        _ => "es menor que",
    };

    public static string De(TipoAccion accion) => accion switch
    {
        TipoAccion.CrearTarea => "crear una tarea",
        TipoAccion.AsignarComercial => "asignárselo a un comercial",
        TipoAccion.MandarCorreo => "mandar un correo",
        _ => "apuntar una nota",
    };
}
