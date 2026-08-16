using Matchketing.Contactos.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Contactos.Aplicacion;

/// <summary>
/// Importación de contactos desde CSV, en dos pasos: **previsualizar** (valida y avisa, sin crear
/// nada) y **confirmar** (crea las válidas). Nadie debería descubrir que su fichero estaba mal
/// después de haber metido 400 filas basura.
/// </summary>
public sealed class ImportarContactos(
    IRepositorioContactos contactos,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    private static readonly string[] AliasNombre = ["nombre", "nombre completo", "contacto", "razon social", "name"];
    private static readonly string[] AliasEmail = ["email", "correo", "correo electronico", "e-mail", "mail"];
    private static readonly string[] AliasTelefono = ["telefono", "tel", "movil", "telefono movil", "phone"];
    private static readonly string[] AliasCargo = ["cargo", "puesto", "position", "title"];
    private static readonly string[] AliasOrigen = ["origen", "procedencia", "fuente", "source"];

    public async Task<Resultado<ResultadoImportacion>> EjecutarAsync(string? contenido, bool previsualizar, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<ResultadoImportacion>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        if (string.IsNullOrWhiteSpace(contenido))
        {
            return Resultado.Fallo<ResultadoImportacion>(Error.Validacion("importacion.vacia", "El fichero está vacío."));
        }

        var lineas = contenido.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lineas.Count < 2)
        {
            return Resultado.Fallo<ResultadoImportacion>(
                Error.Validacion("importacion.sin_filas", "El fichero necesita una cabecera y al menos una fila."));
        }

        var separador = LectorCsv.DetectarSeparador(lineas[0]);
        var cabeceras = LectorCsv.PartirLinea(lineas[0], separador);

        var iNombre = LectorCsv.IndiceDe(cabeceras, AliasNombre);
        var iEmail = LectorCsv.IndiceDe(cabeceras, AliasEmail);
        var iTelefono = LectorCsv.IndiceDe(cabeceras, AliasTelefono);
        var iCargo = LectorCsv.IndiceDe(cabeceras, AliasCargo);
        var iOrigen = LectorCsv.IndiceDe(cabeceras, AliasOrigen);

        if (iNombre < 0)
        {
            return Resultado.Fallo<ResultadoImportacion>(
                Error.Validacion("importacion.sin_nombre", "No se encuentra la columna del nombre."));
        }

        if (iEmail < 0 && iTelefono < 0)
        {
            return Resultado.Fallo<ResultadoImportacion>(
                Error.Validacion("importacion.sin_medio", "Hace falta al menos una columna de correo o de teléfono."));
        }

        var reconocidas = new List<string>();
        if (iNombre >= 0) { reconocidas.Add("nombre"); }
        if (iEmail >= 0) { reconocidas.Add("email"); }
        if (iTelefono >= 0) { reconocidas.Add("teléfono"); }
        if (iCargo >= 0) { reconocidas.Add("cargo"); }
        if (iOrigen >= 0) { reconocidas.Add("origen"); }

        var errores = new List<FilaConError>();
        var nuevos = new List<Contacto>();
        var duplicadas = 0;

        // Claves ya vistas en el propio fichero: un CSV suele traer la misma fila dos veces.
        var vistosEmail = new HashSet<string>(StringComparer.Ordinal);
        var vistosTelefono = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < lineas.Count; i++)
        {
            var campos = LectorCsv.PartirLinea(lineas[i], separador);
            string? Campo(int indice) => indice >= 0 && indice < campos.Count ? campos[indice] : null;

            var creado = Contacto.Crear(
                empresaId, Campo(iNombre), Campo(iEmail), Campo(iTelefono), Campo(iCargo),
                null, Campo(iOrigen) ?? "importación", contexto.UsuarioId, reloj);

            if (creado.Fallido)
            {
                // +1 porque la línea 1 es la cabecera y la gente cuenta desde 1, no desde 0.
                errores.Add(new FilaConError(i + 1, creado.Error!.Mensaje));
                continue;
            }

            var contacto = creado.Valor;

            var repetidoEnFichero =
                (contacto.Email is not null && !vistosEmail.Add(contacto.Email)) |
                (contacto.Telefono is not null && !vistosTelefono.Add(contacto.Telefono));

            var repetidoEnBase = false;
            if (!repetidoEnFichero)
            {
                if (contacto.Email is not null)
                {
                    repetidoEnBase = await contactos.BuscarPorEmailAsync(contacto.Email, ct).ConfigureAwait(false) is not null;
                }

                if (!repetidoEnBase && contacto.Telefono is not null)
                {
                    repetidoEnBase = await contactos.BuscarPorTelefonoAsync(contacto.Telefono, ct).ConfigureAwait(false) is not null;
                }
            }

            if (repetidoEnFichero || repetidoEnBase)
            {
                duplicadas++;
                continue;
            }

            nuevos.Add(contacto);
        }

        if (!previsualizar)
        {
            foreach (var c in nuevos)
            {
                contactos.Anadir(c);
            }
        }

        return Resultado.Ok(new ResultadoImportacion(
            previsualizar, nuevos.Count, previsualizar ? 0 : nuevos.Count, duplicadas, errores, reconocidas));
    }
}
