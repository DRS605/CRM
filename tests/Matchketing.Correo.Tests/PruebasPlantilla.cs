using FluentAssertions;
using Matchketing.Correo.Dominio;
using Xunit;

namespace Matchketing.Correo.Tests;

public sealed class PruebasPlantilla
{
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

    private static Matchketing.Nucleo.Resultados.Resultado<Plantilla> Crear(
        string? nombre = "Seguimiento", string? asunto = "Sobre {{cuenta}}",
        string? cuerpo = "Hola {{nombre}}, te llamo mañana. {{comercial}}", ParaQue paraQue = ParaQue.AtenderSolicitud) =>
        Plantilla.Crear(Guid.NewGuid(), nombre, asunto, cuerpo, paraQue, Reloj);

    [Fact]
    public void Una_plantilla_valida_se_crea()
    {
        var r = Crear();

        r.Exito.Should().BeTrue();
        r.Valor.Usos.Should().Be(0);
        r.Valor.ParaQue.Should().Be(ParaQue.AtenderSolicitud);
    }

    [Fact]
    public void Se_recortan_los_espacios_de_los_extremos()
    {
        var r = Crear(nombre: "  Seguimiento  ", asunto: "  Hola  ", cuerpo: "  Qué tal  ");

        r.Valor.Nombre.Should().Be("Seguimiento");
        r.Valor.Asunto.Should().Be("Hola");
        r.Valor.Cuerpo.Should().Be("Qué tal");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_nombre_no_hay_plantilla(string? nombre) =>
        Crear(nombre: nombre).Error!.Codigo.Should().Be("plantilla.nombre_invalido");

    [Fact]
    public void Sin_asunto_tampoco()
    {
        // Un correo sin asunto acaba en la carpeta de no deseados. No es una validación de formulario,
        // es la diferencia entre que lo lean y que no.
        Crear(asunto: "  ").Error!.Codigo.Should().Be("plantilla.asunto_invalido");
    }

    [Fact]
    public void Ni_sin_cuerpo() =>
        Crear(cuerpo: null).Error!.Codigo.Should().Be("plantilla.cuerpo_invalido");

    [Fact]
    public void Un_cuerpo_larguisimo_se_rechaza()
    {
        // Ocho mil caracteres no los lee nadie, y el límite existe para que quien pegue un contrato
        // entero se dé cuenta aquí y no al enviarlo.
        Crear(cuerpo: new string('a', Plantilla.LongitudMaximaCuerpo + 1))
            .Error!.Codigo.Should().Be("plantilla.cuerpo_invalido");
    }

    [Fact]
    public void Un_hueco_inventado_se_rechaza_al_guardar_este_en_el_asunto()
    {
        Crear(asunto: "Sobre {{sector}}").Error!.Codigo.Should().Be("plantilla.campo_desconocido");
    }

    [Fact]
    public void Y_tambien_en_el_cuerpo()
    {
        Crear(cuerpo: "Hola {{apodo}}").Error!.Codigo.Should().Be("plantilla.campo_desconocido");
    }

    [Fact]
    public void Redactar_devuelve_asunto_y_cuerpo_rellenos()
    {
        var plantilla = Crear().Valor;
        var datos = new DatosDelEnvio("Manolo", "Bar Casa Manolo", "Marta", "Ribera", "m@c.es");

        var r = plantilla.Redactar(datos);

        r.Exito.Should().BeTrue();
        r.Valor.Asunto.Should().Be("Sobre Bar Casa Manolo");
        r.Valor.Cuerpo.Should().Be("Hola Manolo, te llamo mañana. Marta");
    }

    [Fact]
    public void Redactar_falla_si_falta_un_dato_del_asunto()
    {
        var plantilla = Crear().Valor;
        var sinCuenta = new DatosDelEnvio("Manolo", null, "Marta", "Ribera", "m@c.es");

        // Y falla **antes** de mirar el cuerpo: no tiene sentido rellenar medio correo.
        plantilla.Redactar(sinCuenta).Error!.Codigo.Should().Be("correo.campo_sin_valor");
    }

    [Fact]
    public void Cambiar_valida_igual_que_crear()
    {
        var plantilla = Crear().Valor;

        plantilla.Cambiar("Otra", "Sobre {{loquesea}}", "Hola", ParaQue.Comercial)
            .Error!.Codigo.Should().Be("plantilla.campo_desconocido");

        // Y un cambio rechazado no deja la plantilla a medias.
        plantilla.Nombre.Should().Be("Seguimiento");
        plantilla.ParaQue.Should().Be(ParaQue.AtenderSolicitud);
    }

    [Fact]
    public void Usada_cuenta_los_usos()
    {
        var plantilla = Crear().Valor;

        plantilla.Usada();
        plantilla.Usada();

        // Sirve para ordenar la lista por lo que de verdad se usa, que en una lista de cuarenta es la
        // diferencia entre encontrarla y buscarla.
        plantilla.Usos.Should().Be(2);
    }
}
