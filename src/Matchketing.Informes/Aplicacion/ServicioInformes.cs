using System.Globalization;
using System.Text;

namespace Matchketing.Informes.Aplicacion;

public sealed class ServicioInformes(IConsultaInformes consulta)
{
    public Task<InformeEmbudo> EmbudoAsync(Periodo periodo, CancellationToken ct = default) =>
        consulta.EmbudoAsync(periodo, ct);

    public Task<InformeMotivos> MotivosAsync(Periodo periodo, CancellationToken ct = default) =>
        consulta.MotivosAsync(periodo, ct);

    /// <summary>
    /// CSV para abrirlo en Excel español: separador **punto y coma** y decimales con **coma**. Con
    /// separador de comas, Excel en español mete toda la fila en la primera celda y el cliente cree
    /// que el programa está roto.
    /// </summary>
    public async Task<string> EmbudoCsvAsync(Periodo periodo, CancellationToken ct = default)
    {
        var i = await EmbudoAsync(periodo, ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.AppendLine("Etapa;Probabilidad;Abiertas;Importe abierto;Han llegado;Conversión a la siguiente");

        foreach (var e in i.Etapas)
        {
            sb.Append(Campo(e.Nombre)).Append(';')
              .Append(e.Probabilidad.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(e.Abiertas.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(Numero(e.ImporteAbierto)).Append(';')
              .Append(e.HanLlegado.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(e.ConversionALaSiguiente is { } c ? Numero(c) : string.Empty)
              .AppendLine();
        }

        sb.AppendLine();
        sb.Append("Periodo;").AppendLine(Campo(i.Periodo));
        sb.Append("Abiertas;").AppendLine(i.Abiertas.ToString(CultureInfo.InvariantCulture));
        sb.Append("Importe abierto;").AppendLine(Numero(i.ImporteAbierto));
        sb.Append("Previsión ponderada;").AppendLine(Numero(i.PrevisionPonderada));
        sb.Append("Ganadas;").AppendLine(i.Ganadas.ToString(CultureInfo.InvariantCulture));
        sb.Append("Importe ganado;").AppendLine(Numero(i.ImporteGanado));
        sb.Append("Perdidas;").AppendLine(i.Perdidas.ToString(CultureInfo.InvariantCulture));
        sb.Append("Importe perdido;").AppendLine(Numero(i.ImportePerdido));
        sb.Append("Tasa de cierre;").AppendLine(i.TasaCierre is { } t ? Numero(t) : string.Empty);
        sb.Append("Ticket medio;").AppendLine(i.TicketMedio is { } m ? Numero(m) : string.Empty);
        sb.Append("Días medios para cerrar;").AppendLine(i.DiasMediosParaCerrar is { } d ? Numero(d) : string.Empty);

        return sb.ToString();
    }

    public async Task<string> MotivosCsvAsync(Periodo periodo, CancellationToken ct = default)
    {
        var i = await MotivosAsync(periodo, ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.AppendLine("Motivo;Cuántas;Importe;Porcentaje");

        foreach (var m in i.Motivos)
        {
            sb.Append(Campo(m.Motivo)).Append(';')
              .Append(m.Cuantas.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(Numero(m.Importe)).Append(';')
              .Append(Numero(m.Porcentaje))
              .AppendLine();
        }

        sb.AppendLine();
        sb.Append("Periodo;").AppendLine(Campo(i.Periodo));
        sb.Append("Perdidas;").AppendLine(i.TotalPerdidas.ToString(CultureInfo.InvariantCulture));
        sb.Append("Importe perdido;").AppendLine(Numero(i.ImportePerdido));
        sb.Append("Ganadas;").AppendLine(i.TotalGanadas.ToString(CultureInfo.InvariantCulture));
        sb.Append("Importe ganado;").AppendLine(Numero(i.ImporteGanado));

        return sb.ToString();
    }

    /// <summary>Decimales con coma, como espera Excel en español.</summary>
    private static string Numero(decimal valor) =>
        valor.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Entrecomilla si el texto lleva el separador, comillas o un salto de línea.</summary>
    private static string Campo(string valor) =>
        valor.Contains(';', StringComparison.Ordinal) || valor.Contains('"', StringComparison.Ordinal) || valor.Contains('\n', StringComparison.Ordinal)
            ? '"' + valor.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : valor;
}
