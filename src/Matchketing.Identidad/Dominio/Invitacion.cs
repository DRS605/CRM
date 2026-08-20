using System.Security.Cryptography;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Identidad.Dominio;

/// <summary>Se ha invitado a alguien a la empresa.</summary>
public sealed record InvitacionCreada(Guid InvitacionId, Guid EmpresaId, Rol Rol, DateTimeOffset OcurridoEn) : IEventoDominio;

/// <summary>
/// La invitación a entrar en una empresa: un enlace que se le pasa a la persona por donde se hable con
/// ella —correo, WhatsApp, en la mano— y que al abrirlo la deja dentro con el rol que se le puso.
///
/// **Por qué un enlace y no una contraseña provisional.** La alternativa habitual es que el
/// propietario cree la cuenta y le diga la contraseña a su compañero. Eso significa que el
/// propietario conoce la contraseña de otra persona, y a partir de ahí ya no se puede afirmar quién
/// hizo qué: se lleva por delante el registro de auditoría, que es la mitad de lo que sostiene este
/// producto. Con el enlace, la contraseña la elige quien la va a usar y nadie más la ve nunca.
///
/// **Por qué se guarda y el enlace de baja no.** `EnlaceBaja` es un token firmado sin tabla, y a
/// propósito no caduca: una baja tiene que funcionar dentro de tres años. Esto es lo contrario. Una
/// invitación es una llave de la empresa, así que necesita las tres cosas que un token firmado no
/// puede dar: **caducar**, **usarse una sola vez** y **poder retirarse** antes de que se use.
///
/// **Del token solo se guarda su huella.** En la tabla va un SHA-256, no el token. Quien lea la base
/// de datos —una copia de seguridad vieja, un volcado en el portátil de alguien— no se lleva llaves de
/// nadie. Se busca por la huella, que es determinista, así que el índice sigue sirviendo.
/// </summary>
public sealed class Invitacion : RaizAgregadoEmpresa<Guid>
{
    /// <summary>
    /// Días que vale. Una semana: suficiente para quien está de vacaciones, poco para que una
    /// invitación olvidada en un chat de hace dos años siga abriendo la puerta.
    /// </summary>
    public const int DiasDeVida = 7;

    /// <summary>Los caracteres que ocupa la empresa dentro del token (16 bytes en Base64Url).</summary>
    private const int LargoEmpresa = 22;

    private Invitacion(Guid id)
        : base(id, Guid.Empty)
    {
        Email = null!;
        HuellaToken = null!;
    }

    private Invitacion(Guid id, Guid empresaId, string email, Rol rol, Guid invitadoPor, string huella, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Email = email;
        Rol = rol;
        InvitadoPor = invitadoPor;
        HuellaToken = huella;
        CreadaEn = ahora;
        CaducaEn = ahora.AddDays(DiasDeVida);
    }

    /// <summary>A quién se invitó. La invitación vale **para ese correo** y para ningún otro.</summary>
    public string Email { get; private set; }

    public Rol Rol { get; private set; }

    public Guid InvitadoPor { get; private set; }

    /// <summary>SHA-256 del token, en hexadecimal. El token en claro no se guarda en ningún sitio.</summary>
    public string HuellaToken { get; private set; }

    public DateTimeOffset CreadaEn { get; private set; }

    public DateTimeOffset CaducaEn { get; private set; }

    public DateTimeOffset? AceptadaEn { get; private set; }

    public DateTimeOffset? RetiradaEn { get; private set; }

    /// <summary>Ni aceptada, ni retirada, ni caducada: es la única situación en la que sirve.</summary>
    public bool EstaViva(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        return AceptadaEn is null && RetiradaEn is null && CaducaEn > reloj.AhoraUtc;
    }

    /// <summary>
    /// Crea la invitación y devuelve **el token en claro por separado**, porque es la única vez que
    /// existe: la entidad solo se lleva su huella. Si quien llama no lo enseña ahora, no hay forma de
    /// recuperarlo y habrá que invitar otra vez.
    /// </summary>
    public static Resultado<(Invitacion Invitacion, string Token)> Crear(
        Guid empresaId, string? email, Rol rol, Guid invitadoPor, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        // Nombre completo: la propiedad `Email` de esta clase oculta al tipo `Email` del núcleo.
        var correo = Nucleo.Comun.Email.Crear(email);
        if (correo.Fallido)
        {
            return Resultado.Fallo<(Invitacion, string)>(correo.Error!);
        }

        if (!System.Enum.IsDefined(rol))
        {
            return Resultado.Fallo<(Invitacion, string)>(
                Error.Validacion("invitacion.rol_invalido", "Ese rol no existe."));
        }

        var token = TokenNuevo(empresaId);
        var invitacion = new Invitacion(
            Guid.NewGuid(), empresaId, correo.Valor.Valor, rol, invitadoPor, Huella(token), reloj.AhoraUtc);
        invitacion.RegistrarEvento(new InvitacionCreada(invitacion.Id, empresaId, rol, reloj.AhoraUtc));

        return Resultado.Ok((invitacion, token));
    }

    /// <summary>
    /// La marca de usada. Se llama **después** de crear la membresía y en la misma transacción: una
    /// invitación consumida sin membresía dejaría a la persona fuera y sin poder volver a intentarlo.
    /// </summary>
    public Resultado Aceptar(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (AceptadaEn is not null)
        {
            return Resultado.Fallo(Error.Conflicto("invitacion.ya_aceptada", "Esta invitación ya se ha usado."));
        }

        if (RetiradaEn is not null)
        {
            return Resultado.Fallo(Error.Conflicto("invitacion.retirada", "Esta invitación se ha retirado."));
        }

        if (CaducaEn <= reloj.AhoraUtc)
        {
            return Resultado.Fallo(Error.Conflicto("invitacion.caducada", "Esta invitación ha caducado. Pide otra."));
        }

        AceptadaEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>Retira una invitación que todavía no se ha usado. Una ya aceptada no se retira: se quita la membresía.</summary>
    public Resultado Retirar(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (AceptadaEn is not null)
        {
            return Resultado.Fallo(Error.Conflicto(
                "invitacion.ya_aceptada", "Esta invitación ya se usó: lo que hay que quitar es el acceso de esa persona."));
        }

        RetiradaEn ??= reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>
    /// La empresa que va dentro del token, para que el endpoint público pueda fijarla **antes** de
    /// tocar la base de datos. Sin eso la RLS de PostgreSQL no devuelve ninguna fila y la invitación
    /// no se encontraría nunca: es el mismo truco que el enlace de baja y el píxel de apertura, y así
    /// la consulta se hace con las dos barreras puestas en vez de sin ninguna.
    /// </summary>
    public static Guid? EmpresaDelToken(string? token)
    {
        if (token is null || token.Length <= LargoEmpresa)
        {
            return null;
        }

        var bytes = Base64Url.Descodificar(token[..LargoEmpresa]);
        return bytes is { Length: 16 } ? new Guid(bytes) : null;
    }

    /// <summary>La huella con la que se busca en la tabla. Determinista a propósito: es un índice.</summary>
    public static string Huella(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>
    /// Empresa (16 bytes) + 32 bytes al azar. Los 32 bytes son los que hacen que no se pueda adivinar;
    /// la empresa va delante para poder fijar el inquilino antes de la consulta.
    /// </summary>
    private static string TokenNuevo(Guid empresaId) =>
        Base64Url.Codificar(empresaId.ToByteArray()) + Base64Url.Codificar(RandomNumberGenerator.GetBytes(32));
}
