using Matchketing.Correo.Aplicacion;
using Matchketing.Cumplimiento.Aplicacion;
using Matchketing.Cumplimiento.Dominio;
using Matchketing.Nucleo.Comun;

namespace Matchketing.Api.Comun;

/// <summary>
/// El enlace de baja que va dentro de cada correo comercial.
///
/// Vive aquí porque cruza dos módulos —Correo lo necesita, Cumplimiento lo firma— y ninguno de los dos
/// conoce al otro. Es el mismo enlace que se ve en la ficha del contacto: la misma firma, la misma ruta
/// y el mismo secreto, así que no hay dos formas de darse de baja que puedan divergir.
/// </summary>
public sealed class EnlaceDeBajaFirmado(AjustesBaja ajustes, IContextoEmpresa contexto) : IEnlaceDeBaja
{
    public string? De(Guid contactoId)
    {
        // Sin empresa activa no se puede firmar: la firma lleva dentro la empresa, y es lo que impide
        // que el enlace de una valga en otra. Pasa cuando el trabajo de fondo aún no ha fijado el
        // inquilino, y devolver nulo es lo correcto —el correo sale sin enlace— y no una excepción que
        // tire la pasada de envíos entera.
        return contexto.EmpresaId is { } empresaId
            ? $"{ajustes.UrlBase.TrimEnd('/')}/b/{EnlaceBaja.Firmar(empresaId, contactoId, ajustes.Secreto)}"
            : null;
    }
}
