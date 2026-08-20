namespace Matchketing.Avisos.Aplicacion;

/// <summary>
/// Lo que se le muestra a la persona. Va cifrado dentro del cuerpo del push y lo pinta el trabajador
/// de servicio.
///
/// <paramref name="Ruta"/> es dónde abrir al pulsarlo. Un aviso que abre la pantalla de inicio en vez
/// de lo que anuncia obliga a navegar a mano, y entonces el aviso ha costado más de lo que ahorra.
/// </summary>
public sealed record Aviso(string Titulo, string Cuerpo, string Ruta, int? Cuantas = null);

/// <summary>Qué pasó al mandar un aviso. Lo devuelve el emisor y decide si la suscripción sobrevive.</summary>
public enum ResultadoEnvio
{
    Entregado = 1,

    /// <summary>El servicio dice que esa suscripción ya no existe (404 o 410). Hay que borrarla.</summary>
    SuscripcionMuerta = 2,

    /// <summary>Fallo pasajero: red, 429, 500. Se vuelve a intentar en la siguiente pasada.</summary>
    FalloPasajero = 3,

    /// <summary>Nuestro error: token rechazado, cuerpo mal formado. No se reintenta; se registra.</summary>
    Rechazado = 4,
}

public sealed record ResumenAvisos(int Enviados, int Borrados, int Fallidos);
