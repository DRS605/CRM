using FluentAssertions;
using Matchketing.Auditoria.Dominio;
using Matchketing.Cumplimiento.Aplicacion;
using Matchketing.Cumplimiento.Dominio;
using Xunit;

namespace Matchketing.Cumplimiento.Tests;

public sealed class PruebasServicioCumplimiento
{
    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Usuario = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Contacto = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly ConsentimientosEnMemoria consentimientos = new();
    private readonly AlmacenDePrueba almacen = new();
    private readonly AuditoriaDePrueba auditoria = new();
    private readonly RelojFijo reloj = new(new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero));

    private ServicioCumplimiento Servicio(int? mesesRetencion = 24, bool sinEmpresa = false) => new(
        consentimientos, almacen, new RetencionDePrueba(mesesRetencion), auditoria,
        new AjustesBaja("secreto-de-pruebas-largo-y-aburrido", "https://app.matchketing.es"),
        new ContextoDePrueba(sinEmpresa ? null : Empresa, Usuario), reloj);

    private void DarDeAlta(Guid id) => almacen.Contactos.Add(id);

    // ---- Consentimientos ------------------------------------------------------------------

    [Fact]
    public async Task Otorgar_deja_el_permiso_vigente_y_lo_audita()
    {
        DarDeAlta(Contacto);

        var r = await Servicio().OtorgarAsync(
            Contacto, FinalidadConsentimiento.Comercial, BaseLegal.Consentimiento,
            "alta manual", "Acepto recibir ofertas.", null, null);

        r.Exito.Should().BeTrue();
        consentimientos.Todos.Should().ContainSingle(c => c.Vigente);
        auditoria.Apuntes.Should().ContainSingle(a => a.Accion == Acciones.ConsentimientoOtorgado);
    }

    [Fact]
    public async Task No_se_apunta_dos_veces_el_mismo_permiso()
    {
        DarDeAlta(Contacto);
        var servicio = Servicio();
        await servicio.OtorgarAsync(Contacto, FinalidadConsentimiento.Comercial, BaseLegal.Consentimiento, "web", null, null, null);

        var segundo = await servicio.OtorgarAsync(Contacto, FinalidadConsentimiento.Comercial, BaseLegal.Consentimiento, "web", null, null, null);

        segundo.Error!.Codigo.Should().Be("consentimiento.ya_vigente");
    }

    [Fact]
    public async Task A_quien_esta_de_baja_no_se_le_apunta_un_permiso_nuevo()
    {
        // G2: de la baja solo vuelve el interesado. Si esto se pudiera, bastaría con «apuntarle otra
        // vez el consentimiento» para deshacer una baja desde dentro, que es justo lo que no puede ser.
        DarDeAlta(Contacto);
        almacen.DeBaja.Add(Contacto);

        var r = await Servicio().OtorgarAsync(Contacto, FinalidadConsentimiento.Comercial, BaseLegal.Consentimiento, "web", null, null, null);

        r.Error!.Codigo.Should().Be("contacto.dado_de_baja");
    }

    [Fact]
    public async Task Otorgar_a_un_contacto_que_no_existe_no_cuela()
    {
        var r = await Servicio().OtorgarAsync(Contacto, FinalidadConsentimiento.Comercial, BaseLegal.Consentimiento, "web", null, null, null);

        r.Error!.Codigo.Should().Be("contacto.no_encontrado");
    }

    [Fact]
    public async Task Retirar_lo_saca_de_vigor_y_lo_audita()
    {
        DarDeAlta(Contacto);
        var servicio = Servicio();
        await servicio.OtorgarAsync(Contacto, FinalidadConsentimiento.Comercial, BaseLegal.Consentimiento, "web", null, null, null);

        var r = await servicio.RetirarAsync(Contacto, FinalidadConsentimiento.Comercial);

        r.Exito.Should().BeTrue();
        consentimientos.Todos.Should().NotContain(c => c.Vigente);
        auditoria.Apuntes.Should().ContainSingle(a => a.Accion == Acciones.ConsentimientoRetirado);
    }

    [Fact]
    public async Task Retirar_lo_que_no_hay_es_un_no_encontrado()
    {
        DarDeAlta(Contacto);

        (await Servicio().RetirarAsync(Contacto, FinalidadConsentimiento.Comercial))
            .Error!.Codigo.Should().Be("consentimiento.no_vigente");
    }

    // ---- G1: la comprobación que justifica el módulo -------------------------------------

    [Fact]
    public async Task Sin_permiso_no_se_puede_enviar_publicidad()
    {
        DarDeAlta(Contacto);

        var r = await Servicio().PuedeEnviarAsync(Contacto, FinalidadConsentimiento.Comercial);

        r.Error!.Codigo.Should().Be("cumplimiento.sin_base_legal");
    }

    [Fact]
    public async Task El_permiso_para_atender_una_solicitud_no_sirve_para_vender()
    {
        // La distinción entera del módulo: alguien que rellenó un formulario pidiendo un presupuesto
        // ha consentido que le contestes, no que le metas en una lista de correo.
        DarDeAlta(Contacto);
        var servicio = Servicio();
        await servicio.OtorgarAsync(Contacto, FinalidadConsentimiento.AtenderSolicitud, BaseLegal.Consentimiento, "formulario web", null, null, null);

        (await servicio.PuedeEnviarAsync(Contacto, FinalidadConsentimiento.AtenderSolicitud)).Exito.Should().BeTrue();
        (await servicio.PuedeEnviarAsync(Contacto, FinalidadConsentimiento.Comercial)).Fallido.Should().BeTrue();
    }

    [Fact]
    public async Task El_interes_legitimo_tambien_es_base_legal()
    {
        DarDeAlta(Contacto);
        var servicio = Servicio();
        await servicio.OtorgarAsync(Contacto, FinalidadConsentimiento.Comercial, BaseLegal.InteresLegitimo, "cliente anterior", null, null, null);

        (await servicio.PuedeEnviarAsync(Contacto, FinalidadConsentimiento.Comercial)).Exito.Should().BeTrue();
    }

    [Fact]
    public async Task La_baja_manda_sobre_cualquier_permiso()
    {
        DarDeAlta(Contacto);
        var servicio = Servicio();
        await servicio.OtorgarAsync(Contacto, FinalidadConsentimiento.Comercial, BaseLegal.InteresLegitimo, "cliente anterior", null, null, null);
        almacen.DeBaja.Add(Contacto);

        (await servicio.PuedeEnviarAsync(Contacto, FinalidadConsentimiento.Comercial))
            .Error!.Codigo.Should().Be("cumplimiento.de_baja");
    }

    // ---- Baja ----------------------------------------------------------------------------

    [Fact]
    public async Task La_baja_retira_todos_los_permisos_vigentes()
    {
        // Si quedase uno vigente, el siguiente envío encontraría base legal y saldría: la baja habría
        // sido un adorno.
        DarDeAlta(Contacto);
        var servicio = Servicio();
        await servicio.OtorgarAsync(Contacto, FinalidadConsentimiento.Comercial, BaseLegal.Consentimiento, "web", null, null, null);
        await servicio.OtorgarAsync(Contacto, FinalidadConsentimiento.AtenderSolicitud, BaseLegal.Consentimiento, "web", null, null, null);

        var r = await servicio.DarDeBajaAsync(Empresa, Contacto);

        r.Valor.Should().BeTrue();
        almacen.DeBaja.Should().Contain(Contacto);
        consentimientos.Todos.Should().OnlyContain(c => !c.Vigente);
    }

    [Fact]
    public async Task La_baja_se_apunta_como_accion_del_sistema()
    {
        // No la hace un usuario nuestro: la pide el interesado desde su correo, sin entrar aquí.
        DarDeAlta(Contacto);

        await Servicio().DarDeBajaAsync(Empresa, Contacto);

        auditoria.Apuntes.Should().ContainSingle(a => a.Accion == Acciones.ContactoBaja && a.DelSistema);
    }

    [Fact]
    public async Task Pulsar_dos_veces_el_enlace_no_da_error()
    {
        // Quien pulsa dos veces el enlace del correo no debería ver una pantalla de error: pidió una
        // cosa y esa cosa está hecha.
        DarDeAlta(Contacto);
        var servicio = Servicio();
        await servicio.DarDeBajaAsync(Empresa, Contacto);

        var segunda = await servicio.DarDeBajaAsync(Empresa, Contacto);

        segunda.Exito.Should().BeTrue();
        segunda.Valor.Should().BeFalse("la segunda vez no cambió nada");
    }

    [Fact]
    public async Task La_ficha_explica_en_una_frase_que_se_puede_hacer()
    {
        DarDeAlta(Contacto);

        var ficha = await Servicio().FichaAsync(Contacto);

        ficha.Valor.PuedeEnviarComercial.Should().BeFalse();
        ficha.Valor.Explicacion.Should().Contain("No hay ningún permiso registrado");
        ficha.Valor.EnlaceBaja.Should().StartWith("https://app.matchketing.es/b/");
    }

    [Fact]
    public async Task El_enlace_de_la_ficha_es_el_que_da_de_baja_a_ese_contacto()
    {
        DarDeAlta(Contacto);
        var servicio = Servicio();

        var enlace = (await servicio.FichaAsync(Contacto)).Valor.EnlaceBaja;
        var token = enlace[(enlace.LastIndexOf('/') + 1)..];

        var comprobado = servicio.ComprobarEnlaceBaja(token);
        comprobado.Valor.ContactoId.Should().Be(Contacto);
        comprobado.Valor.EmpresaId.Should().Be(Empresa);
    }

    // ---- Derechos de acceso y supresión --------------------------------------------------

    [Fact]
    public async Task Exportar_un_contacto_queda_auditado()
    {
        // Quién se llevó los datos de quién es exactamente lo que hay que poder mirar después.
        DarDeAlta(Contacto);

        var r = await Servicio().ExportarContactoAsync(Contacto);

        r.Exito.Should().BeTrue();
        auditoria.Apuntes.Should().ContainSingle(a => a.Accion == Acciones.ContactoExportado && a.EntidadId == Contacto);
    }

    [Fact]
    public async Task Borrar_un_contacto_lo_borra_y_deja_el_recuento()
    {
        DarDeAlta(Contacto);

        var r = await Servicio().BorrarContactoAsync(Contacto);

        r.Valor.Total.Should().Be(11);
        almacen.Borrados.Should().Contain(Contacto);
        auditoria.Apuntes.Should().ContainSingle(a => a.Accion == Acciones.ContactoBorrado);
    }

    [Fact]
    public async Task No_se_puede_borrar_lo_que_no_existe()
    {
        (await Servicio().BorrarContactoAsync(Contacto)).Error!.Codigo.Should().Be("contacto.no_encontrado");
    }

    // ---- Cierre de cuenta ----------------------------------------------------------------

    [Fact]
    public async Task Borrar_la_empresa_exige_escribir_su_nombre_exacto()
    {
        var r = await Servicio().BorrarEmpresaAsync("reformas ana");

        r.Error!.Codigo.Should().Be("empresa.confirmacion_no_coincide");
        r.Error!.Mensaje.Should().Contain("Reformas Ana");
        almacen.EmpresaBorrada.Should().BeFalse();
    }

    [Fact]
    public async Task Con_el_nombre_exacto_la_empresa_se_va()
    {
        DarDeAlta(Contacto);

        var r = await Servicio().BorrarEmpresaAsync("  Reformas Ana  ");

        r.Exito.Should().BeTrue();
        almacen.EmpresaBorrada.Should().BeTrue();

        // El apunte se escribe **después** del borrado y es lo único que sobrevive de la empresa.
        auditoria.Apuntes.Should().ContainSingle(a => a.Accion == Acciones.EmpresaBorrada && a.DelSistema);
    }

    // ---- Retención -----------------------------------------------------------------------

    [Fact]
    public async Task La_retencion_borra_los_leads_caducados_y_lo_apunta_una_sola_vez()
    {
        var uno = Guid.NewGuid();
        var otro = Guid.NewGuid();
        DarDeAlta(uno);
        DarDeAlta(otro);
        almacen.Caducados.AddRange([uno, otro]);

        var r = await Servicio().AplicarRetencionAsync(Empresa);

        r.Valor.LeadsBorrados.Should().Be(2);
        r.Valor.Meses.Should().Be(24);
        almacen.Borrados.Should().BeEquivalentTo([uno, otro]);
        auditoria.Apuntes.Should().ContainSingle(a => a.Accion == Acciones.RetencionAplicada);
    }

    [Fact]
    public async Task Si_no_hay_nada_que_borrar_no_se_ensucia_el_registro()
    {
        // Una línea diaria de «se han borrado 0 leads» convierte el registro en algo que nadie lee.
        var r = await Servicio().AplicarRetencionAsync(Empresa);

        r.Valor.LeadsBorrados.Should().Be(0);
        auditoria.Apuntes.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_empresa_activa_no_se_puede_ni_mirar()
    {
        // El aislamiento falla cerrado: sin empresa en el contexto no hay nada que hacer aquí.
        var r = await Servicio(sinEmpresa: true).FichaAsync(Contacto);

        r.Error!.Codigo.Should().Be("empresa.sin_seleccionar");
    }
}
