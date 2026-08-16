using FluentAssertions;
using Matchketing.Match.Dominio;
using Xunit;

namespace Matchketing.Match.Tests;

public sealed class PruebasRepartidor
{
    private static CandidatoComercial Comercial(
        string nombre, string[]? zonas = null, int ganadas = 5, int cerradas = 10,
        int abiertas = 3, double? horas = 4) =>
        new(Guid.NewGuid(), nombre, zonas ?? [], ganadas, cerradas, abiertas, horas);

    [Fact]
    public void Sin_comerciales_no_hay_a_quien_asignar()
    {
        Repartidor.Repartir([], "Valencia", "Hostelería").Should().BeNull();
    }

    [Fact]
    public void Quien_lleva_la_zona_gana_a_quien_no()
    {
        var deZona = Comercial("Marta", ["Valencia", "Castellón"]);
        var deFuera = Comercial("Pau", ["Lugo"]);

        var r = Repartidor.Repartir([deFuera, deZona], "Valencia", "Hostelería");

        r!.Nombre.Should().Be("Marta");
        r.Motivos.Should().Contain(m => m.Contains("lleva Valencia", StringComparison.Ordinal));
    }

    [Fact]
    public void A_igualdad_de_todo_gana_quien_menos_carga_tiene()
    {
        var cargado = Comercial("Pau", ["Valencia"], abiertas: 12);
        var libre = Comercial("Marta", ["Valencia"], abiertas: 0);

        var r = Repartidor.Repartir([cargado, libre], "Valencia", "Hostelería");

        r!.Nombre.Should().Be("Marta");
        r.Motivos.Should().Contain("tiene hueco");
    }

    [Fact]
    public void Quien_cierra_mejor_ese_sector_tiene_ventaja()
    {
        var bueno = Comercial("Marta", ganadas: 9, cerradas: 10);
        var flojo = Comercial("Pau", ganadas: 1, cerradas: 10);

        var r = Repartidor.Repartir([flojo, bueno], null, "Hostelería");

        r!.Nombre.Should().Be("Marta");
        r.Motivos.Should().Contain(m => m.Contains("cierra el 90 %", StringComparison.Ordinal));
    }

    [Fact]
    public void Quien_responde_mas_rapido_tiene_ventaja()
    {
        var rapida = Comercial("Marta", horas: 0.5);
        var lento = Comercial("Pau", horas: 20);

        var r = Repartidor.Repartir([lento, rapida], null, null);

        r!.Nombre.Should().Be("Marta");
    }

    [Fact]
    public void A_quien_acaba_de_entrar_no_se_le_penaliza_por_no_tener_historico()
    {
        // La persona nueva no tiene cierres ni tiempos, pero sí la agenda vacía. Si se la
        // penalizara por no tener histórico, nunca recibiría un lead y nunca lo tendría.
        var nueva = Comercial("Nueva", ["Valencia"], ganadas: 0, cerradas: 0, abiertas: 0, horas: null);
        var veterano = Comercial("Veterano", ["Valencia"], ganadas: 5, cerradas: 10, abiertas: 10, horas: 8);

        var r = Repartidor.Repartir([veterano, nueva], "Valencia", "Hostelería");

        r!.Nombre.Should().Be("Nueva");
    }

    [Fact]
    public void Siempre_se_devuelve_al_menos_un_motivo()
    {
        var uno = Comercial("Único", ganadas: 0, cerradas: 0, abiertas: 0, horas: null);

        var r = Repartidor.Repartir([uno], null, null);

        r!.Motivos.Should().NotBeEmpty("una asignación sin explicación parece una lotería");
    }

    [Fact]
    public void Los_puntos_nunca_se_salen_de_cero_a_cien()
    {
        var perfecto = Comercial("Perfecta", ["Valencia"], ganadas: 10, cerradas: 10, abiertas: 0, horas: 0.1);

        var r = Repartidor.Repartir([perfecto], "Valencia", "Hostelería");

        r!.Puntos.Should().BeInRange(0, 100);
    }
}
