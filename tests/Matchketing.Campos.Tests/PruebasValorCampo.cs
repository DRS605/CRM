using FluentAssertions;
using Matchketing.Campos.Dominio;
using Matchketing.Nucleo.Resultados;
using Xunit;

namespace Matchketing.Campos.Tests;

public sealed class PruebasValorCampo
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero));

    private static CampoPropio Campo(TipoCampo tipo, IReadOnlyList<string>? opciones = null) =>
        CampoPropio.Crear(Empresa, Ambito.Contacto, "Un campo", tipo, opciones, 0, Reloj).Valor;

    private static Resultado<string> Normalizar(TipoCampo tipo, string? valor, IReadOnlyList<string>? opciones = null) =>
        ValorCampo.Normalizar(Campo(tipo, opciones), valor);

    [Fact]
    public void Un_valor_vacio_no_se_guarda()
    {
        // Una fila con la cadena vacía y una fila que no existe significan lo mismo para quien lee, y
        // tener las dos formas de decir «no hay dato» garantiza que algún día una pantalla enseñe «—» y
        // otra enseñe nada.
        Normalizar(TipoCampo.Texto, null).Error!.Codigo.Should().Be("valor.vacio");
        Normalizar(TipoCampo.Texto, "   ").Error!.Codigo.Should().Be("valor.vacio");
    }

    // ---- Números ----

    [Fact]
    public void Un_numero_se_acepta_con_coma_y_se_guarda_con_punto()
    {
        // Las dos formas se teclean en España. Guardarlas tal cual daría dos valores distintos para el
        // mismo número, y la exportación saldría con las dos mezcladas.
        Normalizar(TipoCampo.Numero, "3,5").Valor.Should().Be("3.5");
        Normalizar(TipoCampo.Numero, "3.5").Valor.Should().Be("3.5");
        Normalizar(TipoCampo.Numero, " 12 ").Valor.Should().Be("12");
        Normalizar(TipoCampo.Numero, "-4,25").Valor.Should().Be("-4.25");
    }

    [Fact]
    public void Lo_que_no_es_un_numero_se_rechaza_diciendo_lo_que_se_escribio()
    {
        var r = Normalizar(TipoCampo.Numero, "doce kilovatios");

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("valor.no_es_numero");
        r.Error!.Mensaje.Should().Contain("doce kilovatios", "hay que devolverle lo que escribió, no un genérico");
    }

    // ---- Fechas ----

    [Fact]
    public void Una_fecha_solo_se_acepta_como_la_manda_el_navegador()
    {
        // Aceptar «12/03/2026» obligaría a decidir si el 12 es el día o el mes, y esa decisión no se
        // puede acertar. El `<input type="date">` manda siempre aaaa-mm-dd.
        Normalizar(TipoCampo.Fecha, "2026-03-12").Valor.Should().Be("2026-03-12");
        Normalizar(TipoCampo.Fecha, "12/03/2026").Error!.Codigo.Should().Be("valor.no_es_fecha");
        Normalizar(TipoCampo.Fecha, "2026-13-01").Error!.Codigo.Should().Be("valor.no_es_fecha");
    }

    [Fact]
    public void El_mensaje_de_la_fecha_dice_como_se_escribe()
    {
        Normalizar(TipoCampo.Fecha, "ayer").Error!.Mensaje.Should().Contain("2026-03-12");
    }

    // ---- Sí o no ----

    [Fact]
    public void Si_y_no_se_aceptan_como_los_escribe_la_gente()
    {
        foreach (var si in new[] { "sí", "Si", "S", "true", "1", "verdadero" })
        {
            Normalizar(TipoCampo.SiNo, si).Valor.Should().Be("si", $"«{si}» es un sí");
        }

        foreach (var no in new[] { "No", "n", "false", "0", "falso" })
        {
            Normalizar(TipoCampo.SiNo, no).Valor.Should().Be("no", $"«{no}» es un no");
        }

        Normalizar(TipoCampo.SiNo, "quizá").Error!.Codigo.Should().Be("valor.no_es_si_ni_no");
    }

    // ---- Listas ----

    [Fact]
    public void Un_valor_de_lista_se_guarda_tal_como_esta_escrito_en_el_campo()
    {
        // Se devuelve la opción del campo, no lo que teclearon: así todos los valores de esa lista son
        // idénticos y se pueden agrupar. Sin esto, «Gas» y «gas» serían dos grupos en cualquier recuento.
        var opciones = new[] { "Gas natural", "Eléctrica" };

        Normalizar(TipoCampo.Lista, "gas natural", opciones).Valor.Should().Be("Gas natural");
        Normalizar(TipoCampo.Lista, "  ELECTRICA  ", opciones).Valor.Should().Be("Eléctrica");
    }

    [Fact]
    public void Un_valor_que_no_esta_en_la_lista_se_rechaza_diciendo_las_opciones()
    {
        var r = Normalizar(TipoCampo.Lista, "Biomasa", ["Gas natural", "Eléctrica"]);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("valor.fuera_de_la_lista");
        r.Error!.Mensaje.Should().Contain("Gas natural").And.Contain("Eléctrica",
            "decir qué opciones hay ahorra el viaje de ir a mirarlas");
    }

    // ---- Texto ----

    [Fact]
    public void El_texto_tiene_techo()
    {
        Normalizar(TipoCampo.Texto, new string('a', ValorCampo.LongitudMaximaTexto)).Exito.Should().BeTrue();
        Normalizar(TipoCampo.Texto, new string('a', ValorCampo.LongitudMaximaTexto + 1))
            .Error!.Codigo.Should().Be("valor.texto_largo");
    }

    // ---- El valor completo ----

    [Fact]
    public void Un_valor_lleva_el_ambito_de_su_campo_copiado()
    {
        // Se repite a propósito: sin él, borrar los valores de un contacto obligaría a cruzar con la tabla
        // de campos, y esa consulta está en el camino de la supresión del artículo 17.
        var campo = CampoPropio.Crear(Empresa, Ambito.Cuenta, "Sector CNAE", TipoCampo.Texto, null, 0, Reloj).Valor;
        var entidad = Guid.NewGuid();

        var v = ValorCampo.Crear(Empresa, campo, entidad, "4321", Reloj);

        v.Exito.Should().BeTrue();
        v.Valor.Ambito.Should().Be(Ambito.Cuenta);
        v.Valor.CampoId.Should().Be(campo.Id);
        v.Valor.EntidadId.Should().Be(entidad);
    }

    [Fact]
    public void Un_valor_es_de_alguien()
    {
        ValorCampo.Crear(Empresa, Campo(TipoCampo.Texto), Guid.Empty, "algo", Reloj)
            .Error!.Codigo.Should().Be("valor.sin_entidad");
    }

    [Fact]
    public void Cambiar_un_valor_lo_vuelve_a_normalizar_y_apunta_cuando()
    {
        var reloj = new RelojFijo(new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero));
        var campo = Campo(TipoCampo.Numero);
        var v = ValorCampo.Crear(Empresa, campo, Guid.NewGuid(), "10", reloj).Valor;

        reloj.AhoraUtc = reloj.AhoraUtc.AddDays(1);
        v.Cambiar(campo, "12,5", reloj).Exito.Should().BeTrue();

        v.Texto.Should().Be("12.5");
        v.ActualizadoEn.Should().Be(reloj.AhoraUtc);
    }

    [Fact]
    public void Un_cambio_rechazado_no_toca_el_valor_que_habia()
    {
        var campo = Campo(TipoCampo.Numero);
        var v = ValorCampo.Crear(Empresa, campo, Guid.NewGuid(), "10", Reloj).Valor;

        v.Cambiar(campo, "no es un número", Reloj).Fallido.Should().BeTrue();

        v.Texto.Should().Be("10");
    }
}
