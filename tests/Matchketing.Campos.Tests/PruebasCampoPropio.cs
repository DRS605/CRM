using FluentAssertions;
using Matchketing.Campos.Dominio;
using Matchketing.Nucleo.Resultados;
using Xunit;

namespace Matchketing.Campos.Tests;

public sealed class PruebasCampoPropio
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero));

    private static Resultado<CampoPropio> Crear(
        string? nombre, TipoCampo tipo = TipoCampo.Texto, IReadOnlyList<string>? opciones = null) =>
        CampoPropio.Crear(Empresa, Ambito.Contacto, nombre, tipo, opciones, 0, Reloj);

    [Fact]
    public void La_clave_sale_del_nombre_sin_acentos_ni_signos()
    {
        // La clave es lo que va en la cabecera del CSV y lo que usaría una integración, así que tiene que
        // ser predecible y no llevar nada raro.
        CampoPropio.ClaveDe("Nº de póliza").Should().Be("n_de_poliza");
        CampoPropio.ClaveDe("Potencia contratada (kW)").Should().Be("potencia_contratada_kw");
        CampoPropio.ClaveDe("  Año   de   instalación  ").Should().Be("ano_de_instalacion");
        CampoPropio.ClaveDe("¿Tiene mantenimiento?").Should().Be("tiene_mantenimiento");
    }

    [Fact]
    public void La_clave_no_cambia_al_renombrar_el_campo()
    {
        // **El invariante que importa de este módulo.** Si la clave cambiara al corregir una tilde, la
        // columna de un informe que alguien tiene montado desaparecería sin aviso. El nombre es para las
        // personas; la clave, para las máquinas, y las máquinas no perdonan.
        var campo = Crear("Numero de poliza").Valor;
        var clave = campo.Clave;

        campo.Renombrar("Nº de póliza").Exito.Should().BeTrue();

        campo.Nombre.Should().Be("Nº de póliza");
        campo.Clave.Should().Be(clave).And.Be("numero_de_poliza");
    }

    [Fact]
    public void Un_nombre_sin_letras_ni_numeros_se_rechaza()
    {
        // «???» pasa la validación del nombre y da una clave vacía. Sin esta comprobación quedaría una
        // fila con clave vacía que choca con la siguiente igual de rara.
        var r = Crear("¿¿¿");

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campo.nombre_sin_letras");
    }

    [Fact]
    public void El_nombre_es_obligatorio_y_tiene_techo()
    {
        Crear(null).Error!.Codigo.Should().Be("campo.sin_nombre");
        Crear("   ").Error!.Codigo.Should().Be("campo.sin_nombre");
        Crear(new string('a', CampoPropio.LongitudMaximaNombre + 1)).Error!.Codigo.Should().Be("campo.nombre_largo");
    }

    [Fact]
    public void No_hay_ambito_de_oportunidad_porque_no_hay_ficha_de_oportunidad()
    {
        // Un campo propio solo sirve si hay una pantalla donde se ve y se rellena. Añadir el ámbito sin
        // la pantalla habría dejado un campo que se puede definir y no se puede rellenar.
        Enum.GetNames<Ambito>().Should().BeEquivalentTo("Contacto", "Cuenta");
    }

    [Fact]
    public void El_tipo_no_se_puede_cambiar()
    {
        // Un campo de texto que pasa a número deja sin sentido todos los valores guardados, y convertirlos
        // sería adivinar. Así que el tipo se fija al crear y no hay por dónde tocarlo: ni un `set` público
        // ni un método que lo mueva. Renombrar sí, y esa es la vía para arreglar un campo mal puesto:
        // renombrarlo y crear el nuevo con el tipo bueno.
        typeof(CampoPropio).GetProperty(nameof(CampoPropio.Tipo))!.SetMethod!.IsPublic.Should().BeFalse();

        var campo = Crear("Potencia", TipoCampo.Numero).Valor;
        campo.Renombrar("Potencia contratada").Exito.Should().BeTrue();
        campo.Tipo.Should().Be(TipoCampo.Numero);
    }

    // ---- Listas ----

    [Fact]
    public void Una_lista_necesita_al_menos_dos_opciones()
    {
        Crear("Tipo", TipoCampo.Lista, []).Error!.Codigo.Should().Be("campo.pocas_opciones");
        Crear("Tipo", TipoCampo.Lista, ["Gas"]).Error!.Codigo.Should().Be("campo.pocas_opciones");
        Crear("Tipo", TipoCampo.Lista, ["Gas", "Eléctrica"]).Exito.Should().BeTrue();
    }

    [Fact]
    public void Una_lista_con_demasiadas_opciones_es_un_texto_libre_disfrazado()
    {
        var muchas = Enumerable.Range(0, CampoPropio.MaximoOpciones + 1).Select(i => "o" + i).ToList();

        Crear("Tipo", TipoCampo.Lista, muchas).Error!.Codigo.Should().Be("campo.demasiadas_opciones");
    }

    [Fact]
    public void Dos_opciones_que_solo_se_distinguen_por_la_tilde_se_rechazan()
    {
        // «Gas» y «gas» en el mismo desplegable son un error de quien lo escribió, y dejarlas pasar hace
        // que el dato no se pueda agrupar nunca.
        Crear("Tipo", TipoCampo.Lista, ["Gas", "gas"]).Error!.Codigo.Should().Be("campo.opcion_repetida");
        Crear("Tipo", TipoCampo.Lista, ["Eléctrica", "Electrica"]).Error!.Codigo.Should().Be("campo.opcion_repetida");
    }

    [Fact]
    public void Las_opciones_se_limpian_de_espacios_y_de_huecos()
    {
        var r = Crear("Tipo", TipoCampo.Lista, ["  Gas  ", "", "   ", "Eléctrica"]);

        r.Exito.Should().BeTrue();
        r.Valor.Opciones.Should().BeEquivalentTo(["Gas", "Eléctrica"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Un_campo_que_no_es_lista_no_lleva_opciones()
    {
        // Guardarlas dejaría un campo de texto con opciones que nadie usa y que confunden al leer la fila.
        Crear("Notas", TipoCampo.Texto, ["Gas", "Eléctrica"]).Error!.Codigo.Should().Be("campo.opciones_sin_lista");
        Crear("Notas", TipoCampo.Texto, []).Exito.Should().BeTrue();
    }

    [Fact]
    public void Solo_una_lista_puede_cambiar_sus_opciones()
    {
        var texto = Crear("Notas").Valor;

        texto.CambiarOpciones(["a", "b"]).Error!.Codigo.Should().Be("campo.no_es_lista");
    }
}
