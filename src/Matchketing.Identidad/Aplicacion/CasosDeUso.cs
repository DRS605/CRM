using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Identidad.Aplicacion;

/// <summary>Registro, inicio de sesión y elección de la empresa activa.</summary>
public sealed class ServicioIdentidad(
    IRepositorioUsuarios usuarios,
    IRepositorioMembresias membresias,
    IHasherContrasena hasher,
    IGeneradorTokens tokens,
    IUnidadDeTrabajo unidad,
    IReloj reloj)
{
    /// <summary>Da de alta una cuenta y devuelve sesión iniciada: registrarse y entrar es un solo paso.</summary>
    public async Task<Resultado<RespuestaSesion>> RegistrarAsync(string? email, string? contrasena, string? nombre, CancellationToken ct = default)
    {
        var correo = Email.Crear(email);
        if (correo.Fallido)
        {
            return Resultado.Fallo<RespuestaSesion>(correo.Error!);
        }

        if (await usuarios.ExisteEmailAsync(correo.Valor.Valor, ct).ConfigureAwait(false))
        {
            return Resultado.Fallo<RespuestaSesion>(
                Error.Conflicto("usuario.email_repetido", "Ya hay una cuenta con ese correo electrónico."));
        }

        var creado = Usuario.Registrar(email, contrasena, nombre, hasher.Hashear, reloj);
        if (creado.Fallido)
        {
            return Resultado.Fallo<RespuestaSesion>(creado.Error!);
        }

        // Registrarse **es** entrar: este método devuelve la sesión ya iniciada. Sin esta línea, la
        // lista del equipo enseñaba «no ha entrado nunca» junto al nombre de quien estaba mirando la
        // pantalla en ese momento, porque el último acceso solo se apuntaba al pasar por el login.
        creado.Valor.RegistrarAcceso(reloj);

        usuarios.Anadir(creado.Valor);
        await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

        return Resultado.Ok(ConstruirSesion(creado.Valor, null, null));
    }

    /// <summary>
    /// Inicia sesión. El mensaje de error es el mismo para correo inexistente y contraseña
    /// incorrecta: decir cuál de los dos falla regala información a quien prueba correos.
    /// </summary>
    public async Task<Resultado<RespuestaSesion>> IniciarSesionAsync(string? email, string? contrasena, CancellationToken ct = default)
    {
        var credencialesInvalidas = Error.NoAutorizado("sesion.credenciales", "El correo o la contraseña no son correctos.");

        var correo = Email.Crear(email);
        if (correo.Fallido)
        {
            return Resultado.Fallo<RespuestaSesion>(credencialesInvalidas);
        }

        var usuario = await usuarios.BuscarPorEmailAsync(correo.Valor.Valor, ct).ConfigureAwait(false);
        if (usuario is null || !usuario.Activo || !hasher.Verificar(contrasena ?? string.Empty, usuario.HashContrasena))
        {
            return Resultado.Fallo<RespuestaSesion>(credencialesInvalidas);
        }

        usuario.RegistrarAcceso(reloj);
        await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

        return Resultado.Ok(ConstruirSesion(usuario, null, null));
    }

    /// <summary>Empresas donde el usuario tiene membresía activa.</summary>
    public async Task<IReadOnlyList<Membresia>> MembresiasDeAsync(Guid usuarioId, CancellationToken ct = default) =>
        await membresias.DeUsuarioAsync(usuarioId, ct).ConfigureAwait(false);

    /// <summary>
    /// Emite un token nuevo con la empresa activa dentro. A partir de aquí el resto de la API
    /// resuelve el tenant del propio token (invariante T2).
    /// </summary>
    public async Task<Resultado<RespuestaSesion>> SeleccionarEmpresaAsync(Guid usuarioId, Guid empresaId, string nombreEmpresa, CancellationToken ct = default)
    {
        var usuario = await usuarios.BuscarPorIdAsync(usuarioId, ct).ConfigureAwait(false);
        if (usuario is null)
        {
            return Resultado.Fallo<RespuestaSesion>(Error.NoEncontrado("usuario.no_encontrado", "El usuario no existe."));
        }

        var membresia = await membresias.BuscarAsync(usuarioId, empresaId, ct).ConfigureAwait(false);
        if (membresia is null || !membresia.Activa)
        {
            return Resultado.Fallo<RespuestaSesion>(
                Error.Prohibido("empresa.sin_acceso", "No tienes acceso a esa empresa."));
        }

        return Resultado.Ok(ConstruirSesion(usuario, membresia, nombreEmpresa));
    }

    /// <summary>
    /// Cambio de contraseña con la actual por delante. No emite token nuevo: el que la persona tiene
    /// en la mano sigue valiendo hasta que caduque, y obligar a entrar otra vez justo después de
    /// cambiarla solo consigue que parezca que algo ha ido mal.
    /// </summary>
    public async Task<Resultado> CambiarContrasenaAsync(Guid usuarioId, string? actual, string? nueva, CancellationToken ct = default)
    {
        var usuario = await usuarios.BuscarPorIdAsync(usuarioId, ct).ConfigureAwait(false);
        if (usuario is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("usuario.no_encontrado", "El usuario no existe."));
        }

        var r = usuario.CambiarContrasena(actual, nueva, hasher.Verificar, hasher.Hashear, reloj);
        if (r.Fallido)
        {
            return r;
        }

        await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
        return Resultado.Ok();
    }

    public void AnadirMembresia(Membresia membresia) => membresias.Anadir(membresia);

    private RespuestaSesion ConstruirSesion(Usuario usuario, Membresia? membresia, string? nombreEmpresa)
    {
        var token = tokens.Generar(usuario, membresia, nombreEmpresa);
        return new RespuestaSesion(
            token.Token,
            token.ExpiraEn,
            new UsuarioResumen(usuario.Id, usuario.Nombre, usuario.Email),
            membresia?.EmpresaId,
            nombreEmpresa,
            membresia?.Permisos ?? []);
    }
}
