using FluentAssertions;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Xunit;

namespace Matchketing.Identidad.Tests;

/// <summary>
/// Las reglas del equipo. Casi todas existen para que no se pueda llegar a un estado del que no se
/// pueda salir sin entrar en la base de datos a mano.
/// </summary>
public sealed class PruebasServicioEquipo
{
    private static readonly Guid Empresa = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Otra = Guid.Parse("22222222-3333-4444-5555-666666666666");

    private readonly RelojFijo reloj = new(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly RepoUsuarios usuarios = new();
    private readonly RepoMembresias membresias = new();
    private readonly RepoInvitaciones invitaciones = new();
    private readonly HasherDeJuguete hasher = new();

    private ServicioEquipo Servicio => new(usuarios, membresias, invitaciones, usuarios, hasher, reloj);

    [Fact]
    public async Task Invitar_y_aceptar_mete_a_la_persona_en_el_equipo()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);

        var invitada = await Servicio.InvitarAsync(Empresa, "vicent@ribera.es", Rol.Comercial, duena.UsuarioId);
        invitada.Exito.Should().BeTrue();

        var aceptada = await Servicio.AceptarAsync(invitada.Valor.Token, "Vicent Llopis", "Vinaros2026");
        aceptada.Exito.Should().BeTrue();
        aceptada.Valor.Email.Should().Be("vicent@ribera.es");

        var equipo = await Servicio.EquipoAsync(Empresa);
        equipo.Should().HaveCount(2);
        equipo.Should().Contain(m => m.Email == "vicent@ribera.es" && m.Rol == Rol.Comercial && m.Activa);

        // Y la contraseña la eligió ella: quien invitó no la ve ni la elige, que es lo que permite
        // seguir afirmando quién hizo qué en la auditoría.
        hasher.Verificar("Vinaros2026", aceptada.Valor.HashContrasena).Should().BeTrue();
    }

    [Fact]
    public async Task Una_invitacion_no_se_puede_usar_dos_veces()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var invitada = await Servicio.InvitarAsync(Empresa, "vicent@ribera.es", Rol.Comercial, duena.UsuarioId);

        await Servicio.AceptarAsync(invitada.Valor.Token, "Vicent Llopis", "Vinaros2026");
        var segunda = await Servicio.AceptarAsync(invitada.Valor.Token, "Vicent Llopis", "Vinaros2026");

        segunda.Error!.Codigo.Should().Be("invitacion.ya_aceptada");

        // Con la contraseña equivocada el error es otro, y en ese orden a propósito: a quien no puede
        // demostrar quién es no se le cuenta si la invitación existió y ya se usó.
        (await Servicio.AceptarAsync(invitada.Valor.Token, "Vicent Llopis", "MeLoInvento1"))
            .Error!.Codigo.Should().Be("invitacion.credenciales");
    }

    [Fact]
    public async Task Con_una_cuenta_ya_existente_hace_falta_su_contrasena()
    {
        // El enlace prueba que alguien tiene el enlace, no que sea esa persona. Sin esta comprobación,
        // reenviar el mensaje a un tercero le daría acceso a la empresa **con la cuenta de otro**.
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var vicent = Usuario.Registrar("vicent@ribera.es", "Vinaros2026", "Vicent Llopis", hasher.Hashear, reloj).Valor;
        usuarios.Anadir(vicent);

        var invitada = await Servicio.InvitarAsync(Empresa, "vicent@ribera.es", Rol.Comercial, duena.UsuarioId);

        var conOtra = await Servicio.AceptarAsync(invitada.Valor.Token, null, "MeLoInvento1");
        conOtra.Error!.Codigo.Should().Be("invitacion.credenciales");
        conOtra.Error!.Tipo.Should().Be(Nucleo.Resultados.TipoError.NoAutorizado);
        (await Servicio.EquipoAsync(Empresa)).Should().HaveCount(1, "no ha entrado nadie");

        var conLaSuya = await Servicio.AceptarAsync(invitada.Valor.Token, null, "Vinaros2026");
        conLaSuya.Exito.Should().BeTrue();
        conLaSuya.Valor.Id.Should().Be(vicent.Id, "se usa la cuenta que ya había, no se crea otra");
    }

    [Fact]
    public async Task Invitar_a_quien_ya_esta_en_el_equipo_no_tiene_sentido()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);

        var r = await Servicio.InvitarAsync(Empresa, "marta@ribera.es", Rol.Comercial, duena.UsuarioId);

        r.Error!.Codigo.Should().Be("equipo.ya_es_miembro");
    }

    [Fact]
    public async Task Invitar_dos_veces_al_mismo_correo_deja_valiendo_solo_el_enlace_nuevo()
    {
        // Dos llaves vivas de la misma puerta son una llave que nadie sabe que existe.
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);

        var primera = await Servicio.InvitarAsync(Empresa, "vicent@ribera.es", Rol.SoloLectura, duena.UsuarioId);
        var segunda = await Servicio.InvitarAsync(Empresa, "vicent@ribera.es", Rol.Comercial, duena.UsuarioId);

        (await Servicio.PendientesAsync(Empresa)).Should().ContainSingle()
            .Which.Rol.Should().Be(Rol.Comercial);

        (await Servicio.AceptarAsync(primera.Valor.Token, "Vicent", "Vinaros2026"))
            .Error!.Codigo.Should().Be("invitacion.retirada");

        (await Servicio.AceptarAsync(segunda.Valor.Token, "Vicent", "Vinaros2026")).Exito.Should().BeTrue();
    }

    [Fact]
    public async Task Volver_a_invitar_a_quien_se_le_quito_el_acceso_reactiva_su_membresia()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var vicent = Alta("vicent@ribera.es", "Vicent Llopis", Rol.Comercial, Empresa);

        (await Servicio.QuitarAsync(Empresa, vicent.Id, duena.UsuarioId)).Exito.Should().BeTrue();

        var invitada = await Servicio.InvitarAsync(Empresa, "vicent@ribera.es", Rol.SoloLectura, duena.UsuarioId);
        (await Servicio.AceptarAsync(invitada.Valor.Token, null, "Levante2026")).Exito.Should().BeTrue();

        var equipo = await Servicio.EquipoAsync(Empresa);
        equipo.Should().HaveCount(2, "no se ha creado una segunda membresía, se ha reactivado la que había");
        equipo.Single(m => m.Email == "vicent@ribera.es").Activa.Should().BeTrue();
        equipo.Single(m => m.Email == "vicent@ribera.es").Rol.Should().Be(Rol.SoloLectura, "con el rol de la invitación nueva");
    }

    [Fact]
    public async Task Al_ultimo_propietario_no_se_le_puede_bajar_el_rol_ni_quitarle_el_acceso()
    {
        // Una empresa sin propietario es una empresa que nadie puede administrar, y volver atrás
        // pediría entrar en la base de datos a mano.
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var otra = Alta("ana@ribera.es", "Ana Gil", Rol.Propietario, Empresa);

        // Con dos propietarios sí se puede.
        (await Servicio.CambiarRolAsync(Empresa, otra.Id, Rol.Comercial, duena.UsuarioId)).Exito.Should().BeTrue();

        // Con uno, no: ni cambiándole el rol ni quitándole el acceso.
        (await Servicio.CambiarRolAsync(Empresa, duena.Id, Rol.Comercial, otra.UsuarioId))
            .Error!.Codigo.Should().Be("equipo.ultimo_propietario");
        (await Servicio.QuitarAsync(Empresa, duena.Id, otra.UsuarioId))
            .Error!.Codigo.Should().Be("equipo.ultimo_propietario");
    }

    [Fact]
    public async Task Nadie_se_cambia_el_rol_ni_se_quita_el_acceso_a_si_mismo()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        Alta("ana@ribera.es", "Ana Gil", Rol.Propietario, Empresa);

        (await Servicio.CambiarRolAsync(Empresa, duena.Id, Rol.SoloLectura, duena.UsuarioId))
            .Error!.Codigo.Should().Be("equipo.no_a_ti_mismo");
        (await Servicio.QuitarAsync(Empresa, duena.Id, duena.UsuarioId))
            .Error!.Codigo.Should().Be("equipo.no_a_ti_mismo");
    }

    [Fact]
    public async Task Una_membresia_de_otra_empresa_no_se_toca()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var ajena = Alta("otro@otra.es", "Otro Cualquiera", Rol.Comercial, Otra);

        (await Servicio.CambiarRolAsync(Empresa, ajena.Id, Rol.SoloLectura, duena.UsuarioId))
            .Error!.Codigo.Should().Be("equipo.no_encontrado");
        (await Servicio.QuitarAsync(Empresa, ajena.Id, duena.UsuarioId))
            .Error!.Codigo.Should().Be("equipo.no_encontrado");
        (await Servicio.FijarZonasAsync(Empresa, ajena.Id, "Valencia"))
            .Error!.Codigo.Should().Be("equipo.no_encontrado");

        ajena.Rol.Should().Be(Rol.Comercial);
        ajena.Activa.Should().BeTrue();
    }

    [Fact]
    public async Task Las_zonas_se_guardan_y_es_lo_que_reparte_los_leads()
    {
        // Sin esto el primer factor del reparto del Match estaba siempre vacío: repartía por zona sin
        // que nadie tuviera zona.
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var vicent = Alta("vicent@ribera.es", "Vicent Llopis", Rol.Comercial, Empresa);

        (await Servicio.FijarZonasAsync(Empresa, vicent.Id, " Valencia, Castellón ,Alicante ")).Exito.Should().BeTrue();

        var equipo = await Servicio.EquipoAsync(Empresa);
        equipo.Single(m => m.UsuarioId == vicent.UsuarioId).Zonas
            .Should().BeEquivalentTo(["Valencia", "Castellón", "Alicante"]);
        equipo.Single(m => m.UsuarioId == duena.UsuarioId).Zonas.Should().BeEmpty();
    }

    [Fact]
    public async Task Quien_ya_no_entra_sigue_saliendo_en_la_lista()
    {
        // Sus contactos siguen asignados a su nombre. Desaparecer de la lista dejaría oportunidades
        // con un dueño que la pantalla no sabe nombrar.
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var vicent = Alta("vicent@ribera.es", "Vicent Llopis", Rol.Comercial, Empresa);

        await Servicio.QuitarAsync(Empresa, vicent.Id, duena.UsuarioId);

        var equipo = await Servicio.EquipoAsync(Empresa);
        equipo.Should().HaveCount(2);
        equipo[^1].Email.Should().Be("vicent@ribera.es", "las bajas van al final");
        equipo[^1].Activa.Should().BeFalse();
    }

    [Fact]
    public async Task Quitar_a_quien_ya_no_entra_no_falla()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var vicent = Alta("vicent@ribera.es", "Vicent Llopis", Rol.Comercial, Empresa);

        await Servicio.QuitarAsync(Empresa, vicent.Id, duena.UsuarioId);

        (await Servicio.QuitarAsync(Empresa, vicent.Id, duena.UsuarioId)).Exito.Should().BeTrue();
    }

    [Fact]
    public async Task Un_enlace_inventado_no_dice_nada_de_si_existio()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        await Servicio.InvitarAsync(Empresa, "vicent@ribera.es", Rol.Comercial, duena.UsuarioId);

        var abierta = await Servicio.AbrirAsync("estonoesuntokendeverdad");

        abierta.Error!.Codigo.Should().Be("invitacion.no_vale");
    }

    [Fact]
    public async Task Lo_que_se_enseña_antes_de_entrar_dice_si_ya_hay_cuenta()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var sinCuenta = await Servicio.InvitarAsync(Empresa, "nuevo@ribera.es", Rol.Comercial, duena.UsuarioId);

        var abierta = await Servicio.AbrirAsync(sinCuenta.Valor.Token);

        abierta.Valor.Email.Should().Be("nuevo@ribera.es");
        abierta.Valor.Rol.Should().Be(Rol.Comercial);
        abierta.Valor.EmpresaId.Should().Be(Empresa);
        abierta.Valor.YaTieneCuenta.Should().BeFalse("hay que pedirle nombre y contraseña nueva");
    }

    [Fact]
    public async Task Retirar_una_invitacion_deja_el_enlace_sin_valor()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var invitada = await Servicio.InvitarAsync(Empresa, "vicent@ribera.es", Rol.Comercial, duena.UsuarioId);

        (await Servicio.RetirarInvitacionAsync(Empresa, invitada.Valor.Invitacion.Id)).Exito.Should().BeTrue();

        (await Servicio.PendientesAsync(Empresa)).Should().BeEmpty();
        (await Servicio.AceptarAsync(invitada.Valor.Token, "Vicent", "Vinaros2026"))
            .Error!.Codigo.Should().Be("invitacion.retirada");
    }

    [Fact]
    public async Task Una_invitacion_de_otra_empresa_no_se_retira()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var invitada = await Servicio.InvitarAsync(Otra, "vicent@otra.es", Rol.Comercial, duena.UsuarioId);

        (await Servicio.RetirarInvitacionAsync(Empresa, invitada.Valor.Invitacion.Id))
            .Error!.Codigo.Should().Be("invitacion.no_encontrada");
    }

    [Fact]
    public async Task Una_invitacion_caducada_no_se_enseña_ni_se_acepta()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var invitada = await Servicio.InvitarAsync(Empresa, "vicent@ribera.es", Rol.Comercial, duena.UsuarioId);

        reloj.Avanzar(TimeSpan.FromDays(Invitacion.DiasDeVida + 1));

        (await Servicio.PendientesAsync(Empresa)).Should().BeEmpty();
        (await Servicio.AbrirAsync(invitada.Valor.Token)).Error!.Codigo.Should().Be("invitacion.no_vale");
        (await Servicio.AceptarAsync(invitada.Valor.Token, "Vicent", "Vinaros2026"))
            .Error!.Codigo.Should().Be("invitacion.caducada");
    }

    [Fact]
    public async Task Una_contrasena_floja_no_gasta_la_invitacion()
    {
        var duena = Alta("marta@ribera.es", "Marta Ruiz", Rol.Propietario, Empresa);
        var invitada = await Servicio.InvitarAsync(Empresa, "vicent@ribera.es", Rol.Comercial, duena.UsuarioId);

        var flojo = await Servicio.AceptarAsync(invitada.Valor.Token, "Vicent Llopis", "corta");

        flojo.Error!.Codigo.Should().Be("contrasena.corta");
        (await Servicio.EquipoAsync(Empresa)).Should().HaveCount(1);

        // Y sobre todo: la invitación **sigue sirviendo**. Marcarla como usada antes de crear la cuenta
        // dejaría a la persona fuera por haberse equivocado tecleando, y sin enlace para reintentarlo.
        invitada.Valor.Invitacion.AceptadaEn.Should().BeNull();
        (await Servicio.AceptarAsync(invitada.Valor.Token, "Vicent Llopis", "Vinaros2026")).Exito.Should().BeTrue();
    }

    /// <summary>Mete a alguien en una empresa directamente, sin pasar por la invitación.</summary>
    private Membresia Alta(string email, string nombre, Rol rol, Guid empresaId)
    {
        var usuario = Usuario.Registrar(email, "Levante2026", nombre, hasher.Hashear, reloj).Valor;
        usuarios.Anadir(usuario);
        var membresia = Membresia.Crear(usuario.Id, empresaId, rol, reloj);
        membresias.Anadir(membresia);
        return membresia;
    }
}
