using FluentAssertions;
using Matchketing.Webhooks.Dominio;
using Xunit;

namespace Matchketing.Webhooks.Tests;

public sealed class PruebasSuscripcion
{
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

    private static SuscripcionWebhook Nueva(params TipoEvento[] tipos) =>
        SuscripcionWebhook.Crear(
            Guid.NewGuid(), "https://erp.ejemplo.es/hooks/mk", "Pedidos al ERP",
            tipos.Length == 0 ? [TipoEvento.OportunidadGanada] : tipos, Reloj).Valor;

    [Theory]
    [InlineData("http://erp.ejemplo.es/hooks")]        // sin cifrar
    [InlineData("http://localhost:3000/hooks")]        // «solo para probar»
    [InlineData("ftp://erp.ejemplo.es/hooks")]
    [InlineData("erp.ejemplo.es/hooks")]               // sin esquema
    [InlineData("")]
    [InlineData(null)]
    public void Sin_https_no_hay_webhook(string? url)
    {
        var r = SuscripcionWebhook.Crear(Guid.NewGuid(), url, "Pedidos", [TipoEvento.OportunidadGanada], Reloj);

        // No hay excepción para `localhost` a propósito. La tentación es dejar http «solo para
        // probar», y esa excepción acaba en producción con las ventas viajando en claro.
        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("webhook.url_invalida");
    }

    [Fact]
    public void Con_https_si()
    {
        SuscripcionWebhook.Crear(
            Guid.NewGuid(), "https://erp.ejemplo.es/hooks", "Pedidos", [TipoEvento.OportunidadGanada], Reloj)
            .Exito.Should().BeTrue();
    }

    [Fact]
    public void Hace_falta_decir_para_que_es()
    {
        var r = SuscripcionWebhook.Crear(
            Guid.NewGuid(), "https://erp.ejemplo.es/hooks", "  ", [TipoEvento.OportunidadGanada], Reloj);

        // Con tres webhooks nadie recuerda cuál es cuál, y el que no se recuerda es el que no se borra
        // cuando deja de hacer falta.
        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("webhook.sin_descripcion");
    }

    [Fact]
    public void Sin_eventos_no_es_una_suscripcion()
    {
        var r = SuscripcionWebhook.Crear(Guid.NewGuid(), "https://erp.ejemplo.es/hooks", "Pedidos", [], Reloj);

        // Una suscripción vacía «para elegir luego» es una fila que no hace nada y parece que sí.
        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("webhook.sin_eventos");
    }

    [Fact]
    public void Los_eventos_repetidos_se_colapsan()
    {
        var s = Nueva(TipoEvento.OportunidadGanada, TipoEvento.OportunidadGanada, TipoEvento.LeadCreado);

        s.Tipos.Should().HaveCount(2);
    }

    [Fact]
    public void Nace_con_secreto_y_activa()
    {
        var s = Nueva();

        s.Activa.Should().BeTrue();
        s.Secreto.Should().StartWith("whsec_");
        s.MotivoApagado.Should().BeNull();
        s.FallosSeguidos.Should().Be(0);
    }

    [Fact]
    public void Solo_escucha_lo_suyo()
    {
        var s = Nueva(TipoEvento.OportunidadGanada);

        s.Escucha(TipoEvento.OportunidadGanada).Should().BeTrue();
        s.Escucha(TipoEvento.OportunidadPerdida).Should().BeFalse();
    }

    [Fact]
    public void Una_suscripcion_apagada_no_escucha_nada()
    {
        var s = Nueva(TipoEvento.OportunidadGanada);
        for (var i = 0; i < SuscripcionWebhook.FallosParaDesactivar; i++)
        {
            s.Fallada("no contesta");
        }

        // Aunque siga suscrita al evento. Es lo que impide que se sigan apilando entregas para una URL
        // que ya se sabe que no funciona.
        s.Escucha(TipoEvento.OportunidadGanada).Should().BeFalse();
    }

    [Fact]
    public void Se_apaga_sola_al_quinto_fallo_definitivo_y_dice_por_que()
    {
        var s = Nueva();

        for (var i = 1; i < SuscripcionWebhook.FallosParaDesactivar; i++)
        {
            s.Fallada("el servidor contestó 500").Should().BeFalse($"al fallo {i} todavía no se apaga");
            s.Activa.Should().BeTrue();
        }

        s.Fallada("el servidor contestó 500").Should().BeTrue();
        s.Activa.Should().BeFalse();

        // El motivo se guarda para poder leerlo en la pantalla. Sin esto, alguien se encuentra un
        // webhook apagado sin saber si fue él, un compañero o el sistema.
        s.MotivoApagado.Should().Contain("500");
    }

    [Fact]
    public void Una_entrega_buena_borra_el_historial_de_fallos()
    {
        var s = Nueva();
        s.Fallada("500");
        s.Fallada("500");

        s.Entregada(Reloj);

        // Cuentan los fallos **seguidos**. Un endpoint que falla una vez cada martes no debe acabar
        // apagado tres meses después por acumulación.
        s.FallosSeguidos.Should().Be(0);
        s.UltimaEntregaEn.Should().Be(Reloj.AhoraUtc);
    }

    [Fact]
    public void Reactivar_es_a_mano_y_lo_deja_limpio()
    {
        var s = Nueva();
        for (var i = 0; i < SuscripcionWebhook.FallosParaDesactivar; i++)
        {
            s.Fallada("500");
        }

        s.Reactivar();

        // No se reactiva sola: si se apagó porque la URL estaba mal, reintentarlo por nuestra cuenta
        // solo repetiría el fallo cinco veces más.
        s.Activa.Should().BeTrue();
        s.MotivoApagado.Should().BeNull();
        s.FallosSeguidos.Should().Be(0);
    }

    [Fact]
    public void Rotar_el_secreto_lo_cambia_de_verdad()
    {
        var s = Nueva();
        var antes = s.Secreto;

        var nuevo = s.RotarSecreto();

        nuevo.Should().NotBe(antes);
        s.Secreto.Should().Be(nuevo);
    }

    [Fact]
    public void Cambiar_no_deja_quitar_todos_los_eventos()
    {
        var s = Nueva();

        s.Cambiar("Pedidos al ERP", []).Fallido.Should().BeTrue();
        s.Tipos.Should().NotBeEmpty("un cambio rechazado no puede dejar la suscripción a medias");
    }

    [Fact]
    public void Cambiar_reemplaza_los_eventos_enteros()
    {
        var s = Nueva(TipoEvento.OportunidadGanada);

        s.Cambiar("Solo bajas", [TipoEvento.ContactoBaja]).Exito.Should().BeTrue();

        s.Tipos.Should().Equal(TipoEvento.ContactoBaja);
        s.Escucha(TipoEvento.OportunidadGanada).Should().BeFalse();
    }
}
