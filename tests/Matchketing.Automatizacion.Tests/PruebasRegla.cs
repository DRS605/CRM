using FluentAssertions;
using Matchketing.Automatizacion.Dominio;
using Matchketing.Nucleo.Tiempo;
using Xunit;

namespace Matchketing.Automatizacion.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; set; } = ahora;
}

public sealed class PruebasRegla
{
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

    private static readonly Hechos Manolo = new("Valencia", "feria", "Hostelería", 18400m, null);

    private static Matchketing.Nucleo.Resultados.Resultado<Regla> Crear(
        string? nombre = "Leads de Valencia",
        Disparador disparador = Disparador.LeadCreado,
        IReadOnlyCollection<Condicion>? condiciones = null,
        IReadOnlyCollection<Accion>? acciones = null) =>
        Regla.Crear(Guid.NewGuid(), nombre, disparador,
            condiciones ?? [new Condicion(Campo.Provincia, Operador.Es, "Valencia")],
            acciones ?? [Accion.Tarea("Llamar", 0)], Reloj);

    [Fact]
    public void Nace_apagada()
    {
        // Una regla que empieza a disparar en el mismo segundo en que se guarda no da tiempo a leerla, y
        // lo que hace no se deshace: las tareas creadas están creadas y los correos mandados están
        // mandados.
        Crear().Valor.Activa.Should().BeFalse();
    }

    [Fact]
    public void Se_lee_de_un_tiron_y_en_castellano()
    {
        var regla = Crear(
            condiciones: [new Condicion(Campo.Provincia, Operador.Es, "Valencia"),
                          new Condicion(Campo.Importe, Operador.MayorQue, "10000")],
            disparador: Disparador.OportunidadGanada,
            acciones: [Accion.Tarea("Pedir referencia", 30), Accion.Nota("Cliente grande")]).Valor;

        // Es la prueba de que no hace falta un lienzo de ramas: la regla entera cabe en una frase.
        regla.Leer().Should().Be(
            "Si pasa «oportunidad.ganada» y provincia es «Valencia» y importe es mayor que «10000», " +
            "entonces crear la tarea «Pedir referencia» para dentro de 30 días, y apuntar «Cliente grande».");
    }

    [Fact]
    public void Una_regla_sin_condiciones_es_valida_y_se_lee_bien()
    {
        var regla = Crear(condiciones: []).Valor;

        // «Cuando entre un lead, mándale el acuse de recibo» no necesita condiciones.
        regla.Leer().Should().StartWith("Si pasa «lead.creado», entonces");
        regla.Encender();
        regla.Aplica(Disparador.LeadCreado, Manolo).Should().BeTrue();
    }

    [Fact]
    public void Aplica_solo_si_esta_encendida()
    {
        var regla = Crear().Valor;

        regla.Aplica(Disparador.LeadCreado, Manolo).Should().BeFalse("está apagada");
        regla.Encender();
        regla.Aplica(Disparador.LeadCreado, Manolo).Should().BeTrue();
    }

    [Fact]
    public void Aplica_solo_a_su_disparador()
    {
        var regla = Crear().Valor;
        regla.Encender();

        regla.Aplica(Disparador.OportunidadGanada, Manolo).Should().BeFalse();
    }

    [Fact]
    public void Todas_las_condiciones_o_ninguna_nunca_una_o_otra()
    {
        var regla = Crear(condiciones:
        [
            new Condicion(Campo.Provincia, Operador.Es, "Valencia"),
            new Condicion(Campo.Sector, Operador.Es, "Construcción"),
        ]).Valor;
        regla.Encender();

        // Cumple la primera y no la segunda. No hay «o» a propósito: en cuanto se mezclan «y» y «o» hace
        // falta paréntesis, y con paréntesis hace falta un lienzo de ramas.
        regla.Aplica(Disparador.LeadCreado, Manolo).Should().BeFalse();
    }

    [Fact]
    public void Cambiarla_la_apaga()
    {
        var regla = Crear().Valor;
        regla.Encender();

        regla.Cambiar("Otro nombre", Disparador.LeadCreado,
            [new Condicion(Campo.Provincia, Operador.Es, "Alicante")], [Accion.Nota("Hola")]).Exito.Should().BeTrue();

        // Un cambio a medias que siga disparando es la forma más rápida de mandar cien correos que nadie
        // quería.
        regla.Activa.Should().BeFalse();
    }

    [Fact]
    public void Un_cambio_rechazado_no_la_deja_a_medias()
    {
        var regla = Crear().Valor;

        regla.Cambiar("Otro", Disparador.LeadCreado, [], []).Fallido.Should().BeTrue();

        regla.Nombre.Should().Be("Leads de Valencia");
        regla.Acciones.Should().HaveCount(1);
    }

    // ---------- Lo que no se puede guardar ----------

    [Fact]
    public void Sin_acciones_no_hay_regla() =>
        Crear(acciones: []).Error!.Codigo.Should().Be("regla.sin_acciones");

    [Fact]
    public void Mas_de_tres_condiciones_se_rechaza_y_se_sugiere_partirla()
    {
        var r = Crear(condiciones:
        [
            new Condicion(Campo.Provincia, Operador.Es, "Valencia"),
            new Condicion(Campo.Sector, Operador.Es, "Hostelería"),
            new Condicion(Campo.Origen, Operador.Es, "feria"),
            new Condicion(Campo.Origen, Operador.Contiene, "web"),
        ]);

        r.Error!.Codigo.Should().Be("regla.demasiadas_condiciones");
        r.Error.Mensaje.Should().Contain("dos reglas");
    }

    [Fact]
    public void Mas_de_cuatro_acciones_se_rechaza() =>
        Crear(acciones: [Accion.Nota("a"), Accion.Nota("b"), Accion.Nota("c"), Accion.Nota("d"), Accion.Nota("e")])
            .Error!.Codigo.Should().Be("regla.demasiadas_acciones");

    [Fact]
    public void Una_condicion_de_importe_con_disparador_de_contacto_no_se_cumpliria_nunca()
    {
        var r = Crear(
            disparador: Disparador.LeadCreado,
            condiciones: [new Condicion(Campo.Importe, Operador.MayorQue, "10000")]);

        // Es la validación que más tiempo ahorra: sin ella la regla se guarda, se enciende y no hace nada,
        // y no hay forma de saber por qué mirándola.
        r.Error!.Codigo.Should().Be("regla.condicion_imposible");
        r.Error.Mensaje.Should().Contain("no se cumpliría nunca");
    }

    [Fact]
    public void El_motivo_de_perdida_solo_existe_al_perder()
    {
        Crear(
            disparador: Disparador.OportunidadGanada,
            condiciones: [new Condicion(Campo.MotivoPerdida, Operador.Es, "Precio")])
            .Error!.Codigo.Should().Be("regla.condicion_imposible");

        Regla.Crear(Guid.NewGuid(), "Perdidas por precio", Disparador.OportunidadPerdida,
            [new Condicion(Campo.MotivoPerdida, Operador.Es, "Precio")],
            [Accion.Tarea("Volver en seis meses", 180)], Reloj)
            .Exito.Should().BeTrue();
    }

    [Fact]
    public void Una_accion_mal_puesta_se_rechaza_con_su_motivo()
    {
        Crear(acciones: [Accion.Tarea("  ", 0)]).Error!.Codigo.Should().Be("regla.tarea_sin_titulo");
        Crear(acciones: [Accion.Tarea("Llamar", 400)]).Error!.Codigo.Should().Be("regla.tarea_plazo_invalido");
        Crear(acciones: [Accion.Correo(Guid.Empty)]).Error!.Codigo.Should().Be("regla.sin_plantilla");
        Crear(acciones: [Accion.Asignar(Guid.Empty)]).Error!.Codigo.Should().Be("regla.sin_comercial");
        Crear(acciones: [Accion.Nota("   ")]).Error!.Codigo.Should().Be("regla.nota_vacia");
    }

    [Fact]
    public void Sin_nombre_no_hay_regla() =>
        Crear(nombre: "  ").Error!.Codigo.Should().Be("regla.nombre_invalido");

    [Fact]
    public void Disparada_cuenta_las_veces()
    {
        var regla = Crear().Valor;

        regla.Disparada(Reloj);
        regla.Disparada(Reloj);

        regla.Veces.Should().Be(2);
        regla.UltimaVezEn.Should().Be(Reloj.AhoraUtc);
    }
}
