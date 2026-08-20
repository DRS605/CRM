using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Correo.Dominio;

/// <summary>
/// Para qué sirve la plantilla. Decide **qué permiso hace falta** para usarla, y por eso es un dato de
/// la plantilla y no una casilla que se marque en el momento de enviar: quien escribe el texto sabe si
/// está contestando o vendiendo; quien pulsa «enviar» a las siete de la tarde, no siempre.
/// </summary>
public enum ParaQue
{
    /// <summary>Contestar a lo que la persona ha pedido. No permite promociones.</summary>
    AtenderSolicitud = 1,

    /// <summary>Comunicación comercial. Exige consentimiento comercial vigente.</summary>
    Comercial = 2,
}

/// <summary>
/// Un correo escrito una vez para mandarlo muchas.
///
/// Existe para que escribir el correto número catorce cueste un toque y no cuatro minutos, que es la
/// misma tesis del [repaso]: el trabajo repetido es el que no se hace. Pero **no** es una plantilla de
/// campaña: aquí no hay listas ni segmentos, se manda de uno en uno desde la ficha de alguien. La
/// diferencia importa porque es lo que permite comprobar el permiso de esa persona concreta antes de
/// cada envío.
/// </summary>
public sealed class Plantilla : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaNombre = 80;
    public const int LongitudMaximaAsunto = 200;
    public const int LongitudMaximaCuerpo = 8000;

    private Plantilla(Guid id)
        : base(id, Guid.Empty)
    {
        Nombre = null!;
        Asunto = null!;
        Cuerpo = null!;
    }

    private Plantilla(Guid id, Guid empresaId, string nombre, string asunto, string cuerpo, ParaQue paraQue, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Nombre = nombre;
        Asunto = asunto;
        Cuerpo = cuerpo;
        ParaQue = paraQue;
        CreadaEn = ahora;
    }

    /// <summary>Cómo la llama el comercial en la lista. No sale en el correo.</summary>
    public string Nombre { get; private set; }

    public string Asunto { get; private set; }

    /// <summary>Texto plano. Ver la nota de <see cref="Correo"/> sobre por qué no hay HTML.</summary>
    public string Cuerpo { get; private set; }

    public ParaQue ParaQue { get; private set; }

    public DateTimeOffset CreadaEn { get; private set; }

    /// <summary>Cuántas veces se ha usado. Sirve para ordenar la lista por lo que de verdad se usa.</summary>
    public int Usos { get; private set; }

    public static Resultado<Plantilla> Crear(
        Guid empresaId, string? nombre, string? asunto, string? cuerpo, ParaQue paraQue, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var comprobado = Comprobar(nombre, asunto, cuerpo);
        if (comprobado.Fallido)
        {
            return Resultado.Fallo<Plantilla>(comprobado.Error!);
        }

        return Resultado.Ok(new Plantilla(
            Guid.NewGuid(), empresaId, nombre!.Trim(), asunto!.Trim(), cuerpo!.Trim(), paraQue, reloj.AhoraUtc));
    }

    public Resultado Cambiar(string? nombre, string? asunto, string? cuerpo, ParaQue paraQue)
    {
        var comprobado = Comprobar(nombre, asunto, cuerpo);
        if (comprobado.Fallido)
        {
            return comprobado;
        }

        Nombre = nombre!.Trim();
        Asunto = asunto!.Trim();
        Cuerpo = cuerpo!.Trim();
        ParaQue = paraQue;
        return Resultado.Ok();
    }

    public void Usada() => Usos++;

    /// <summary>El asunto y el cuerpo ya rellenados, o el motivo de por qué no se puede.</summary>
    public Resultado<(string Asunto, string Cuerpo)> Redactar(DatosDelEnvio datos)
    {
        var asunto = Campos.Rellenar(Asunto, datos);
        if (asunto.Fallido)
        {
            return Resultado.Fallo<(string, string)>(asunto.Error!);
        }

        var cuerpo = Campos.Rellenar(Cuerpo, datos);
        return cuerpo.Fallido
            ? Resultado.Fallo<(string, string)>(cuerpo.Error!)
            : Resultado.Ok((asunto.Valor, cuerpo.Valor));
    }

    private static Resultado Comprobar(string? nombre, string? asunto, string? cuerpo)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > LongitudMaximaNombre)
        {
            return Resultado.Fallo(Error.Validacion(
                "plantilla.nombre_invalido", $"Ponle un nombre de hasta {LongitudMaximaNombre} caracteres."));
        }

        if (string.IsNullOrWhiteSpace(asunto) || asunto.Length > LongitudMaximaAsunto)
        {
            return Resultado.Fallo(Error.Validacion(
                "plantilla.asunto_invalido", $"El asunto es obligatorio y cabe en {LongitudMaximaAsunto} caracteres."));
        }

        // Un correo sin asunto acaba en la carpeta de no deseados, y uno de ocho mil caracteres no se
        // lee. Los dos límites son de sentido común, no de base de datos.
        if (string.IsNullOrWhiteSpace(cuerpo) || cuerpo.Length > LongitudMaximaCuerpo)
        {
            return Resultado.Fallo(Error.Validacion(
                "plantilla.cuerpo_invalido", $"El cuerpo es obligatorio y cabe en {LongitudMaximaCuerpo} caracteres."));
        }

        var enAsunto = Campos.Validar(asunto);
        return enAsunto.Fallido ? enAsunto : Campos.Validar(cuerpo);
    }
}
