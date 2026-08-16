using Email_ = Matchketing.Nucleo.Comun.Email;
using Telefono_ = Matchketing.Nucleo.Comun.Telefono;
using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Contactos.Dominio;

public sealed record ContactoCreado(Guid ContactoId, Guid EmpresaId, string Origen, DateTimeOffset OcurridoEn) : IEventoDominio;

public sealed record ContactoFusionado(Guid SupervivienteId, Guid AbsorbidoId, Guid EmpresaId, DateTimeOffset OcurridoEn) : IEventoDominio;

/// <summary>
/// Una persona. Puede pertenecer a una <see cref="Cuenta"/> o no (B2C). Necesita al menos un medio
/// de contacto —correo o teléfono— porque un contacto al que no se puede llamar ni escribir no
/// sirve de nada (invariante C1).
/// </summary>
public sealed class Contacto : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaNombre = 160;
    public const int LongitudMaximaCargo = 100;
    public const int LongitudMaximaOrigen = 60;

    private Contacto(Guid id)
        : base(id, Guid.Empty) => Nombre = null!;

    private Contacto(Guid id, Guid empresaId, string nombre, string? email, string? telefono, string? cargo, Guid? cuentaId, string origen, Guid? propietarioId, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Nombre = nombre;
        Email = email;
        Telefono = telefono;
        Cargo = cargo;
        CuentaId = cuentaId;
        Origen = origen;
        PropietarioId = propietarioId;
        Estado = EstadoContacto.Lead;
        Activo = true;
        CreadoEn = ahora;
        ActualizadoEn = ahora;
    }

    public string Nombre { get; private set; }

    /// <summary>Correo ya normalizado (minúsculas). Clave de deduplicación.</summary>
    public string? Email { get; private set; }

    /// <summary>Teléfono ya normalizado (+34…). La otra clave de deduplicación.</summary>
    public string? Telefono { get; private set; }

    public string? Cargo { get; private set; }

    public Guid? CuentaId { get; private set; }

    /// <summary>De dónde salió: formulario, feria, recomendación, importación…</summary>
    public string Origen { get; private set; } = "manual";

    public Guid? PropietarioId { get; private set; }

    public EstadoContacto Estado { get; private set; }

    public bool Activo { get; private set; }

    /// <summary>Si se fusionó dentro de otro, aquí queda el rastro. Nunca se borra un contacto.</summary>
    public Guid? FusionadoEnId { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public DateTimeOffset ActualizadoEn { get; private set; }

    public static Resultado<Contacto> Crear(
        Guid empresaId, string? nombre, string? email, string? telefono, string? cargo,
        Guid? cuentaId, string? origen, Guid? propietarioId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var datos = Normalizar(nombre, email, telefono, cargo, origen);
        if (datos.Fallido)
        {
            return Resultado.Fallo<Contacto>(datos.Error!);
        }

        var d = datos.Valor;
        var contacto = new Contacto(
            Guid.NewGuid(), empresaId, d.Nombre, d.Email, d.Telefono, d.Cargo, cuentaId, d.Origen, propietarioId, reloj.AhoraUtc);
        contacto.RegistrarEvento(new ContactoCreado(contacto.Id, empresaId, d.Origen, reloj.AhoraUtc));
        return Resultado.Ok(contacto);
    }

    public Resultado Actualizar(string? nombre, string? email, string? telefono, string? cargo, Guid? cuentaId, Guid? propietarioId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var datos = Normalizar(nombre, email, telefono, cargo, Origen);
        if (datos.Fallido)
        {
            return Resultado.Fallo(datos.Error!);
        }

        var d = datos.Valor;
        Nombre = d.Nombre;
        Email = d.Email;
        Telefono = d.Telefono;
        Cargo = d.Cargo;
        CuentaId = cuentaId;
        PropietarioId = propietarioId;
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    public Resultado CambiarEstado(EstadoContacto estado, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (Estado == EstadoContacto.Baja)
        {
            return Resultado.Fallo(Error.Conflicto(
                "contacto.dado_de_baja",
                "El contacto pidió no recibir más comunicaciones; solo él puede volver a darse de alta."));
        }

        Estado = estado;
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>Baja a petición del contacto. Es irreversible desde nuestro lado (invariante G2).</summary>
    public void DarDeBaja(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        Estado = EstadoContacto.Baja;
        ActualizadoEn = reloj.AhoraUtc;
    }

    public void Desactivar(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        Activo = false;
        ActualizadoEn = reloj.AhoraUtc;
    }

    /// <summary>
    /// Absorbe los datos de un duplicado: rellena solo los huecos, nunca pisa lo que ya hay. El
    /// absorbido se desactiva y guarda a quién se fusionó; las actividades las mueve el caso de uso.
    /// </summary>
    public Resultado Absorber(Contacto absorbido, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(absorbido);
        ArgumentNullException.ThrowIfNull(reloj);

        if (absorbido.Id == Id)
        {
            return Resultado.Fallo(Error.Validacion("contacto.fusion_consigo_mismo", "Un contacto no se puede fusionar consigo mismo."));
        }

        if (absorbido.EmpresaId != EmpresaId)
        {
            return Resultado.Fallo(Error.Prohibido("contacto.fusion_otra_empresa", "No se pueden fusionar contactos de empresas distintas."));
        }

        if (!absorbido.Activo)
        {
            return Resultado.Fallo(Error.Conflicto("contacto.ya_fusionado", "Ese contacto ya está fusionado o desactivado."));
        }

        Email ??= absorbido.Email;
        Telefono ??= absorbido.Telefono;
        Cargo ??= absorbido.Cargo;
        CuentaId ??= absorbido.CuentaId;
        PropietarioId ??= absorbido.PropietarioId;

        // Gana el estado más avanzado: si uno de los dos ya es cliente, el superviviente lo es.
        if (absorbido.Estado == EstadoContacto.Cliente && Estado == EstadoContacto.Lead)
        {
            Estado = EstadoContacto.Cliente;
        }

        // La baja manda siempre: si cualquiera de los dos la pidió, el superviviente queda de baja.
        if (absorbido.Estado == EstadoContacto.Baja)
        {
            Estado = EstadoContacto.Baja;
        }

        absorbido.Activo = false;
        absorbido.FusionadoEnId = Id;
        absorbido.ActualizadoEn = reloj.AhoraUtc;
        ActualizadoEn = reloj.AhoraUtc;

        RegistrarEvento(new ContactoFusionado(Id, absorbido.Id, EmpresaId, reloj.AhoraUtc));
        return Resultado.Ok();
    }

    private static Resultado<(string Nombre, string? Email, string? Telefono, string? Cargo, string Origen)> Normalizar(
        string? nombre, string? email, string? telefono, string? cargo, string? origen)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Resultado.Fallo<(string, string?, string?, string?, string)>(
                Error.Validacion("contacto.nombre_vacio", "El nombre del contacto es obligatorio."));
        }

        if (nombre.Trim().Length > LongitudMaximaNombre)
        {
            return Resultado.Fallo<(string, string?, string?, string?, string)>(
                Error.Validacion("contacto.nombre_largo", "El nombre del contacto es demasiado largo."));
        }

        string? correo = null;
        if (!string.IsNullOrWhiteSpace(email))
        {
            var r = Email_.Crear(email);
            if (r.Fallido)
            {
                return Resultado.Fallo<(string, string?, string?, string?, string)>(r.Error!);
            }

            correo = r.Valor.Valor;
        }

        string? tlf = null;
        if (!string.IsNullOrWhiteSpace(telefono))
        {
            var r = Telefono_.Crear(telefono);
            if (r.Fallido)
            {
                return Resultado.Fallo<(string, string?, string?, string?, string)>(r.Error!);
            }

            tlf = r.Valor.Valor;
        }

        // C1: sin correo ni teléfono no hay forma de contactar, y entonces no es un contacto.
        if (correo is null && tlf is null)
        {
            return Resultado.Fallo<(string, string?, string?, string?, string)>(
                Error.Validacion("contacto.sin_medio", "Hace falta al menos un correo o un teléfono."));
        }

        var cargoLimpio = string.IsNullOrWhiteSpace(cargo) ? null : cargo.Trim();
        if (cargoLimpio?.Length > LongitudMaximaCargo)
        {
            return Resultado.Fallo<(string, string?, string?, string?, string)>(
                Error.Validacion("contacto.cargo_largo", "El cargo es demasiado largo."));
        }

        var origenLimpio = string.IsNullOrWhiteSpace(origen) ? "manual" : origen.Trim().ToLowerInvariant();
        if (origenLimpio.Length > LongitudMaximaOrigen)
        {
            origenLimpio = origenLimpio[..LongitudMaximaOrigen];
        }

        return Resultado.Ok((nombre.Trim(), correo, tlf, cargoLimpio, origenLimpio));
    }
}
