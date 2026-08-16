using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Match.Dominio;

/// <summary>
/// Cosas que le pasan a un contacto y que dicen algo sobre su interés. Los pesos no son opinión
/// suelta: responder pesa más que abrir, y abrir pesa más que pasar por una página.
/// </summary>
public enum TipoSenal
{
    FormularioEnviado = 1,
    RespuestaCorreo = 2,
    ReunionRealizada = 3,
    LlamadaContestada = 4,
    OportunidadCreada = 5,
    ClicEnlace = 6,
    CorreoAbierto = 7,
    VisitaWeb = 8,
}

/// <summary>Peso bruto y tope diario de cada señal.</summary>
public static class PesosSenal
{
    /// <summary>
    /// Días en los que una señal pierde la mitad de su fuerza. Siete: un lead caliente de hace un
    /// mes está frío, y el sistema tiene que saberlo.
    /// </summary>
    public const double SemividaDias = 7.0;

    /// <summary>Penalización por no tener ninguna actividad en un mes.</summary>
    public const int PenalizacionInactividad = -20;

    public const int DiasParaInactividad = 30;

    public static int Peso(TipoSenal tipo) => tipo switch
    {
        TipoSenal.FormularioEnviado => 35,
        TipoSenal.RespuestaCorreo => 30,
        TipoSenal.ReunionRealizada => 30,
        TipoSenal.LlamadaContestada => 25,
        TipoSenal.OportunidadCreada => 20,
        TipoSenal.ClicEnlace => 15,
        TipoSenal.CorreoAbierto => 8,
        TipoSenal.VisitaWeb => 6,
        _ => 0,
    };

    /// <summary>
    /// Cuántas veces al día cuenta como mucho. **Todas las señales tienen tope**: sin él, un robot
    /// que abre el mismo correo veinte veces —o una importación que crea diez oportunidades de
    /// golpe— convertiría a un contacto cualquiera en el más caliente de la lista.
    /// </summary>
    public static int TopeDiario(TipoSenal tipo) => tipo switch
    {
        TipoSenal.VisitaWeb => 5,
        TipoSenal.ClicEnlace => 3,
        TipoSenal.CorreoAbierto => 3,
        TipoSenal.RespuestaCorreo => 3,
        TipoSenal.LlamadaContestada => 2,
        TipoSenal.ReunionRealizada => 2,
        TipoSenal.FormularioEnviado => 2,

        // Abrir cinco oportunidades en un día es un solo hecho de interés, no cinco.
        TipoSenal.OportunidadCreada => 1,
        _ => 1,
    };

    public static string Describir(TipoSenal tipo) => tipo switch
    {
        TipoSenal.FormularioEnviado => "rellenó tu formulario",
        TipoSenal.RespuestaCorreo => "respondió a tu correo",
        TipoSenal.ReunionRealizada => "tuvisteis una reunión",
        TipoSenal.LlamadaContestada => "cogió el teléfono",
        TipoSenal.OportunidadCreada => "abrió una oportunidad contigo",
        TipoSenal.ClicEnlace => "pinchó un enlace tuyo",
        TipoSenal.CorreoAbierto => "abrió tu correo",
        TipoSenal.VisitaWeb => "visitó tu web",
        _ => "hizo algo",
    };
}

/// <summary>Una señal registrada. Es un hecho con fecha: no se edita ni se borra.</summary>
public sealed class Senal : RaizAgregadoEmpresa<Guid>
{
    private Senal(Guid id)
        : base(id, Guid.Empty)
    {
    }

    private Senal(Guid id, Guid empresaId, Guid contactoId, TipoSenal tipo, DateTimeOffset ocurridaEn)
        : base(id, empresaId)
    {
        ContactoId = contactoId;
        Tipo = tipo;
        OcurridaEn = ocurridaEn;
    }

    public Guid ContactoId { get; private set; }

    public TipoSenal Tipo { get; private set; }

    public DateTimeOffset OcurridaEn { get; private set; }

    public static Resultado<Senal> Crear(Guid empresaId, Guid contactoId, TipoSenal tipo, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        return !Enum.IsDefined(tipo)
            ? Resultado.Fallo<Senal>(Error.Validacion("senal.tipo_invalido", "Ese tipo de señal no existe."))
            : Resultado.Ok(new Senal(Guid.NewGuid(), empresaId, contactoId, tipo, reloj.AhoraUtc));
    }
}
