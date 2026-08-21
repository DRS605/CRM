using Matchketing.Campos.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Campos.Aplicacion;

/// <summary>La definición de un campo, para la pantalla de ajustes.</summary>
public sealed record FichaCampo(
    Guid Id,
    string Ambito,
    string Nombre,
    string Clave,
    string Tipo,
    IReadOnlyList<string> Opciones,
    int Orden,
    int Rellenos);

/// <summary>
/// Un campo con su valor para una entidad concreta: lo que se pinta en la ficha.
///
/// Salen **todos** los campos definidos, también los que esa persona no tiene rellenos, con el valor a
/// nulo. Si solo salieran los rellenos, un campo recién definido no aparecería en ninguna ficha y no
/// habría forma de rellenarlo por primera vez.
/// </summary>
public sealed record CampoConValor(
    Guid CampoId,
    string Nombre,
    string Clave,
    string Tipo,
    IReadOnlyList<string> Opciones,
    string? Valor);

public sealed class ServicioCampos(
    IRepositorioCampos repositorio,
    IExisteLaEntidad existe,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    // ---------- La definición ----------

    public async Task<Resultado<CampoPropio>> CrearAsync(
        Ambito ambito, string? nombre, TipoCampo tipo, IReadOnlyList<string>? opciones,
        CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<CampoPropio>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var suyos = await repositorio.CamposAsync(ambito, ct).ConfigureAwait(false);
        if (suyos.Count >= CampoPropio.MaximoPorAmbito)
        {
            return Resultado.Fallo<CampoPropio>(Error.Conflicto(
                "campo.demasiados",
                $"Ya hay {CampoPropio.MaximoPorAmbito} campos propios ahí, que es el máximo. " +
                "Con más, la ficha deja de leerse: quita uno que no uses."));
        }

        var creado = CampoPropio.Crear(
            empresaId, ambito, nombre, tipo, opciones, orden: SiguienteOrden(suyos), reloj);

        if (creado.Fallido)
        {
            return creado;
        }

        // Dos campos con la misma clave harían dos columnas iguales en el CSV y dos filas iguales en la
        // ficha. Se compara por clave y no por nombre: «Nº de póliza» y «N de poliza» son nombres
        // distintos y la misma clave, y quien los ve en la pantalla no entendería por qué hay dos.
        if (suyos.Any(c => c.Clave == creado.Valor.Clave))
        {
            return Resultado.Fallo<CampoPropio>(Error.Conflicto(
                "campo.repetido", $"Ya hay un campo que se llama prácticamente igual: «{creado.Valor.Nombre}»."));
        }

        repositorio.Anadir(creado.Valor);
        return creado;
    }

    public async Task<Resultado> RenombrarAsync(Guid id, string? nombre, CancellationToken ct = default)
    {
        var campo = await repositorio.CampoAsync(id, ct).ConfigureAwait(false);
        return campo is null
            ? Resultado.Fallo(Error.NoEncontrado("campo.no_encontrado", "Ese campo no existe."))
            : campo.Renombrar(nombre);
    }

    /// <summary>
    /// Cambia las opciones de una lista, **si no deja ningún dato inválido detrás**.
    ///
    /// Quitar una opción que alguien ya usó dejaría valores que no están en la lista: la ficha los
    /// enseñaría sin poder cambiarlos y cualquier recuento futuro tendría un grupo fantasma. Se rechaza
    /// diciendo cuántos hay, que es lo que permite arreglarlo antes.
    /// </summary>
    public async Task<Resultado> CambiarOpcionesAsync(
        Guid id, IReadOnlyList<string>? opciones, CancellationToken ct = default)
    {
        var campo = await repositorio.CampoAsync(id, ct).ConfigureAwait(false);
        if (campo is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("campo.no_encontrado", "Ese campo no existe."));
        }

        var limpias = (opciones ?? []).Select(o => (o ?? string.Empty).Trim()).Where(o => o.Length > 0).ToList();
        var fuera = await repositorio.CuantosFueraDeAsync(id, limpias, ct).ConfigureAwait(false);

        if (fuera > 0)
        {
            return Resultado.Fallo(Error.Conflicto(
                "campo.opciones_en_uso",
                fuera == 1
                    ? "Hay un contacto o cuenta con una opción que estás quitando. Cámbiasela primero."
                    : $"Hay {fuera} fichas con una opción que estás quitando. Cámbiaselas primero."));
        }

        return campo.CambiarOpciones(limpias);
    }

    /// <summary>Reordena los campos de un ámbito. Llega la lista entera en el orden que se quiere.</summary>
    public async Task<Resultado> ReordenarAsync(Ambito ambito, IReadOnlyList<Guid> orden, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orden);

        var suyos = await repositorio.CamposAsync(ambito, ct).ConfigureAwait(false);

        // Tienen que venir todos y ninguno de más. Con una lista parcial, los que faltasen se quedarían
        // con el orden viejo y el resultado sería un orden que nadie pidió.
        if (orden.Count != suyos.Count || orden.Distinct().Count() != orden.Count
            || orden.Any(id => suyos.All(c => c.Id != id)))
        {
            return Resultado.Fallo(Error.Validacion(
                "campo.orden_incompleto", "Hay que mandar todos los campos de ese ámbito, una vez cada uno."));
        }

        for (var i = 0; i < orden.Count; i++)
        {
            suyos.First(c => c.Id == orden[i]).Colocar(i);
        }

        return Resultado.Ok();
    }

    /// <summary>
    /// Borra el campo **y todos sus valores**. Devuelve cuántos se fueron.
    ///
    /// Dejar los valores habría sido más prudente en apariencia y peor de verdad: sin el campo no se
    /// sabe qué significaban ni de qué tipo eran, así que serían datos que nadie puede leer ni borrar. Y
    /// si el campo era de un contacto, son datos personales huérfanos.
    /// </summary>
    public async Task<Resultado<int>> BorrarAsync(Guid id, CancellationToken ct = default)
    {
        var campo = await repositorio.CampoAsync(id, ct).ConfigureAwait(false);
        if (campo is null)
        {
            return Resultado.Fallo<int>(Error.NoEncontrado("campo.no_encontrado", "Ese campo no existe."));
        }

        var valores = await repositorio.QuitarValoresDeAsync(id, ct).ConfigureAwait(false);
        repositorio.Quitar(campo);
        return Resultado.Ok(valores);
    }

    public async Task<IReadOnlyList<FichaCampo>> DefinicionAsync(CancellationToken ct = default)
    {
        var todos = await repositorio.TodosAsync(ct).ConfigureAwait(false);
        var fichas = new List<FichaCampo>(todos.Count);

        foreach (var c in todos.OrderBy(c => c.Ambito).ThenBy(c => c.Orden))
        {
            // Cuántas fichas lo tienen relleno. Es el número que hace falta para decidir si un campo se
            // puede quitar sin pensar o si hay algo que perder.
            var rellenos = await repositorio.CuantosRellenosAsync(c.Id, ct).ConfigureAwait(false);
            fichas.Add(new FichaCampo(
                c.Id, TextosCampo.De(c.Ambito), c.Nombre, c.Clave, TextosCampo.De(c.Tipo),
                c.Opciones, c.Orden, rellenos));
        }

        return fichas;
    }

    // ---------- Los valores ----------

    public async Task<IReadOnlyList<CampoConValor>> DeLaFichaAsync(
        Ambito ambito, Guid entidadId, CancellationToken ct = default)
    {
        var campos = await repositorio.CamposAsync(ambito, ct).ConfigureAwait(false);
        if (campos.Count == 0)
        {
            return [];
        }

        var valores = (await repositorio.ValoresDeAsync(ambito, entidadId, ct).ConfigureAwait(false))
            .ToDictionary(v => v.CampoId);

        return campos
            .OrderBy(c => c.Orden)
            .Select(c => new CampoConValor(
                c.Id, c.Nombre, c.Clave, TextosCampo.De(c.Tipo), c.Opciones,
                valores.TryGetValue(c.Id, out var v) ? v.Texto : null))
            .ToArray();
    }

    /// <summary>
    /// Pone el valor de un campo en una ficha. Con el valor vacío, lo quita.
    ///
    /// Que vaciar sea quitar y no guardar la cadena vacía es lo que evita tener dos formas de decir «no
    /// hay dato». Y es lo que espera quien borra el contenido de una casilla.
    /// </summary>
    public async Task<Resultado> FijarAsync(
        Guid campoId, Guid entidadId, string? valor, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var campo = await repositorio.CampoAsync(campoId, ct).ConfigureAwait(false);
        if (campo is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("campo.no_encontrado", "Ese campo no existe."));
        }

        if (!await existe.ExisteAsync(campo.Ambito, entidadId, ct).ConfigureAwait(false))
        {
            return Resultado.Fallo(Error.NoEncontrado(
                "campo.entidad_no_encontrada",
                campo.Ambito == Ambito.Contacto ? "Ese contacto no existe." : "Esa cuenta no existe."));
        }

        var guardado = await repositorio.ValorAsync(campoId, entidadId, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(valor))
        {
            if (guardado is not null)
            {
                repositorio.Quitar(guardado);
            }

            return Resultado.Ok();
        }

        if (guardado is not null)
        {
            return guardado.Cambiar(campo, valor, reloj);
        }

        var creado = ValorCampo.Crear(empresaId, campo, entidadId, valor, reloj);
        if (creado.Fallido)
        {
            return Resultado.Fallo(creado.Error!);
        }

        repositorio.Anadir(creado.Valor);
        return Resultado.Ok();
    }

    /// <summary>
    /// El siguiente hueco de orden. Se usa el máximo más uno y no la cuenta: si se borró uno de en
    /// medio, la cuenta chocaría con un orden que ya existe y dos campos empatarían.
    /// </summary>
    private static int SiguienteOrden(IReadOnlyList<CampoPropio> suyos) =>
        suyos.Count == 0 ? 0 : suyos.Max(c => c.Orden) + 1;
}
