using Matchketing.Campos.Dominio;

namespace Matchketing.Campos.Aplicacion;

public interface IRepositorioCampos
{
    Task<CampoPropio?> CampoAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<CampoPropio>> CamposAsync(Ambito ambito, CancellationToken ct = default);

    /// <summary>Todos, de los dos ámbitos. Para la pantalla de ajustes y para la exportación.</summary>
    Task<IReadOnlyList<CampoPropio>> TodosAsync(CancellationToken ct = default);

    void Anadir(CampoPropio campo);

    void Quitar(CampoPropio campo);

    Task<ValorCampo?> ValorAsync(Guid campoId, Guid entidadId, CancellationToken ct = default);

    /// <summary>Los valores de una entidad concreta: lo que se pinta en su ficha.</summary>
    Task<IReadOnlyList<ValorCampo>> ValoresDeAsync(Ambito ambito, Guid entidadId, CancellationToken ct = default);

    void Anadir(ValorCampo valor);

    void Quitar(ValorCampo valor);

    /// <summary>
    /// Se van todos los valores de un campo. Se usa al borrar el campo: dejarlos sería guardar datos que
    /// ya nadie puede leer, porque sin el campo no se sabe ni qué significaban.
    /// </summary>
    Task<int> QuitarValoresDeAsync(Guid campoId, CancellationToken ct = default);

    /// <summary>Cuántas fichas tienen este campo relleno. Decide si quitarlo cuesta algo o no.</summary>
    Task<int> CuantosRellenosAsync(Guid campoId, CancellationToken ct = default);

    /// <summary>
    /// Cuántos valores guardados de este campo **no** están en esa lista de opciones.
    ///
    /// Es la comprobación que impide cambiar las opciones de una lista dejando datos inválidos detrás.
    /// Vive en un puerto porque hace falta mirar la tabla de valores, y el dominio no la conoce.
    /// </summary>
    Task<int> CuantosFueraDeAsync(Guid campoId, IReadOnlyList<string> opciones, CancellationToken ct = default);
}

/// <summary>
/// ¿Existe esa entidad en esta empresa? Lo implementa la persistencia, que conoce contactos y cuentas.
///
/// Sin esto se podrían colgar valores de un identificador inventado: filas que no se ven en ninguna
/// ficha, que no se borran con nadie y que salen en la exportación de la empresa sin dueño.
/// </summary>
public interface IExisteLaEntidad
{
    Task<bool> ExisteAsync(Ambito ambito, Guid entidadId, CancellationToken ct = default);
}
