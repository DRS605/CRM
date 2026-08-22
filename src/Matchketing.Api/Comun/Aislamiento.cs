using Matchketing.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Api.Comun;

/// <summary>
/// ¿Está puesta la **segunda** barrera del aislamiento entre empresas?
///
/// El aislamiento tiene dos: el filtro global de EF Core, que vive en el código, y las políticas de
/// seguridad por fila de PostgreSQL, que viven en la base. Las segundas **no se aplican a un
/// superusuario**, así que un despliegue que se conecte con `postgres` se queda con una sola barrera y
/// **nada falla**: las pruebas pasan, la aplicación funciona y los datos siguen separados… mientras el
/// filtro de EF Core no tenga un agujero. Es exactamente la clase de fallo que no se descubre.
///
/// Por eso esto no avisa: hace que la sonda de salud diga que la instancia está enferma, y un
/// equilibrador de carga con una sonda no le manda tráfico a una instancia enferma. Es preferible una
/// caída en el primer minuto del primer despliegue —con el motivo escrito— a un producto que promete
/// aislamiento y tiene la mitad.
///
/// La comprobación se hace una vez y se recuerda: es una pregunta sobre el rol de la conexión, y el rol
/// no cambia mientras el proceso vive.
/// </summary>
public sealed class Aislamiento(IConfiguration config)
{
    /// <summary>
    /// Escape para las pruebas de integración, que corren **a propósito** como superusuario: crean y
    /// borran la base en cada arranque. Que haya que decirlo en voz alta es la mitad del valor: nadie
    /// pone esto en un despliegue de verdad sin darse cuenta de lo que hace.
    /// </summary>
    private bool Permitido => config.GetValue("Aislamiento:PermitirSuperusuario", false);

    private bool? esSuperusuario;

    public async Task<bool> DosBarrerasAsync(ContextoMatchketing bd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bd);

        if (Permitido)
        {
            return true;
        }

        if (esSuperusuario is null)
        {
            var filas = await bd.Database
                .SqlQuery<bool>($"SELECT usesuper AS \"Value\" FROM pg_user WHERE usename = current_user")
                .ToListAsync(ct)
                .ConfigureAwait(false);

            // Sin fila no se puede afirmar que esté mal, y una sonda que se pone en rojo por una duda
            // es una sonda que se ignora. Se da por bueno y se sigue.
            esSuperusuario = filas.Count > 0 && filas[0];
        }

        return esSuperusuario != true;
    }
}
