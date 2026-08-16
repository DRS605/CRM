using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Contactos.Dominio;

/// <summary>
/// Empresa a la que pertenece un contacto. Es **opcional**: en B2C no se rellena y no estorba.
/// </summary>
public sealed class Cuenta : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaNombre = 160;

    private Cuenta(Guid id)
        : base(id, Guid.Empty) => Nombre = null!;

    private Cuenta(Guid id, Guid empresaId, string nombre, string? nif, string? sector, string? provincia, int? tamano, string? web, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Nombre = nombre;
        Nif = nif;
        Sector = sector;
        Provincia = provincia;
        Tamano = tamano;
        Web = web;
        Activa = true;
        CreadoEn = ahora;
        ActualizadoEn = ahora;
    }

    public string Nombre { get; private set; }

    public string? Nif { get; private set; }

    /// <summary>Sector de actividad. Es uno de los factores del Encaje del módulo 5.</summary>
    public string? Sector { get; private set; }

    /// <summary>Provincia. Otro factor del Encaje, y la base del reparto de leads por zona.</summary>
    public string? Provincia { get; private set; }

    /// <summary>Número de empleados, si se sabe.</summary>
    public int? Tamano { get; private set; }

    public string? Web { get; private set; }

    public bool Activa { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public DateTimeOffset ActualizadoEn { get; private set; }

    public static Resultado<Cuenta> Crear(Guid empresaId, string? nombre, string? nif, string? sector, string? provincia, int? tamano, string? web, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var error = Validar(nombre, tamano);
        if (error is not null)
        {
            return Resultado.Fallo<Cuenta>(error);
        }

        return Resultado.Ok(new Cuenta(
            Guid.NewGuid(), empresaId, nombre!.Trim(), N(nif), N(sector), N(provincia), tamano, N(web), reloj.AhoraUtc));
    }

    public Resultado Actualizar(string? nombre, string? nif, string? sector, string? provincia, int? tamano, string? web, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var error = Validar(nombre, tamano);
        if (error is not null)
        {
            return Resultado.Fallo(error);
        }

        Nombre = nombre!.Trim();
        Nif = N(nif);
        Sector = N(sector);
        Provincia = N(provincia);
        Tamano = tamano;
        Web = N(web);
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    public void Desactivar(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        Activa = false;
        ActualizadoEn = reloj.AhoraUtc;
    }

    private static Error? Validar(string? nombre, int? tamano)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Error.Validacion("cuenta.nombre_vacio", "El nombre de la cuenta es obligatorio.");
        }

        if (nombre.Trim().Length > LongitudMaximaNombre)
        {
            return Error.Validacion("cuenta.nombre_largo", "El nombre de la cuenta es demasiado largo.");
        }

        return tamano is < 0 or > 1_000_000
            ? Error.Validacion("cuenta.tamano_invalido", "El número de empleados no es válido.")
            : null;
    }

    private static string? N(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
