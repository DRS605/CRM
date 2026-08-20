using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Identidad.Aplicacion;

/// <summary>Una persona del equipo, con su rol y su zona. Es lo que se enseña en Ajustes › Equipo.</summary>
public sealed record MiembroEquipo(
    Guid MembresiaId,
    Guid UsuarioId,
    string Nombre,
    string Email,
    Rol Rol,
    bool Activa,
    IReadOnlyList<string> Zonas,
    DateTimeOffset? UltimoAccesoEn);

/// <summary>Una invitación que aún no se ha usado. Sin el token: ese solo existe una vez.</summary>
public sealed record InvitacionPendiente(Guid Id, string Email, Rol Rol, DateTimeOffset CaducaEn);

/// <summary>Lo que ve quien abre el enlace, antes de decidir si entra.</summary>
public sealed record InvitacionAbierta(string Email, Rol Rol, Guid EmpresaId, bool YaTieneCuenta);

/// <summary>
/// El equipo de una empresa: quién entra, con qué rol y a qué zona.
///
/// Existía todo menos la puerta. `Membresia` traía rol, zonas, `CambiarRol`, `FijarZonas` y
/// `Desactivar` desde el módulo 1, y `PermisosDeRol` reparte once permisos entre tres roles. Nada de
/// eso tenía llamante: la única membresía que se creaba nunca era la del **propietario al crear la
/// empresa**, así que ninguna empresa podía tener dos personas, los roles Comercial y Solo lectura
/// eran inalcanzables, y las zonas —el primer factor del reparto de leads del Match— estaban siempre
/// vacías. Un CRM de un solo usuario no es lo que dice el alcance del MVP.
/// </summary>
public sealed class ServicioEquipo(
    IRepositorioUsuarios usuarios,
    IRepositorioMembresias membresias,
    IRepositorioInvitaciones invitaciones,
    IConsultaPersonas personas,
    IHasherContrasena hasher,
    IReloj reloj)
{
    /// <summary>La lista del equipo, propietarios primero y las bajas al final.</summary>
    public async Task<IReadOnlyList<MiembroEquipo>> EquipoAsync(Guid empresaId, CancellationToken ct = default)
    {
        var lista = await membresias.DeEmpresaAsync(empresaId, ct).ConfigureAwait(false);
        if (lista.Count == 0)
        {
            return [];
        }

        var fichas = await personas.DeIdsAsync(lista.Select(m => m.UsuarioId).ToArray(), ct).ConfigureAwait(false);
        var porId = fichas.ToDictionary(p => p.Id);

        return lista
            .Where(m => porId.ContainsKey(m.UsuarioId))
            .Select(m => new MiembroEquipo(
                m.Id, m.UsuarioId, porId[m.UsuarioId].Nombre, porId[m.UsuarioId].Email,
                m.Rol, m.Activa, m.ListaZonas, porId[m.UsuarioId].UltimoAccesoEn))
            .OrderBy(m => m.Activa ? 0 : 1)
            .ThenBy(m => (int)m.Rol)
            .ThenBy(m => m.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<InvitacionPendiente>> PendientesAsync(Guid empresaId, CancellationToken ct = default)
    {
        var vivas = await invitaciones.VivasDeEmpresaAsync(empresaId, ct).ConfigureAwait(false);
        return vivas
            .Where(i => i.EstaViva(reloj))
            .OrderBy(i => i.CaducaEn)
            .Select(i => new InvitacionPendiente(i.Id, i.Email, i.Rol, i.CaducaEn))
            .ToList();
    }

    /// <summary>
    /// Invita a alguien y devuelve **el token en claro**, que es la única vez que existe: quien llama
    /// tiene que enseñarlo en ese momento.
    ///
    /// Invitar dos veces al mismo correo no acumula invitaciones: la anterior se retira y el enlace
    /// viejo deja de valer. Dos llaves vivas de la misma puerta es una llave que nadie sabe que existe.
    /// </summary>
    public async Task<Resultado<(Invitacion Invitacion, string Token)>> InvitarAsync(
        Guid empresaId, string? email, Rol rol, Guid invitadoPor, CancellationToken ct = default)
    {
        var correo = Nucleo.Comun.Email.Crear(email);
        if (correo.Fallido)
        {
            return Resultado.Fallo<(Invitacion, string)>(correo.Error!);
        }

        var yaEsta = await usuarios.BuscarPorEmailAsync(correo.Valor.Valor, ct).ConfigureAwait(false);
        if (yaEsta is not null
            && await membresias.BuscarAsync(yaEsta.Id, empresaId, ct).ConfigureAwait(false) is { Activa: true })
        {
            return Resultado.Fallo<(Invitacion, string)>(
                Error.Conflicto("equipo.ya_es_miembro", "Esa persona ya está en el equipo."));
        }

        foreach (var anterior in await invitaciones.VivasDeEmpresaAsync(empresaId, ct).ConfigureAwait(false))
        {
            if (anterior.Email == correo.Valor.Valor && anterior.EstaViva(reloj))
            {
                anterior.Retirar(reloj);
            }
        }

        var creada = Invitacion.Crear(empresaId, correo.Valor.Valor, rol, invitadoPor, reloj);
        if (creada.Fallido)
        {
            return creada;
        }

        invitaciones.Anadir(creada.Valor.Invitacion);
        return creada;
    }

    /// <summary>Lo que hay detrás del enlace, sin consumirlo. Sirve para pintar la pantalla de bienvenida.</summary>
    public async Task<Resultado<InvitacionAbierta>> AbrirAsync(string? token, CancellationToken ct = default)
    {
        var invitacion = await PorTokenAsync(token, ct).ConfigureAwait(false);
        if (invitacion is null || !invitacion.EstaViva(reloj))
        {
            return Resultado.Fallo<InvitacionAbierta>(Error.NoEncontrado(
                "invitacion.no_vale", "Esta invitación ya no vale. Pide otra a quien te invitó."));
        }

        var cuenta = await usuarios.BuscarPorEmailAsync(invitacion.Email, ct).ConfigureAwait(false);
        return Resultado.Ok(new InvitacionAbierta(invitacion.Email, invitacion.Rol, invitacion.EmpresaId, cuenta is not null));
    }

    /// <summary>
    /// Acepta la invitación. Si la persona no tiene cuenta, la crea aquí con **su** contraseña: quien
    /// invita no la ve ni la elige, y por eso el registro de auditoría sigue pudiendo afirmar quién
    /// hizo qué.
    ///
    /// Si ya tiene cuenta, hace falta **su contraseña**. El enlace solo prueba que alguien tiene el
    /// enlace, no que sea esa persona: sin la contraseña, reenviar el mensaje a un tercero le daría
    /// acceso a la empresa con la cuenta de otro. Por eso este caso de uso comprueba una contraseña, y
    /// por eso su endpoint lleva el mismo límite de intentos que el de entrar.
    /// </summary>
    public async Task<Resultado<Usuario>> AceptarAsync(
        string? token, string? nombre, string? contrasena, CancellationToken ct = default)
    {
        var invitacion = await PorTokenAsync(token, ct).ConfigureAwait(false);
        if (invitacion is null)
        {
            return Resultado.Fallo<Usuario>(Error.NoEncontrado(
                "invitacion.no_vale", "Esta invitación ya no vale. Pide otra a quien te invitó."));
        }

        var cuenta = await usuarios.BuscarPorEmailAsync(invitacion.Email, ct).ConfigureAwait(false);
        if (cuenta is not null
            && (!cuenta.Activo || !hasher.Verificar(contrasena ?? string.Empty, cuenta.HashContrasena)))
        {
            return Resultado.Fallo<Usuario>(Error.NoAutorizado(
                "invitacion.credenciales",
                $"Ya hay una cuenta con {invitacion.Email}: hace falta su contraseña para entrar en la empresa."));
        }

        // Se comprueba que la invitación sirve **antes** de crear nada, para no dejar por ahí una cuenta
        // recién hecha que no ha entrado en ninguna empresa.
        if (!invitacion.EstaViva(reloj))
        {
            return Resultado.Fallo<Usuario>(invitacion.Aceptar(reloj).Error!);
        }

        if (cuenta is null)
        {
            var creado = Usuario.Registrar(invitacion.Email, contrasena, nombre, hasher.Hashear, reloj);
            if (creado.Fallido)
            {
                return creado;
            }

            cuenta = creado.Valor;
            usuarios.Anadir(cuenta);
        }

        // Y se marca como usada al final, cuando ya no puede fallar nada: marcarla antes la gastaría en
        // cuanto la contraseña no valiera, y quedaría en manos de que quien llama no guarde. Eso
        // funciona hoy y se rompe el día que alguien añada un `GuardarCambiosAsync` de más.
        var aceptada = invitacion.Aceptar(reloj);
        if (aceptada.Fallido)
        {
            return Resultado.Fallo<Usuario>(aceptada.Error!);
        }

        // Si estuvo en el equipo y se le quitó el acceso, se reactiva la membresía que ya había: crear
        // otra chocaría con el índice único de usuario+empresa, y perder la fecha de alta original no
        // aporta nada.
        var membresia = await membresias.BuscarAsync(cuenta.Id, invitacion.EmpresaId, ct).ConfigureAwait(false);
        if (membresia is null)
        {
            membresias.Anadir(Membresia.Crear(cuenta.Id, invitacion.EmpresaId, invitacion.Rol, reloj));
        }
        else
        {
            membresia.Reactivar(invitacion.Rol);
        }

        return Resultado.Ok(cuenta);
    }

    /// <summary>
    /// Cambia el rol de alguien del equipo. No el propio: si el último propietario se baja a comercial
    /// se queda una empresa que nadie puede administrar, y deshacerlo pediría entrar en la base de
    /// datos. Que lo haga otra persona con permiso es la salida sana.
    /// </summary>
    public async Task<Resultado> CambiarRolAsync(
        Guid empresaId, Guid membresiaId, Rol rol, Guid quienPide, CancellationToken ct = default)
    {
        if (!System.Enum.IsDefined(rol))
        {
            return Resultado.Fallo(Error.Validacion("equipo.rol_invalido", "Ese rol no existe."));
        }

        var membresia = await membresias.BuscarPorIdAsync(membresiaId, empresaId, ct).ConfigureAwait(false);
        if (membresia is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("equipo.no_encontrado", "Esa persona no está en el equipo."));
        }

        if (membresia.UsuarioId == quienPide)
        {
            return Resultado.Fallo(Error.Conflicto(
                "equipo.no_a_ti_mismo", "Tu propio rol lo tiene que cambiar otra persona con permiso."));
        }

        if (membresia.Rol == Rol.Propietario && rol != Rol.Propietario
            && await membresias.ContarPropietariosAsync(empresaId, ct).ConfigureAwait(false) <= 1)
        {
            return Resultado.Fallo(Error.Conflicto(
                "equipo.ultimo_propietario", "Es el único propietario: nombra a otro antes de cambiarle el rol."));
        }

        return membresia.CambiarRol(rol);
    }

    /// <summary>
    /// Las provincias que cubre una persona. Es el primer factor del reparto de leads del Match, y
    /// hasta ahora no había forma de rellenarlo: el reparto por zona no repartía por zona.
    /// </summary>
    public async Task<Resultado> FijarZonasAsync(Guid empresaId, Guid membresiaId, string? zonas, CancellationToken ct = default)
    {
        var membresia = await membresias.BuscarPorIdAsync(membresiaId, empresaId, ct).ConfigureAwait(false);
        return membresia is null
            ? Resultado.Fallo(Error.NoEncontrado("equipo.no_encontrado", "Esa persona no está en el equipo."))
            : membresia.FijarZonas(zonas);
    }

    /// <summary>
    /// Le quita el acceso a la empresa. **No borra al usuario ni sus datos**: sus contactos siguen
    /// asignados a su nombre y su rastro en la auditoría y en la cronología no se toca, porque son
    /// hechos. Volver a invitarla reactiva la misma membresía.
    /// </summary>
    public async Task<Resultado> QuitarAsync(Guid empresaId, Guid membresiaId, Guid quienPide, CancellationToken ct = default)
    {
        var membresia = await membresias.BuscarPorIdAsync(membresiaId, empresaId, ct).ConfigureAwait(false);
        if (membresia is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("equipo.no_encontrado", "Esa persona no está en el equipo."));
        }

        if (membresia.UsuarioId == quienPide)
        {
            return Resultado.Fallo(Error.Conflicto(
                "equipo.no_a_ti_mismo", "No puedes quitarte a ti mismo el acceso a la empresa."));
        }

        if (!membresia.Activa)
        {
            return Resultado.Ok();
        }

        if (membresia.Rol == Rol.Propietario
            && await membresias.ContarPropietariosAsync(empresaId, ct).ConfigureAwait(false) <= 1)
        {
            return Resultado.Fallo(Error.Conflicto(
                "equipo.ultimo_propietario", "Es el único propietario: una empresa sin propietario no se puede administrar."));
        }

        membresia.Desactivar();
        return Resultado.Ok();
    }

    public async Task<Resultado> RetirarInvitacionAsync(Guid empresaId, Guid invitacionId, CancellationToken ct = default)
    {
        var invitacion = await invitaciones.BuscarPorIdAsync(invitacionId, empresaId, ct).ConfigureAwait(false);
        return invitacion is null
            ? Resultado.Fallo(Error.NoEncontrado("invitacion.no_encontrada", "Esa invitación no existe."))
            : invitacion.Retirar(reloj);
    }

    private async Task<Invitacion?> PorTokenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return await invitaciones.BuscarPorHuellaAsync(Invitacion.Huella(token), ct).ConfigureAwait(false);
    }
}
