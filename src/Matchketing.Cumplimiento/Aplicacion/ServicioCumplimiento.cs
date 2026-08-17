using Matchketing.Auditoria.Aplicacion;
using Matchketing.Auditoria.Dominio;
using Matchketing.Cumplimiento.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Cumplimiento.Aplicacion;

/// <summary>
/// Los derechos de las personas cuyos datos están aquí dentro, hechos código.
///
/// El módulo entero existe para que la respuesta a «¿puedo mandarle esto?» no sea nunca «supongo que
/// sí». Tres reglas lo gobiernan:
///
/// * **G1** — sin base legal vigente no se envía nada. La comprobación la hace el servidor, y quien
///   quiera mandar un correo comercial tiene que pasar por ella; no es un aviso en la interfaz.
/// * **G2** — la baja es irreversible desde nuestro lado. Solo el interesado puede volver.
/// * **G3** — borrar es borrar. La supresión quita filas de las tablas; no pone un `activo = false`.
///   Lo único que sobrevive es una línea de auditoría con cifras, sin un solo dato personal.
/// </summary>
public sealed class ServicioCumplimiento(
    IRepositorioConsentimientos consentimientos,
    IAlmacenPersonal almacen,
    IAjustesRetencion ajustes,
    IRegistradorAuditoria auditoria,
    AjustesBaja ajustesBaja,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    private const string EntidadContacto = "contacto";
    private const string EntidadEmpresa = "empresa";

    public async Task<Resultado<Consentimiento>> OtorgarAsync(
        Guid contactoId, FinalidadConsentimiento finalidad, BaseLegal baseLegal,
        string? canal, string? textoAceptado, string? ip, string? agente, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Consentimiento>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        if (await almacen.EstaDeBajaAsync(contactoId, ct).ConfigureAwait(false) is not { } deBaja)
        {
            return Resultado.Fallo<Consentimiento>(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        // G2: si pidió no recibir más, no se le vuelve a apuntar un permiso desde dentro. Volver
        // requiere que sea él quien lo pida, y eso llega por la entrada pública, no por aquí.
        if (deBaja)
        {
            return Resultado.Fallo<Consentimiento>(Error.Conflicto(
                "contacto.dado_de_baja", "El contacto pidió no recibir más comunicaciones; no se le puede apuntar un consentimiento nuevo."));
        }

        if (await consentimientos.VigenteAsync(contactoId, finalidad, ct).ConfigureAwait(false) is not null)
        {
            return Resultado.Fallo<Consentimiento>(Error.Conflicto(
                "consentimiento.ya_vigente", $"Ya hay un consentimiento vigente para {Textos.De(finalidad)}."));
        }

        var otorgado = Consentimiento.Otorgar(empresaId, contactoId, finalidad, baseLegal, canal, textoAceptado, ip, agente, reloj);
        if (otorgado.Fallido)
        {
            return otorgado;
        }

        consentimientos.Anadir(otorgado.Valor);
        auditoria.Registrar(EntidadContacto, contactoId, Acciones.ConsentimientoOtorgado, new { finalidad = finalidad.ToString(), baseLegal = baseLegal.ToString() });
        return otorgado;
    }

    public async Task<Resultado> RetirarAsync(Guid contactoId, FinalidadConsentimiento finalidad, CancellationToken ct = default)
    {
        var vigente = await consentimientos.VigenteAsync(contactoId, finalidad, ct).ConfigureAwait(false);
        if (vigente is null)
        {
            return Resultado.Fallo(Error.NoEncontrado(
                "consentimiento.no_vigente", $"No hay ningún consentimiento vigente para {Textos.De(finalidad)}."));
        }

        var retirado = vigente.Retirar(reloj);
        if (retirado.Exito)
        {
            auditoria.Registrar(EntidadContacto, contactoId, Acciones.ConsentimientoRetirado, new { finalidad = finalidad.ToString() });
        }

        return retirado;
    }

    /// <summary>
    /// **G1**, la comprobación que justifica el módulo. Devuelve un fallo con el motivo exacto en vez
    /// de un booleano, porque quien no puede enviar necesita saber por qué para arreglarlo.
    /// </summary>
    public async Task<Resultado> PuedeEnviarAsync(Guid contactoId, FinalidadConsentimiento finalidad, CancellationToken ct = default)
    {
        if (await almacen.EstaDeBajaAsync(contactoId, ct).ConfigureAwait(false) is not { } deBaja)
        {
            return Resultado.Fallo(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        if (deBaja)
        {
            return Resultado.Fallo(Error.Prohibido(
                "cumplimiento.de_baja", "El contacto pidió no recibir más comunicaciones."));
        }

        return await consentimientos.VigenteAsync(contactoId, finalidad, ct).ConfigureAwait(false) is null
            ? Resultado.Fallo(Error.Prohibido(
                "cumplimiento.sin_base_legal", $"No hay base legal vigente para {Textos.De(finalidad)}."))
            : Resultado.Ok();
    }

    /// <summary>El panel de privacidad de la ficha: estado, explicación y enlace de baja para copiar.</summary>
    public async Task<Resultado<FichaCumplimiento>> FichaAsync(Guid contactoId, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<FichaCumplimiento>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        if (await almacen.EstaDeBajaAsync(contactoId, ct).ConfigureAwait(false) is not { } deBaja)
        {
            return Resultado.Fallo<FichaCumplimiento>(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        var lista = await consentimientos.DeContactoAsync(contactoId, ct).ConfigureAwait(false);
        var comercial = await PuedeEnviarAsync(contactoId, FinalidadConsentimiento.Comercial, ct).ConfigureAwait(false);

        var explicacion = deBaja
            ? "Pidió no recibir más comunicaciones. No se le puede escribir ni llamar para vender."
            : comercial.Exito
                ? "Se le puede enviar publicidad: hay base legal vigente para comunicaciones comerciales."
                : lista.Any(c => c.Vigente)
                    ? "Solo se le puede contestar a lo que preguntó. Para enviarle publicidad hace falta su permiso."
                    : "No hay ningún permiso registrado. Pídelo antes de escribirle.";

        return Resultado.Ok(new FichaCumplimiento(
            contactoId,
            deBaja,
            comercial.Exito,
            explicacion,
            $"{ajustesBaja.UrlBase.TrimEnd('/')}/b/{EnlaceBaja.Firmar(empresaId, contactoId, ajustesBaja.Secreto)}",
            lista
                .OrderByDescending(c => c.OtorgadoEn)
                .Select(c => new LineaConsentimiento(
                    c.Id, Textos.De(c.Finalidad), Textos.De(c.Base), c.Canal, c.TextoAceptado, c.OtorgadoEn, c.RetiradoEn, c.Vigente))
                .ToList()));
    }

    /// <summary>Comprueba la firma de un enlace de baja. No toca la base: solo dice a quién señala.</summary>
    public Resultado<(Guid EmpresaId, Guid ContactoId)> ComprobarEnlaceBaja(string? token) =>
        EnlaceBaja.Comprobar(token, ajustesBaja.Secreto);

    /// <summary>
    /// La baja de un clic. Marca el contacto y **retira todos sus consentimientos**: dejar uno
    /// vigente convertiría la baja en un adorno, porque cualquier envío posterior encontraría base
    /// legal y saldría.
    ///
    /// Es idempotente a propósito: quien pulsa dos veces el enlace del correo no debe ver un error.
    /// </summary>
    public async Task<Resultado<bool>> DarDeBajaAsync(Guid empresaId, Guid contactoId, CancellationToken ct = default)
    {
        if (await almacen.EstaDeBajaAsync(contactoId, ct).ConfigureAwait(false) is not { } yaEstaba)
        {
            return Resultado.Fallo<bool>(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        var vigentes = (await consentimientos.DeContactoAsync(contactoId, ct).ConfigureAwait(false))
            .Where(c => c.Vigente)
            .ToList();

        foreach (var c in vigentes)
        {
            c.Retirar(reloj);
        }

        if (yaEstaba && vigentes.Count == 0)
        {
            return Resultado.Ok(false);
        }

        await almacen.DarDeBajaContactoAsync(contactoId, ct).ConfigureAwait(false);

        // Del sistema: la baja la pide el interesado desde su correo, sin haber entrado en la
        // aplicación, así que no hay ningún usuario nuestro a quien atribuirla.
        auditoria.RegistrarDelSistema(empresaId, EntidadContacto, contactoId, Acciones.ContactoBaja, new { consentimientosRetirados = vigentes.Count });
        return Resultado.Ok(true);
    }

    /// <summary>Derecho de acceso y portabilidad: todo lo que guardamos de una persona, en un JSON.</summary>
    public async Task<Resultado<object>> ExportarContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        var datos = await almacen.ReunirContactoAsync(contactoId, ct).ConfigureAwait(false);
        if (datos is null)
        {
            return Resultado.Fallo<object>(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        auditoria.Registrar(EntidadContacto, contactoId, Acciones.ContactoExportado);
        return Resultado.Ok(datos);
    }

    /// <summary>
    /// Derecho de supresión. Borra de verdad; lo único que queda es el apunte de auditoría, que
    /// lleva el identificador y los recuentos y ni un dato personal.
    /// </summary>
    public async Task<Resultado<RecuentoBorrado>> BorrarContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        if (!await almacen.ExisteContactoAsync(contactoId, ct).ConfigureAwait(false))
        {
            return Resultado.Fallo<RecuentoBorrado>(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        var recuento = await almacen.BorrarContactoAsync(contactoId, ct).ConfigureAwait(false);
        auditoria.Registrar(EntidadContacto, contactoId, Acciones.ContactoBorrado, recuento);
        return Resultado.Ok(recuento);
    }

    public async Task<Resultado<object>> ExportarEmpresaAsync(CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<object>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var datos = await almacen.ReunirEmpresaAsync(ct).ConfigureAwait(false);
        auditoria.Registrar(EntidadEmpresa, empresaId, Acciones.EmpresaExportada);
        return Resultado.Ok(datos);
    }

    /// <summary>
    /// Se lleva la empresa entera. Pide escribir su nombre exacto porque es la única operación del
    /// sistema que no tiene vuelta: un «¿seguro?» con un botón se pulsa sin leerlo.
    ///
    /// El apunte de auditoría se escribe **después** del borrado, y por eso sobrevive: es lo único
    /// que queda de la empresa, y dice cuándo se fue y cuántas filas se llevó.
    /// </summary>
    public async Task<Resultado<RecuentoBorrado>> BorrarEmpresaAsync(string? confirmacion, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<RecuentoBorrado>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var nombre = await almacen.NombreEmpresaAsync(ct).ConfigureAwait(false);
        if (nombre is null)
        {
            return Resultado.Fallo<RecuentoBorrado>(Error.NoEncontrado("empresa.no_encontrada", "La empresa no existe."));
        }

        if (!string.Equals(confirmacion?.Trim(), nombre, StringComparison.Ordinal))
        {
            return Resultado.Fallo<RecuentoBorrado>(Error.Validacion(
                "empresa.confirmacion_no_coincide", $"Para borrarla, escribe su nombre exacto: {nombre}"));
        }

        var recuento = await almacen.BorrarEmpresaAsync(ct).ConfigureAwait(false);
        auditoria.RegistrarDelSistema(empresaId, EntidadEmpresa, empresaId, Acciones.EmpresaBorrada, recuento);
        return Resultado.Ok(recuento);
    }

    /// <summary>
    /// Retención: los leads que nadie ha tocado en N meses y nunca llegaron a nada se borran. No es
    /// limpieza, es el principio de limitación del plazo de conservación —guardar para siempre el
    /// teléfono de alguien que preguntó un precio en 2019 no tiene ninguna base—, y hacerlo a mano
    /// es lo mismo que no hacerlo.
    ///
    /// Nunca toca clientes ni contactos con oportunidad abierta: eso lo decide el almacén.
    /// </summary>
    public async Task<Resultado<ResultadoRetencion>> AplicarRetencionAsync(Guid empresaId, CancellationToken ct = default)
    {
        var meses = await ajustes.MesesRetencionAsync(ct).ConfigureAwait(false);
        if (meses is not { } n)
        {
            return Resultado.Fallo<ResultadoRetencion>(Error.NoEncontrado("empresa.no_encontrada", "La empresa no existe."));
        }

        var limite = reloj.AhoraUtc.AddMonths(-n);
        var caducados = await almacen.LeadsCaducadosAsync(limite, ct).ConfigureAwait(false);

        var filas = 0;
        foreach (var id in caducados)
        {
            filas += (await almacen.BorrarContactoAsync(id, ct).ConfigureAwait(false)).Total;
        }

        if (caducados.Count > 0)
        {
            auditoria.RegistrarDelSistema(empresaId, EntidadContacto, null, Acciones.RetencionAplicada, new { meses = n, leads = caducados.Count, filas });
        }

        return Resultado.Ok(new ResultadoRetencion(n, caducados.Count, filas));
    }
}
