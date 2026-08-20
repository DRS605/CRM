namespace Matchketing.Nucleo.Comun;

/// <summary>
/// Empresa activa de la petición en curso. Se resuelve del JWT y alimenta tanto el filtro global
/// de EF Core como la variable de sesión de PostgreSQL que activa la RLS.
/// </summary>
public interface IContextoEmpresa
{
    Guid? EmpresaId { get; }

    Guid? UsuarioId { get; }

    IReadOnlyCollection<string> Permisos { get; }

    bool Tiene(string permiso);
}

/// <summary>
/// Permite fijar la empresa **sin token**, para los endpoints que la deducen de otra cosa: la clave de
/// un formulario público, la firma de un enlace de baja, el token de un píxel de apertura o el de una
/// invitación. En todos ellos quien llama no está autenticado —o no lo está *para esa empresa*— y el
/// inquilino sale de algo firmado o guardado, nunca de un parámetro que el cliente pueda inventar.
///
/// **La empresa fijada gana al token**, y eso es a propósito. Antes ganaba el token, y era un fallo
/// esperando su turno: si a `/f/{clave}` —el formulario de la web de un cliente— llegaba una petición
/// con la sesión de otra empresa abierta, el lead se guardaba en la empresa **de la sesión**, no en la
/// del formulario. Hoy no pasa porque el navegador no adjunta el token a esas rutas, pero eso es una
/// casualidad del transporte, no una garantía. Lo que sí es una garantía: fijar la empresa es un acto
/// deliberado de cuatro endpoints contados, y el valor que se pasa siempre viene firmado o de una fila.
///
/// El invariante T2 sigue en pie con una frase más: la empresa sale del JWT, salvo en los endpoints
/// públicos que la derivan de un token propio, donde manda la derivada.
/// </summary>
public interface IContextoEmpresaPublico
{
    void FijarEmpresa(Guid empresaId);
}
