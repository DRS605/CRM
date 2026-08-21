using FluentAssertions;
using Matchketing.Campanias.Dominio;
using Xunit;

namespace Matchketing.Campanias.Tests;

public sealed class PruebasCampania
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();

    private static Campania Nueva(RelojFijo reloj) =>
        Campania.Crear(Empresa, "Oferta de primavera", Guid.NewGuid(), Guid.NewGuid(), reloj).Valor;

    private static RelojFijo Reloj() => new(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Una_campania_nace_en_borrador_y_sin_nada_enviado()
    {
        var c = Nueva(Reloj());

        c.Estado.Should().Be(EstadoCampania.Borrador);
        c.EsBorrador.Should().BeTrue();
        c.Destinatarios.Should().Be(0);
        c.LanzadaEn.Should().BeNull();
        c.LanzadaPor.Should().BeNull();
    }

    [Fact]
    public void Hay_que_decir_a_quien_y_que()
    {
        var reloj = Reloj();

        Campania.Crear(Empresa, "X", Guid.Empty, Guid.NewGuid(), reloj).Error!.Codigo
            .Should().Be("campania.sin_segmento");

        Campania.Crear(Empresa, "X", Guid.NewGuid(), Guid.Empty, reloj).Error!.Codigo
            .Should().Be("campania.sin_plantilla");

        Campania.Crear(Empresa, " ", Guid.NewGuid(), Guid.NewGuid(), reloj).Error!.Codigo
            .Should().Be("campania.sin_nombre");
    }

    [Fact]
    public void No_se_lanza_a_un_segmento_vacio()
    {
        // Si se dejara, la campaña quedaría en la lista como «enviada» sin haber salido nada, y nadie
        // volvería a mirarla. Que falle mientras quien la lanza se acuerda de qué segmento eligió.
        var reloj = Reloj();
        var c = Nueva(reloj);

        var r = c.Lanzar(Usuario, 0, "clientes", reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campania.segmento_vacio");
        c.Estado.Should().Be(EstadoCampania.Borrador);
    }

    [Fact]
    public void Una_campania_la_tiene_que_lanzar_alguien()
    {
        var reloj = Reloj();
        var c = Nueva(reloj);

        c.Lanzar(Guid.Empty, 10, "clientes", reloj).Error!.Codigo.Should().Be("campania.sin_firma");
    }

    [Fact]
    public void Hay_un_techo_de_destinatarios_y_el_mensaje_dice_cuantos_hay()
    {
        var reloj = Reloj();
        var c = Nueva(reloj);

        var r = c.Lanzar(Usuario, Campania.MaximoDestinatarios + 1, "clientes", reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campania.demasiados");
        r.Error!.Mensaje.Should().Contain(Campania.MaximoDestinatarios.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Justo_el_maximo_si_se_puede_lanzar()
    {
        var reloj = Reloj();
        var c = Nueva(reloj);

        c.Lanzar(Usuario, Campania.MaximoDestinatarios, "clientes", reloj).Exito.Should().BeTrue();
    }

    [Fact]
    public void Al_lanzar_se_guarda_quien_y_la_frase_del_segmento()
    {
        var reloj = Reloj();
        var c = Nueva(reloj);

        c.Lanzar(Usuario, 3, "  Clientes: clientes, de Valencia  ", reloj).Exito.Should().BeTrue();

        c.Estado.Should().Be(EstadoCampania.Enviando);
        c.LanzadaPor.Should().Be(Usuario);
        c.LanzadaEn.Should().Be(reloj.AhoraUtc);
        c.Destinatarios.Should().Be(3);
        c.Pendientes.Should().Be(3);

        // Recortada, y guardada porque el segmento se puede editar o borrar después: la campaña tiene
        // que poder seguir diciendo a quién apuntaba el día que salió.
        c.SegmentoAlLanzar.Should().Be("Clientes: clientes, de Valencia");
    }

    [Fact]
    public void Una_campania_lanzada_no_se_edita_ni_se_relanza()
    {
        var reloj = Reloj();
        var c = Nueva(reloj);
        c.Lanzar(Usuario, 2, "clientes", reloj);

        c.Cambiar("Otro nombre", Guid.NewGuid(), Guid.NewGuid()).Error!.Codigo.Should().Be("campania.ya_lanzada");
        c.Lanzar(Usuario, 2, "clientes", reloj).Error!.Codigo.Should().Be("campania.ya_lanzada");
    }

    [Fact]
    public void Se_cierra_sola_cuando_no_queda_nadie_pendiente()
    {
        // No hay un método público para cerrarla. Un estado final que depende de que alguien pulse algo
        // es un estado que se queda a medias, y una campaña «enviando» eterna no dice nada.
        var reloj = Reloj();
        var c = Nueva(reloj);
        c.Lanzar(Usuario, 3, "clientes", reloj);

        c.Anotar(encolado: true, reloj);
        c.Anotar(encolado: false, reloj);
        c.Estado.Should().Be(EstadoCampania.Enviando);
        c.Pendientes.Should().Be(1);

        reloj.Avanzar(TimeSpan.FromMinutes(5));
        c.Anotar(encolado: true, reloj);

        c.Estado.Should().Be(EstadoCampania.Enviada);
        c.TerminadaEn.Should().Be(reloj.AhoraUtc);
        c.Encolados.Should().Be(2);
        c.Excluidos.Should().Be(1);
        c.Pendientes.Should().Be(0);
        c.Cerrada.Should().BeTrue();
    }

    [Fact]
    public void Detener_deja_de_encolar_y_los_pendientes_cuentan_como_excluidos()
    {
        var reloj = Reloj();
        var c = Nueva(reloj);
        c.Lanzar(Usuario, 10, "clientes", reloj);
        c.Anotar(encolado: true, reloj);
        c.Anotar(encolado: true, reloj);

        c.Detener(reloj).Exito.Should().BeTrue();
        c.DescartarPendientes(8);

        c.Estado.Should().Be(EstadoCampania.Detenida);
        c.Encolados.Should().Be(2, "lo ya encolado sale igual: un correo en el buzón de salida no se recoge");
        c.Excluidos.Should().Be(8);
        c.Pendientes.Should().Be(0);
        c.Cerrada.Should().BeTrue();
    }

    [Fact]
    public void Detener_una_campania_que_ya_no_esta_enviando_no_hace_nada()
    {
        var reloj = Reloj();
        var c = Nueva(reloj);

        c.Detener(reloj).Error!.Codigo.Should().Be("campania.no_en_marcha");

        c.Lanzar(Usuario, 1, "clientes", reloj);
        c.Anotar(encolado: true, reloj);

        // Ya está enviada. Detenerla sería cambiar un estado final por otro.
        c.Detener(reloj).Error!.Codigo.Should().Be("campania.no_en_marcha");
        c.Estado.Should().Be(EstadoCampania.Enviada);
    }

    [Fact]
    public void Una_campania_detenida_no_se_vuelve_a_cerrar_al_anotar()
    {
        // Puede llegar una anotación tardía: la pasada del trabajo estaba a medias cuando alguien la
        // detuvo. Eso no debe devolverla a «enviada», que diría que terminó como estaba previsto.
        var reloj = Reloj();
        var c = Nueva(reloj);
        c.Lanzar(Usuario, 2, "clientes", reloj);
        c.Detener(reloj);
        c.DescartarPendientes(2);

        c.Anotar(encolado: true, reloj);

        c.Estado.Should().Be(EstadoCampania.Detenida);
    }
}

public sealed class PruebasEnvioCampania
{
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Nace_pendiente_y_sin_correo()
    {
        var e = EnvioCampania.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        e.Estado.Should().Be(EstadoEnvio.Pendiente);
        e.CorreoId.Should().BeNull();
        e.Motivo.Should().BeNull();
        e.ResueltoEn.Should().BeNull();
    }

    [Fact]
    public void No_se_le_puede_encolar_dos_veces_el_mismo_correo()
    {
        // Es la guarda que evita que una persona reciba el correo dos veces si una pasada del trabajo se
        // solapa con la siguiente. Sin ella, el doble envío sería un fallo raro y difícil de reproducir.
        var e = EnvioCampania.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var primero = Guid.NewGuid();

        e.Encolar(primero, Reloj).Should().BeTrue();
        e.Encolar(Guid.NewGuid(), Reloj).Should().BeFalse();

        e.CorreoId.Should().Be(primero);
    }

    [Fact]
    public void Un_envio_ya_encolado_no_se_puede_excluir_despues()
    {
        var e = EnvioCampania.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        e.Encolar(Guid.NewGuid(), Reloj);

        e.Excluir("por lo que sea", Reloj).Should().BeFalse();
        e.Estado.Should().Be(EstadoEnvio.Encolado);
        e.Motivo.Should().BeNull();
    }

    [Fact]
    public void El_motivo_se_recorta_en_vez_de_perder_la_campania()
    {
        // El motivo viene de otro módulo —el mensaje de la comprobación de permiso—. Perder una campaña
        // entera porque un módulo vecino devolvió un texto largo sería absurdo.
        var e = EnvioCampania.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        e.Excluir(new string('x', EnvioCampania.LongitudMaximaMotivo + 50), Reloj).Should().BeTrue();

        e.Motivo!.Length.Should().Be(EnvioCampania.LongitudMaximaMotivo);
    }

    [Fact]
    public void Un_motivo_vacio_se_convierte_en_algo_legible()
    {
        var e = EnvioCampania.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        e.Excluir("   ", Reloj);

        e.Motivo.Should().Be("Sin motivo apuntado.");
    }
}
