using FluentAssertions;
using Matchketing.Correo.Dominio;
using Xunit;

namespace Matchketing.Correo.Tests;

/// <summary>
/// Los huecos de una plantilla. Lo que se prueba aquí es una sola cosa: **que no salga un correo con
/// «Hola {{nombre}},» ni con «Hola ,»**. Las dos se ven igual de mal y las dos se descubren cuando ya
/// lo ha leído el cliente.
/// </summary>
public sealed class PruebasCampos
{
    private static readonly DatosDelEnvio Completos =
        new("Manolo", "Bar Casa Manolo", "Marta Ruiz", "Instalaciones Ribera", "manolo@casamanolo.es");

    [Fact]
    public void Rellenar_pone_cada_dato_en_su_hueco()
    {
        var r = Campos.Rellenar("Hola {{nombre}}, soy {{comercial}} de {{empresa}}. Sobre {{cuenta}}…", Completos);

        r.Exito.Should().BeTrue();
        r.Valor.Should().Be("Hola Manolo, soy Marta Ruiz de Instalaciones Ribera. Sobre Bar Casa Manolo…");
    }

    [Fact]
    public void Un_hueco_repetido_se_rellena_las_dos_veces()
    {
        var r = Campos.Rellenar("{{nombre}}, un momento {{nombre}}", Completos);

        r.Valor.Should().Be("Manolo, un momento Manolo");
    }

    [Fact]
    public void Los_espacios_dentro_del_hueco_no_molestan()
    {
        // Alguien lo va a escribir con espacios. Rechazarlo sería una tarde perdida por nada.
        Campos.Rellenar("Hola {{ nombre }}", Completos).Valor.Should().Be("Hola Manolo");
    }

    [Fact]
    public void Un_texto_sin_huecos_sale_igual()
    {
        Campos.Rellenar("Buenos días. Le llamo mañana.", Completos).Valor
            .Should().Be("Buenos días. Le llamo mañana.");
    }

    [Fact]
    public void Las_llaves_sueltas_no_son_un_hueco()
    {
        // Un `{` solo, o `{}`, es texto. Solo cuenta la pareja doble.
        Campos.Rellenar("Coste: {1} y {precio}", Completos).Valor.Should().Be("Coste: {1} y {precio}");
    }

    [Fact]
    public void Un_hueco_sin_valor_no_se_envia()
    {
        var sinCuenta = Completos with { Cuenta = null };

        var r = Campos.Rellenar("Sobre {{cuenta}}", sinCuenta);

        // «Sobre ,» es peor que no mandar nada: se nota que viene de una máquina, que es justo lo que
        // la plantilla intentaba disimular.
        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("correo.campo_sin_valor");

        // Y dice qué hacer, que es la mitad de un buen mensaje de error.
        r.Error.Mensaje.Should().Contain("cuenta").And.Contain("ficha");
    }

    [Fact]
    public void Un_hueco_en_blanco_tampoco_vale()
    {
        var enBlanco = Completos with { Nombre = "   " };

        Campos.Rellenar("Hola {{nombre}}", enBlanco).Fallido.Should().BeTrue();
    }

    [Theory]
    [InlineData("Hola {{nombre}}")]
    [InlineData("{{cuenta}} y {{empresa}}")]
    [InlineData("Sin huecos")]
    [InlineData("")]
    public void Validar_acepta_lo_que_existe(string texto) =>
        Campos.Validar(texto).Exito.Should().BeTrue();

    [Theory]
    [InlineData("Hola {{Nombre}}")]           // mayúscula: los campos son en minúscula
    [InlineData("Hola {{cargo}}")]
    [InlineData("Hola {{telefono}}")]
    [InlineData("Hola {{}}")]
    public void Validar_rechaza_un_hueco_que_no_existe(string texto)
    {
        var r = Campos.Validar(texto);

        // Se rechaza **al guardar la plantilla**, no al enviar. Dejarlo pasar significa que el correo
        // saldría con las llaves puestas, y eso no se descubre hasta que alguien lo lee.
        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("plantilla.campo_desconocido");
    }

    [Fact]
    public void Un_hueco_sin_cerrar_se_rechaza_y_se_dice()
    {
        var r = Campos.Validar("Hola {{nombre");

        r.Fallido.Should().BeTrue();
        r.Error!.Mensaje.Should().Contain("sin cerrar");
    }

    [Fact]
    public void El_mensaje_de_error_enumera_los_campos_que_si_existen()
    {
        var r = Campos.Validar("{{apellido}}");

        // Quien se equivoca escribiendo una plantilla necesita saber qué puede usar, no solo que se ha
        // equivocado.
        foreach (var campo in Campos.Todos)
        {
            r.Error!.Mensaje.Should().Contain(campo);
        }
    }
}
