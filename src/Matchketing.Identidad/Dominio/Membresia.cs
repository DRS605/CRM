using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Identidad.Dominio;

/// <summary>
/// Pertenencia de un usuario a una empresa, con su rol. Es la pieza que hace posible que una
/// gestoría o un comercial externo trabajen en varias empresas sin duplicar la cuenta.
/// </summary>
public sealed class Membresia : RaizAgregadoEmpresa<Guid>
{
    private Membresia(Guid id)
        : base(id, Guid.Empty)
    {
    }

    private Membresia(Guid id, Guid usuarioId, Guid empresaId, Rol rol, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        UsuarioId = usuarioId;
        Rol = rol;
        Activa = true;
        CreadoEn = ahora;
    }

    public Guid UsuarioId { get; private set; }

    public Rol Rol { get; private set; }

    public bool Activa { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public IReadOnlyList<string> Permisos => Activa ? PermisosDeRol.De(Rol) : [];

    public static Membresia Crear(Guid usuarioId, Guid empresaId, Rol rol, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        return new Membresia(Guid.NewGuid(), usuarioId, empresaId, rol, reloj.AhoraUtc);
    }

    /// <summary>Cambia el rol. No se permite dejar la empresa sin ningún propietario (se comprueba en el caso de uso).</summary>
    public Resultado CambiarRol(Rol rol)
    {
        if (!Activa)
        {
            return Resultado.Fallo(Error.Conflicto("membresia.inactiva", "La membresía no está activa."));
        }

        Rol = rol;
        return Resultado.Ok();
    }

    public void Desactivar() => Activa = false;
}
