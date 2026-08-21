namespace Matchketing.Identidad.Dominio;

/// <summary>Códigos de permiso. Se comprueban en la capa de aplicación, nunca solo en la interfaz.</summary>
public static class Permisos
{
    public const string ContactoLeer = "contacto.leer";
    public const string ContactoGestionar = "contacto.gestionar";
    public const string OportunidadLeer = "oportunidad.leer";
    public const string OportunidadGestionar = "oportunidad.gestionar";
    public const string TareaLeer = "tarea.leer";
    public const string TareaGestionar = "tarea.gestionar";
    public const string FormularioGestionar = "formulario.gestionar";
    public const string InformeLeer = "informe.leer";
    public const string DatosExportar = "datos.exportar";
    public const string CampaniaLeer = "campania.leer";

    /// <summary>
    /// Crear y lanzar campañas. Tiene permiso propio y **no cae dentro de `contacto.gestionar`**: una
    /// cosa es escribirle a un cliente y otra es escribirle a cuatrocientos en nombre de la empresa. El
    /// día que se dio de alta a un comercial nuevo, nadie pensó que le estaba dando eso.
    /// </summary>
    public const string CampaniaGestionar = "campania.gestionar";

    public const string EmpresaAjustes = "empresa.ajustes";
    public const string UsuarioGestionar = "usuario.gestionar";

    public static readonly IReadOnlyList<string> Todos =
    [
        ContactoLeer, ContactoGestionar,
        OportunidadLeer, OportunidadGestionar,
        TareaLeer, TareaGestionar,
        FormularioGestionar, InformeLeer, DatosExportar,
        CampaniaLeer, CampaniaGestionar,
        EmpresaAjustes, UsuarioGestionar,
    ];
}

/// <summary>Rol dentro de una empresa. Un rol es un conjunto de permisos, nada más.</summary>
public enum Rol
{
    /// <summary>Dueño de la cuenta: todos los permisos.</summary>
    Propietario = 1,

    /// <summary>Vende: opera sobre contactos, oportunidades y tareas.</summary>
    Comercial = 2,

    /// <summary>Consulta y exporta, no modifica.</summary>
    SoloLectura = 3,
}

public static class PermisosDeRol
{
    public static IReadOnlyList<string> De(Rol rol) => rol switch
    {
        Rol.Propietario => Permisos.Todos,
        Rol.Comercial =>
        [
            Permisos.ContactoLeer, Permisos.ContactoGestionar,
            Permisos.OportunidadLeer, Permisos.OportunidadGestionar,
            Permisos.TareaLeer, Permisos.TareaGestionar,
            Permisos.InformeLeer,

            // Ve las campañas y no las lanza. Ver una le hace falta: si a su cliente le llegó un correo
            // de una campaña, tiene que poder saberlo antes de llamarle, o llama a ciegas.
            Permisos.CampaniaLeer,
        ],
        Rol.SoloLectura =>
        [
            Permisos.ContactoLeer, Permisos.OportunidadLeer,
            Permisos.TareaLeer, Permisos.InformeLeer, Permisos.DatosExportar,
            Permisos.CampaniaLeer,
        ],
        _ => [],
    };
}

/// <summary>Nombres en castellano de los roles. La interfaz va en castellano.</summary>
public static class TextosRol
{
    public static string De(Rol rol) => rol switch
    {
        Rol.Propietario => "propietario",
        Rol.Comercial => "comercial",
        Rol.SoloLectura => "solo lectura",
        _ => "sin rol",
    };
}
