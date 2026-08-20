using FluentAssertions;
using Matchketing.Correo.Dominio;
using Xunit;

namespace Matchketing.Correo.Tests;

public sealed class PruebasCorreo
{
    private static readonly DateTimeOffset Inicio = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Empresa = Guid.NewGuid();

    private static Dominio.Correo Nuevo(RelojFijo reloj, string? para = "manolo@casamanolo.es") =>
        Dominio.Correo.Crear(
            Empresa, Guid.NewGuid(), Guid.NewGuid(), para, "Sobre el presupuesto",
            "Hola Manolo, te llamo mañana.", ParaQue.AtenderSolicitud, null, reloj).Valor;

    [Fact]
    public void Nace_en_cola_y_para_ahora_mismo()
    {
        var reloj = new RelojFijo(Inicio);
        var c = Nuevo(reloj);

        c.Estado.Should().Be(EstadoCorreo.Encolado);
        c.LeToca(Inicio).Should().BeTrue();
        c.EnviadoEn.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-un-correo")]
    [InlineData("manolo@")]
    public void Sin_una_direccion_valida_no_se_encola(string? para)
    {
        var r = Dominio.Correo.Crear(
            Empresa, Guid.NewGuid(), Guid.NewGuid(), para, "Asunto", "Cuerpo",
            ParaQue.AtenderSolicitud, null, new RelojFijo(Inicio));

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("correo.destino_invalido");
    }

    [Fact]
    public void El_token_lleva_la_empresa_dentro()
    {
        var c = Nuevo(new RelojFijo(Inicio));

        // Es lo que permite que la petición del píxel —que llega sin sesión— pueda fijar la empresa y
        // que la RLS siga aplicando. Sin esto, la apertura no se apuntaría nunca.
        Dominio.Correo.EmpresaDelToken(c.TokenApertura).Should().Be(Empresa);
    }

    [Fact]
    public void Dos_correos_tienen_tokens_distintos()
    {
        var reloj = new RelojFijo(Inicio);

        // Aunque sean de la misma empresa: la parte que autoriza son los 16 bytes al azar. Si dos
        // correos compartieran token, abrir uno marcaría el otro.
        Nuevo(reloj).TokenApertura.Should().NotBe(Nuevo(reloj).TokenApertura);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corto")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!--------------------")]
    public void Un_token_con_mala_forma_no_da_empresa(string? token)
    {
        // El endpoint del píxel lo llama con lo que le manden, y lo que le manden puede ser cualquier
        // cosa. Nunca una excepción.
        Dominio.Correo.EmpresaDelToken(token).Should().BeNull();
    }

    [Fact]
    public void Al_salir_queda_enviado_y_con_la_fecha()
    {
        var reloj = new RelojFijo(Inicio);
        var c = Nuevo(reloj);

        reloj.Avanzar(TimeSpan.FromSeconds(30));
        c.Salio(reloj);

        c.Estado.Should().Be(EstadoCorreo.Enviado);
        c.EnviadoEn.Should().Be(reloj.AhoraUtc);
        c.ProximoIntentoEn.Should().BeNull();
    }

    [Fact]
    public void Un_fallo_pasajero_se_reintenta_cada_vez_mas_tarde()
    {
        var reloj = new RelojFijo(Inicio);
        var c = Nuevo(reloj);

        c.NoSalio("no contesta", definitivo: false, reloj).Should().BeFalse();
        c.ProximoIntentoEn.Should().Be(Inicio.AddMinutes(1));
        c.LeToca(Inicio).Should().BeFalse();

        reloj.AhoraUtc = c.ProximoIntentoEn!.Value;
        c.NoSalio("no contesta", definitivo: false, reloj);
        c.ProximoIntentoEn.Should().Be(reloj.AhoraUtc.AddMinutes(5));
    }

    [Fact]
    public void Un_fallo_definitivo_no_se_reintenta_ni_una_vez()
    {
        var reloj = new RelojFijo(Inicio);
        var c = Nuevo(reloj);

        // Un buzón que no existe no se arregla insistiendo, y hacerlo cuatro veces es la forma conocida
        // de que un servidor de correo empiece a marcar todo lo que mandas como no deseado.
        c.NoSalio("el servidor rechazó la dirección", definitivo: true, reloj).Should().BeTrue();

        c.Estado.Should().Be(EstadoCorreo.Fallido);
        c.Intentos.Should().Be(1);
        c.ProximoIntentoEn.Should().BeNull();
    }

    [Fact]
    public void Se_rinde_en_pocos_minutos_y_no_en_horas()
    {
        var reloj = new RelojFijo(Inicio);
        var c = Nuevo(reloj);

        for (var i = 1; i < Dominio.Correo.IntentosMaximos; i++)
        {
            c.NoSalio("no contesta", definitivo: false, reloj).Should().BeFalse();
            reloj.AhoraUtc = c.ProximoIntentoEn!.Value;
        }

        c.NoSalio("no contesta", definitivo: false, reloj).Should().BeTrue();
        c.Estado.Should().Be(EstadoCorreo.Fallido);

        // Un correo que sale seis horas tarde ya no sirve: la conversación siguió por otro lado. Por eso
        // aquí se insiste menos que con un webhook, que sí sigue valiendo mañana.
        (reloj.AhoraUtc - Inicio).Should().BeLessThan(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Cancelar_no_es_lo_mismo_que_fallar()
    {
        var reloj = new RelojFijo(Inicio);
        var c = Nuevo(reloj);

        c.Cancelar("El contacto pidió no recibir más comunicaciones.", reloj);

        // Se distingue del fallo para que en la pantalla no parezca que hay que reintentarlo: no ha
        // fallado nada, es que **no había que mandarlo**.
        c.Estado.Should().Be(EstadoCorreo.Cancelado);
        c.Intentos.Should().Be(0);
        c.ProximoIntentoEn.Should().BeNull();
        c.UltimoFallo.Should().Contain("no recibir");
    }

    [Fact]
    public void La_primera_apertura_se_apunta_y_las_siguientes_solo_cuentan()
    {
        var reloj = new RelojFijo(Inicio);
        var c = Nuevo(reloj);
        c.Salio(reloj);

        c.Abierto(reloj).Should().BeTrue("la primera vez sí se apunta en la cronología");
        reloj.Avanzar(TimeSpan.FromHours(2));
        c.Abierto(reloj).Should().BeFalse("cinco líneas de «ha abierto el correo» no dicen más que una");

        c.Aperturas.Should().Be(2);
        c.PrimeraAperturaEn.Should().Be(Inicio);
        c.UltimaAperturaEn.Should().Be(reloj.AhoraUtc);
    }

    [Fact]
    public void Un_correo_que_no_ha_salido_no_se_puede_abrir()
    {
        var reloj = new RelojFijo(Inicio);
        var c = Nuevo(reloj);

        // Si llega una petición del píxel de un correo que nunca se envió, es que alguien está probando
        // tokens a mano. No se cuenta.
        c.Abierto(reloj).Should().BeFalse();
        c.Aperturas.Should().Be(0);
    }

    [Fact]
    public void Un_correo_cancelado_tampoco()
    {
        var reloj = new RelojFijo(Inicio);
        var c = Nuevo(reloj);
        c.Cancelar("de baja", reloj);

        c.Abierto(reloj).Should().BeFalse();
    }
}
