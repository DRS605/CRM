using Matchketing.Match.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Match.Aplicacion;

public sealed record MatchDeContacto(Guid ContactoId, int? Match, int Encaje, int Momento, IReadOnlyList<string> Motivos, bool SinHistorico)
{
    public string Explicacion => Motivos.Count == 0 ? "Sin datos suficientes." : string.Join(" · ", Motivos) + ".";
}

public sealed class ServicioMatch(
    IRepositorioSenales senales,
    IRepositorioPuntuaciones puntuaciones,
    IConsultaMatch consulta,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    /// <summary>
    /// Registra una señal y **recalcula al instante**. Es lo que permite que un lead entre, se
    /// puntúe y se asigne en menos de un minuto sin que nadie lo toque.
    /// </summary>
    public async Task<Resultado> RegistrarSenalAsync(Guid contactoId, TipoSenal tipo, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var senal = Senal.Crear(empresaId, contactoId, tipo, reloj);
        if (senal.Fallido)
        {
            return Resultado.Fallo(senal.Error!);
        }

        senales.Anadir(senal.Valor);

        // La señal recién creada todavía no está en la base, así que se le pasa a mano al recálculo:
        // si no, el Match de esta petición no la vería y el contacto se quedaría con la puntuación
        // vieja hasta el barrido nocturno.
        await RecalcularAsync(contactoId, ct, new SenalPuntuable(tipo, reloj.AhoraUtc)).ConfigureAwait(false);
        return Resultado.Ok();
    }

    public async Task<Resultado<MatchDeContacto>> RecalcularAsync(
        Guid contactoId, CancellationToken ct = default, SenalPuntuable? recienRegistrada = null)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<MatchDeContacto>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var datos = await consulta.DatosDeAsync(contactoId, ct).ConfigureAwait(false);
        if (datos is null)
        {
            return Resultado.Fallo<MatchDeContacto>(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        var perfil = await consulta.PerfilAsync(ct).ConfigureAwait(false);
        var peso = await consulta.PesoEncajeAsync(ct).ConfigureAwait(false);
        var lista = await senales.DeContactoAsync(contactoId, ct).ConfigureAwait(false);
        var todas = recienRegistrada is { } nueva ? [.. lista, nueva] : lista;

        var resultado = MotorMatch.Calcular(datos, perfil, todas, peso, reloj.AhoraUtc);
        await GuardarAsync(empresaId, contactoId, resultado, ct).ConfigureAwait(false);

        return Resultado.Ok(new MatchDeContacto(
            contactoId, resultado.Match, resultado.Encaje, resultado.Momento, resultado.Motivos, resultado.SinHistorico));
    }

    /// <summary>
    /// Barrido de toda la empresa. Existe porque el Momento **decae con el tiempo**: sin recalcular,
    /// un lead de hace un mes seguiría marcando 90 eternamente.
    /// </summary>
    public async Task<int> RecalcularTodosAsync(CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return 0;
        }

        var contactos = await consulta.ContactosActivosAsync(ct).ConfigureAwait(false);
        if (contactos.Count == 0)
        {
            return 0;
        }

        var perfil = await consulta.PerfilAsync(ct).ConfigureAwait(false);
        var peso = await consulta.PesoEncajeAsync(ct).ConfigureAwait(false);
        var datos = await consulta.DatosDeVariosAsync(contactos, ct).ConfigureAwait(false);
        var porContacto = await senales.DeVariosAsync(contactos, ct).ConfigureAwait(false);

        foreach (var id in contactos)
        {
            if (!datos.TryGetValue(id, out var d))
            {
                continue;
            }

            var suyas = porContacto.TryGetValue(id, out var s) ? s : [];
            var resultado = MotorMatch.Calcular(d, perfil, suyas, peso, reloj.AhoraUtc);
            await GuardarAsync(empresaId, id, resultado, ct).ConfigureAwait(false);
        }

        return contactos.Count;
    }

    public async Task<Resultado<MatchDeContacto>> ObtenerAsync(Guid contactoId, CancellationToken ct = default)
    {
        var guardada = await puntuaciones.DeContactoAsync(contactoId, ct).ConfigureAwait(false);
        if (guardada is not null)
        {
            return Resultado.Ok(new MatchDeContacto(
                contactoId, guardada.Match, guardada.Encaje, guardada.Momento, guardada.ListaMotivos, guardada.SinHistorico));
        }

        return await RecalcularAsync(contactoId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Propone el comercial con mejor match para este contacto. Devuelve también el porqué, para
    /// que la asignación no parezca una lotería.
    /// </summary>
    public async Task<Resultado<AsignacionPropuesta>> ProponerComercialAsync(Guid contactoId, CancellationToken ct = default)
    {
        var datos = await consulta.DatosDeAsync(contactoId, ct).ConfigureAwait(false);
        if (datos is null)
        {
            return Resultado.Fallo<AsignacionPropuesta>(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        var candidatos = await consulta.ComercialesAsync(datos.Sector, ct).ConfigureAwait(false);
        var propuesta = Repartidor.Repartir(candidatos, datos.Provincia, datos.Sector);

        return propuesta is null
            ? Resultado.Fallo<AsignacionPropuesta>(Error.NoEncontrado("reparto.sin_comerciales", "No hay ningún comercial al que asignar el lead."))
            : Resultado.Ok(propuesta);
    }

    private async Task GuardarAsync(Guid empresaId, Guid contactoId, ResultadoMatch resultado, CancellationToken ct)
    {
        var guardada = await puntuaciones.DeContactoAsync(contactoId, ct).ConfigureAwait(false);
        if (guardada is null)
        {
            puntuaciones.Anadir(PuntuacionMatch.Crear(empresaId, contactoId, resultado, reloj));
        }
        else
        {
            guardada.Actualizar(resultado, reloj);
        }
    }
}
