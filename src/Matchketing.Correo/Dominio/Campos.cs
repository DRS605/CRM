using System.Text;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Correo.Dominio;

/// <summary>
/// Lo que se sabe de la persona y de quien escribe. <paramref name="Correo"/> no es un campo de
/// plantilla —es la dirección de destino— pero viene en la misma consulta: pedirlo aparte serían dos
/// viajes a la base para preparar un solo correo.
/// </summary>
public sealed record DatosDelEnvio(
    string? Nombre, string? Cuenta, string? Comercial, string? Empresa, string? Correo);

/// <summary>
/// Los huecos que puede llevar una plantilla, y cómo se rellenan.
///
/// Son **cuatro y cerrados**. La tentación es dejar cualquier campo del contacto, y con eso llegan dos
/// problemas: plantillas que fallan al enviar porque ese contacto no tiene ese dato, y correos que
/// salen con «Hola {{cargo}},». Cuatro campos que casi siempre existen se pueden exigir; cuarenta, no.
///
/// Y son estrictos por los dos lados:
///
/// · **Al guardar**, un hueco que no existe es un error. Dejarlo pasar significa que el correo saldrá
///   con las llaves puestas, y eso no se descubre hasta que lo lee el cliente.
/// · **Al enviar**, un hueco sin valor es un error. «Hola ,» es peor que no mandar nada: se nota que
///   viene de una máquina, que es justo lo que la plantilla intentaba disimular.
/// </summary>
public static class Campos
{
    public const string Nombre = "nombre";
    public const string Cuenta = "cuenta";
    public const string Comercial = "comercial";
    public const string Empresa = "empresa";

    public static IReadOnlyList<string> Todos { get; } = [Nombre, Cuenta, Comercial, Empresa];

    /// <summary>Los huecos que aparecen en un texto, sin repetir y en orden de aparición.</summary>
    public static IReadOnlyList<string> Usados(string? texto)
    {
        var encontrados = new List<string>();
        if (string.IsNullOrEmpty(texto))
        {
            return encontrados;
        }

        var desde = 0;
        while (true)
        {
            var abre = texto.IndexOf("{{", desde, StringComparison.Ordinal);
            if (abre < 0)
            {
                return encontrados;
            }

            var cierra = texto.IndexOf("}}", abre + 2, StringComparison.Ordinal);
            if (cierra < 0)
            {
                // Un `{{` sin cerrar: se cuenta como hueco vacío para que la validación lo rechace en
                // vez de dejarlo pasar como texto normal.
                encontrados.Add(string.Empty);
                return encontrados;
            }

            var nombre = texto[(abre + 2)..cierra].Trim();
            if (!encontrados.Contains(nombre, StringComparer.Ordinal))
            {
                encontrados.Add(nombre);
            }

            desde = cierra + 2;
        }
    }

    /// <summary>¿Todos los huecos de este texto existen? Se comprueba al guardar la plantilla.</summary>
    public static Resultado Validar(string? texto)
    {
        foreach (var usado in Usados(texto))
        {
            if (!Todos.Contains(usado, StringComparer.Ordinal))
            {
                return Resultado.Fallo(Error.Validacion(
                    "plantilla.campo_desconocido",
                    string.IsNullOrEmpty(usado)
                        ? "Hay un «{{» sin cerrar."
                        : $"El campo «{usado}» no existe. Los que hay: {string.Join(", ", Todos)}."));
            }
        }

        return Resultado.Ok();
    }

    /// <summary>
    /// Rellena los huecos. Falla si alguno no tiene valor: es la comprobación que evita que salga un
    /// correo con un saludo a medias.
    /// </summary>
    public static Resultado<string> Rellenar(string texto, DatosDelEnvio datos)
    {
        ArgumentNullException.ThrowIfNull(texto);
        ArgumentNullException.ThrowIfNull(datos);

        var resultado = new StringBuilder(texto.Length);
        var desde = 0;

        while (true)
        {
            var abre = texto.IndexOf("{{", desde, StringComparison.Ordinal);
            if (abre < 0)
            {
                resultado.Append(texto, desde, texto.Length - desde);
                return Resultado.Ok(resultado.ToString());
            }

            var cierra = texto.IndexOf("}}", abre + 2, StringComparison.Ordinal);
            if (cierra < 0)
            {
                return Resultado.Fallo<string>(Error.Validacion(
                    "plantilla.campo_desconocido", "Hay un «{{» sin cerrar."));
            }

            resultado.Append(texto, desde, abre - desde);

            var nombre = texto[(abre + 2)..cierra].Trim();
            var valor = Valor(nombre, datos);

            if (string.IsNullOrWhiteSpace(valor))
            {
                return Resultado.Fallo<string>(Error.Validacion(
                    "correo.campo_sin_valor",
                    $"No se puede rellenar «{nombre}»: este contacto no tiene ese dato. " +
                    "Rellénalo en su ficha o usa otra plantilla."));
            }

            resultado.Append(valor);
            desde = cierra + 2;
        }
    }

    private static string? Valor(string campo, DatosDelEnvio datos) => campo switch
    {
        Nombre => datos.Nombre,
        Cuenta => datos.Cuenta,
        Comercial => datos.Comercial,
        Empresa => datos.Empresa,
        _ => null,
    };
}
