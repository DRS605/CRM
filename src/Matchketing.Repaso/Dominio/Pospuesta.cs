using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Repaso.Dominio;

/// <summary>
/// «Esto ya lo he visto; no me lo vuelvas a preguntar hasta tal día.»
///
/// Es el único dato que este módulo guarda, y es lo que hace posible **vaciar la pila**. Sin él, una
/// oportunidad estancada a la que contestas «sigue viva» vuelve a aparecer mañana, y la pantalla
/// pasa de ser un repaso a ser un recordatorio de que nunca acabas. Un comercial abandona eso en dos
/// semanas.
///
/// La alternativa era tocar la fecha de entrada en la etapa para que dejara de contar como estancada,
/// y eso habría sido mentir en el histórico del embudo para arreglar un problema de interfaz.
///
/// También es el registro de que alguien la revisó: quién y cuándo. Un jefe puede distinguir «lo miró
/// y decidió que sigue» de «nadie lo ha mirado», que es justo lo que un CRM normal no sabe decir.
/// </summary>
public sealed class Pospuesta : RaizAgregadoEmpresa<Guid>
{
    private Pospuesta(Guid id)
        : base(id, Guid.Empty) => Clave = null!;

    private Pospuesta(Guid id, Guid empresaId, string clave, Guid? usuarioId, DateOnly hasta, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Clave = clave;
        UsuarioId = usuarioId;
        Hasta = hasta;
        En = ahora;
    }

    /// <summary>La clave de la pregunta, tal cual: <c>oportunidad-estancada:a1b2…</c></summary>
    public string Clave { get; private set; }

    public Guid? UsuarioId { get; private set; }

    /// <summary>Día en que la pregunta vuelve. Se compara con «hoy», no con un instante.</summary>
    public DateOnly Hasta { get; private set; }

    public DateTimeOffset En { get; private set; }

    public static Pospuesta Crear(Guid empresaId, ClavePregunta clave, Guid? usuarioId, int dias, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var hasta = HorasLaborables.DiaDeTrabajo(reloj.AhoraUtc).AddDays(Math.Max(dias, 1));
        return new Pospuesta(Guid.NewGuid(), empresaId, clave.ToString(), usuarioId, hasta, reloj.AhoraUtc);
    }

    /// <summary>Cuánto se pospone cada cosa. Los avisos, mucho más que el trabajo pendiente.</summary>
    public static int DiasPara(TipoPregunta tipo, Respuesta respuesta) => (tipo, respuesta) switch
    {
        // «Déjalo estar» sobre un aviso: no es que no interese, es que no ahora. Volver a la semana
        // sería insistir, y a quien insiste se le deja de escuchar.
        (TipoPregunta.SilencioCaliente, Respuesta.DejarloEstar) => 30,
        (TipoPregunta.ClienteSinSiguientePaso, Respuesta.DejarloEstar) => 90,

        // Un correo sin contestar: catorce días. Menos que los avisos porque aquí hay una conversación
        // empezada y enfriarla un mes es perderla; más que una semana porque volver a proponer lo mismo
        // el viernes siguiente es lo que enseña a contestar «ahora no» sin leer.
        (TipoPregunta.CorreoSinRespuesta, Respuesta.DejarloEstar) => 14,

        // «Ya me contestó» no necesita aplazamiento largo: la actividad entrante que se acaba de apuntar
        // ya saca la pregunta de la consulta para siempre. El día de gracia es solo para que no
        // reaparezca por un desfase de reloj entre el apunte y la siguiente pila.
        (TipoPregunta.CorreoSinRespuesta, Respuesta.YaContesto) => 1,

        // Una campaña abierta y aparcada: treinta días. Más que un correo personal sin contestar,
        // porque aquí no hay una conversación empezada que se pueda enfriar —él abrió un envío, no
        // escribió—, y volver a sacarlo a los catorce días con el mismo motivo es ruido.
        (TipoPregunta.AbrioLaCampania, Respuesta.DejarloEstar) => 30,
        (TipoPregunta.AbrioLaCampania, Respuesta.YaContesto) => 1,

        // Revisada y sigue igual: una semana, que es cada cuánto se repasa.
        (_, Respuesta.SigueViva) => 7,

        // «Ahora no» es un aplazamiento corto a propósito: sirve para pasar de pantalla sin decidir,
        // no para esconder algo indefinidamente.
        (_, Respuesta.Saltar) => 3,

        _ => 7,
    };
}
