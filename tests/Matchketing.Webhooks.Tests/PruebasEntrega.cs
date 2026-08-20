using FluentAssertions;
using Matchketing.Webhooks.Dominio;
using Xunit;

namespace Matchketing.Webhooks.Tests;

public sealed class PruebasEntrega
{
    private static readonly DateTimeOffset Inicio = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static Entrega Nueva(RelojFijo reloj) =>
        Entrega.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoEvento.OportunidadGanada, "{}", reloj);

    [Fact]
    public void Nace_para_ahora_mismo()
    {
        var reloj = new RelojFijo(Inicio);
        var e = Nueva(reloj);

        // Sin espera inicial: el evento acaba de ocurrir y una integración se espera «ya».
        e.Estado.Should().Be(EstadoEntrega.Pendiente);
        e.LeToca(Inicio).Should().BeTrue();
        e.Intentos.Should().Be(0);
    }

    [Fact]
    public void El_identificador_es_el_que_se_le_da()
    {
        var reloj = new RelojFijo(Inicio);
        var id = Guid.NewGuid();

        var e = Entrega.Crear(id, Guid.NewGuid(), Guid.NewGuid(), TipoEvento.LeadCreado, """{"id":"x"}""", reloj);

        // Viene de fuera porque va **dentro del cuerpo**, y el cuerpo es lo que se firma. Si el dominio
        // lo generara, el cuerpo habría que escribirlo después y podrían separarse.
        e.Id.Should().Be(id);
    }

    [Fact]
    public void Un_fallo_aplaza_el_siguiente_intento_cada_vez_mas()
    {
        var reloj = new RelojFijo(Inicio);
        var e = Nueva(reloj);

        e.NoSalio(500, "error del servidor", reloj).Should().BeFalse();
        e.ProximoIntentoEn.Should().Be(Inicio.AddMinutes(1));
        e.LeToca(Inicio).Should().BeFalse("todavía no le toca");

        reloj.Avanzar(TimeSpan.FromMinutes(1));
        e.LeToca(reloj.AhoraUtc).Should().BeTrue();

        e.NoSalio(500, "error del servidor", reloj).Should().BeFalse();
        e.ProximoIntentoEn.Should().Be(reloj.AhoraUtc.AddMinutes(5));
    }

    [Fact]
    public void Se_rinde_tras_los_intentos_previstos_y_llega_a_pasado_manana()
    {
        var reloj = new RelojFijo(Inicio);
        var e = Nueva(reloj);

        for (var i = 1; i < Entrega.IntentosMaximos; i++)
        {
            e.NoSalio(503, "no disponible", reloj).Should().BeFalse($"en el intento {i} aún queda");
            reloj.AhoraUtc = e.ProximoIntentoEn!.Value;
        }

        e.NoSalio(503, "no disponible", reloj).Should().BeTrue();
        e.Estado.Should().Be(EstadoEntrega.Agotada);
        e.ProximoIntentoEn.Should().BeNull("una entrega agotada no se vuelve a mirar");
        e.LeToca(reloj.AhoraUtc.AddYears(1)).Should().BeFalse();

        // Lo importante del escalado no es el número de intentos, es cuánto abarca: un despliegue del
        // otro lado, una noche de mantenimiento o un fin de semana tienen que caber dentro.
        (reloj.AhoraUtc - Inicio).Should().BeGreaterThan(TimeSpan.FromHours(24));
    }

    [Fact]
    public void Al_salir_se_queda_limpia()
    {
        var reloj = new RelojFijo(Inicio);
        var e = Nueva(reloj);
        e.NoSalio(500, "error del servidor", reloj);

        reloj.Avanzar(TimeSpan.FromMinutes(1));
        e.Salio(201, reloj);

        e.Estado.Should().Be(EstadoEntrega.Entregada);
        e.EntregadaEn.Should().Be(reloj.AhoraUtc);
        e.UltimoCodigo.Should().Be(201);
        e.UltimoFallo.Should().BeNull("si salió, el fallo anterior ya no cuenta nada");
        e.ProximoIntentoEn.Should().BeNull();
    }

    [Fact]
    public void Abandonar_no_gasta_reintentos_ni_cuenta_como_fallo_del_otro_lado()
    {
        var reloj = new RelojFijo(Inicio);
        var e = Nueva(reloj);

        e.Abandonar("La suscripción ya no está activa.", reloj);

        // Es el caso de la suscripción borrada mientras la entrega esperaba turno. No es culpa de
        // quien recibe, así que no puede contarle como fallo ni consumir intentos.
        e.Estado.Should().Be(EstadoEntrega.Agotada);
        e.Intentos.Should().Be(0);
        e.ProximoIntentoEn.Should().BeNull();
        e.UltimoFallo.Should().Contain("activa");
    }

    [Fact]
    public void Un_reintento_conserva_el_mismo_identificador()
    {
        var reloj = new RelojFijo(Inicio);
        var e = Nueva(reloj);
        var id = e.Id;
        var cuerpo = e.Cuerpo;

        e.NoSalio(null, "no contestó", reloj);

        // Si el identificador o el cuerpo cambiaran al reintentar, la deduplicación del otro lado no
        // serviría de nada: vería dos eventos distintos y actuaría dos veces.
        e.Id.Should().Be(id);
        e.Cuerpo.Should().Be(cuerpo);
    }
}
