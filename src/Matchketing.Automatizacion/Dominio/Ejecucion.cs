using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Automatizacion.Dominio;

/// <summary>
/// Una vez que una regla actuó sobre alguien. Cumple **dos** funciones y las dos importan.
///
/// La primera es que una regla actúa **una sola vez por sujeto, para siempre**. Sin esto, un evento que
/// se reprocese —un reintento, un guardado que pasa dos veces— crearía la tarea dos veces o mandaría el
/// correo dos veces, y eso no se puede deshacer. La garantía la da un índice único en la base
/// (`regla_id`, `sujeto_id`), no un `if`: un `if` no protege de dos procesos a la vez.
///
/// La segunda es que **se puede ver qué ha hecho una regla**. Un comercial que se encuentra una tarea
/// que no creó tiene que poder averiguar de dónde salió, y quien escribió la regla tiene que poder
/// comprobar que hace lo que cree. Una automatización que no se puede auditar es una automatización que
/// se acaba apagando por si acaso.
/// </summary>
public sealed class Ejecucion : RaizAgregadoEmpresa<Guid>
{
    private Ejecucion(Guid id)
        : base(id, Guid.Empty) => QueHizo = null!;

    private Ejecucion(Guid id, Guid empresaId, Guid reglaId, Guid sujetoId, Guid? contactoId, string queHizo, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        ReglaId = reglaId;
        SujetoId = sujetoId;
        ContactoId = contactoId;
        QueHizo = queHizo;
        CuandoEn = ahora;
    }

    public Guid ReglaId { get; private set; }

    /// <summary>Sobre quién actuó: el contacto o la oportunidad, según el disparador.</summary>
    public Guid SujetoId { get; private set; }

    /// <summary>El contacto al que afecta, para poder enseñarlo en su ficha. Puede coincidir con el sujeto.</summary>
    public Guid? ContactoId { get; private set; }

    /// <summary>Qué hizo, en castellano y ya escrito. Es lo que se lee en la pantalla.</summary>
    public string QueHizo { get; private set; }

    public DateTimeOffset CuandoEn { get; private set; }

    public static Ejecucion Crear(
        Guid empresaId, Guid reglaId, Guid sujetoId, Guid? contactoId, string queHizo, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        return new Ejecucion(Guid.NewGuid(), empresaId, reglaId, sujetoId, contactoId, queHizo, reloj.AhoraUtc);
    }
}
