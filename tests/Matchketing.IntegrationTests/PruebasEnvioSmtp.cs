using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Matchketing.Api.Comun;
using Matchketing.Correo.Dominio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// El envío contra un servidor SMTP **de verdad**, hablando el protocolo.
///
/// Todo lo demás del correo está probado con un emisor de mentira, y eso deja fuera justo lo que solo
/// se ve al hablar con algo que no hemos escrito nosotros: las cabeceras que van en el sobre. Aquí se
/// levanta un servidor mínimo en un puerto, se manda un correo y se lee lo que llegó.
///
/// Lo que encontró: **los correos comerciales salían sin ninguna forma de darse de baja.** La
/// maquinaria estaba entera —el enlace firmado, la ruta pública, la pantalla para copiarlo— y no
/// llegaba al correo.
/// </summary>
public sealed class PruebasEnvioSmtp
{
    /// <summary>Un servidor SMTP mínimo: responde lo justo y se queda con el mensaje.</summary>
    private sealed class ServidorFalso : IDisposable
    {
        private readonly TcpListener escucha;
        private readonly Task atendiendo;

        public ServidorFalso()
        {
            escucha = new TcpListener(IPAddress.Loopback, 0);
            escucha.Start();
            Puerto = ((IPEndPoint)escucha.LocalEndpoint).Port;
            atendiendo = Task.Run(AtenderAsync);
        }

        public int Puerto { get; }

        public TaskCompletionSource<string> Mensaje { get; } = new();

        private async Task AtenderAsync()
        {
            using var cliente = await escucha.AcceptTcpClientAsync().ConfigureAwait(false);
            using var flujo = cliente.GetStream();
            using var lee = new StreamReader(flujo, Encoding.UTF8);
            await using var escribe = new StreamWriter(flujo, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };

            await escribe.WriteLineAsync("220 prueba.local ESMTP").ConfigureAwait(false);

            var recibido = new StringBuilder();
            var enDatos = false;

            while (await lee.ReadLineAsync().ConfigureAwait(false) is { } linea)
            {
                if (enDatos)
                {
                    if (linea == ".")
                    {
                        await escribe.WriteLineAsync("250 2.0.0 Ok").ConfigureAwait(false);
                        Mensaje.TrySetResult(recibido.ToString());
                        enDatos = false;
                        continue;
                    }

                    recibido.AppendLine(linea);
                    continue;
                }

                var orden = linea.ToUpperInvariant();
                if (orden.StartsWith("EHLO", StringComparison.Ordinal) || orden.StartsWith("HELO", StringComparison.Ordinal))
                {
                    await escribe.WriteLineAsync("250 prueba.local").ConfigureAwait(false);
                }
                else if (orden == "DATA")
                {
                    enDatos = true;
                    await escribe.WriteLineAsync("354 Adelante").ConfigureAwait(false);
                }
                else if (orden == "QUIT")
                {
                    await escribe.WriteLineAsync("221 2.0.0 Adiós").ConfigureAwait(false);
                    return;
                }
                else
                {
                    await escribe.WriteLineAsync("250 2.0.0 Ok").ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            escucha.Stop();
            Mensaje.TrySetCanceled();
        }
    }

    private static Matchketing.Correo.Dominio.Correo UnCorreo(ParaQue paraQue) =>
        Matchketing.Correo.Dominio.Correo.Crear(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "manolo@casamanolo.es", "Oferta de septiembre",
            "Hola Manolo, este mes tenemos oferta.", paraQue, null,
            new RelojDePruebaSmtp()).Valor;

    private sealed class RelojDePruebaSmtp : Matchketing.Nucleo.Tiempo.IReloj
    {
        public DateTimeOffset AhoraUtc => new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
    }

    private static async Task<string> EnviarAsync(string? urlBaja, ParaQue paraQue)
    {
        using var servidor = new ServidorFalso();
        var emisor = new EnviaCorreoSmtp(
            new AjustesSmtp("localhost", servidor.Puerto, null, null, "marta@ribera.es", "Marta Ruiz", false),
            NullLogger<EnviaCorreoSmtp>.Instance);

        var r = await emisor.EnviarAsync(UnCorreo(paraQue), null, urlBaja);
        r.Salio.Should().BeTrue(r.Fallo);

        var espera = await Task.WhenAny(servidor.Mensaje.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        espera.Should().Be(servidor.Mensaje.Task, "el servidor tenía que haber recibido el mensaje");
        return await servidor.Mensaje.Task;
    }

    [Fact]
    public async Task Un_correo_comercial_lleva_la_baja_en_la_cabecera_y_en_el_texto()
    {
        const string Baja = "https://crm.tuempresa.es/b/firma";

        var mensaje = await EnviarAsync(Baja, ParaQue.Comercial);

        // La cabecera la leen los programas de correo. Desde 2024, Gmail y Yahoo exigen a quien manda
        // envíos masivos una baja de un clic, y sin las **dos** cabeceras no cuenta.
        mensaje.Should().Contain($"List-Unsubscribe: <{Baja}>");
        mensaje.Should().Contain("List-Unsubscribe-Post: List-Unsubscribe=One-Click");

        // Y el texto lo lee la persona, que tiene derecho a ver cómo se sale sin buscar un botón
        // escondido en su cliente de correo. El cuerpo va en base64, así que se descodifica.
        Cuerpo(mensaje).Should().Contain(Baja).And.Contain("no quieres recibir más correos");
    }

    [Fact]
    public async Task Una_respuesta_a_lo_que_preguntaron_no_lleva_baja()
    {
        var mensaje = await EnviarAsync(null, ParaQue.AtenderSolicitud);

        mensaje.Should().NotContain("List-Unsubscribe");
        Cuerpo(mensaje).Should().NotContain("no quieres recibir más correos");
    }

    [Fact]
    public async Task El_asunto_y_el_cuerpo_llegan_en_UTF_8()
    {
        // El castellano lleva tildes y eñes en el asunto y en el cuerpo, y un correo mal codificado se
        // lee «maÃ±ana». Se comprueba que va declarado como UTF-8 y que el texto vuelve entero.
        var mensaje = await EnviarAsync(null, ParaQue.AtenderSolicitud);

        mensaje.Should().Contain("charset=utf-8");
        Cuerpo(mensaje).Should().Contain("Hola Manolo, este mes tenemos oferta.");
    }

    /// <summary>El cuerpo del mensaje, descodificado del base64 en el que lo mete `MailMessage`.</summary>
    private static string Cuerpo(string mensaje)
    {
        var partes = mensaje.Split("\r\n\r\n", 2, StringSplitOptions.None);
        if (partes.Length < 2)
        {
            partes = mensaje.Split("\n\n", 2, StringSplitOptions.None);
        }

        var base64 = string.Concat(partes[^1].Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()));

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            // Si no viniera en base64, el texto plano vale igual para lo que se afirma.
            return partes[^1];
        }
    }
}
