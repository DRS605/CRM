namespace Matchketing.Contactos.Dominio;

/// <summary>Situación del contacto en su ciclo de vida.</summary>
public enum EstadoContacto
{
    /// <summary>Todavía no ha comprado.</summary>
    Lead = 1,

    /// <summary>Ya ha comprado alguna vez.</summary>
    Cliente = 2,

    /// <summary>Se descartó.</summary>
    Perdido = 3,

    /// <summary>Ha pedido no recibir más comunicaciones. Estado terminal desde nuestro lado.</summary>
    Baja = 4,
}

public enum TipoActividad
{
    Nota = 1,
    Llamada = 2,
    Correo = 3,
    Reunion = 4,
    Formulario = 5,
    VisitaWeb = 6,

    /// <summary>Anotación del propio sistema: fusión, cambio de propietario, importación…</summary>
    Sistema = 7,
}

public enum SentidoActividad
{
    Entrante = 1,
    Saliente = 2,

    /// <summary>Ni entra ni sale: una nota interna o un apunte del sistema.</summary>
    Interna = 3,
}

/// <summary>Resultado de una llamada, para registrarla en un solo clic.</summary>
public enum ResultadoLlamada
{
    Contactado = 1,
    NoContesta = 2,
    NoInteresa = 3,
    VolverALlamar = 4,
}
