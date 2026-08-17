using FluentAssertions;
using Matchketing.Auditoria.Dominio;
using Matchketing.Nucleo.Tiempo;
using Xunit;

namespace Matchketing.Auditoria.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc => ahora;
}

public sealed class PruebasRegistroAuditoria
{
    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static IReloj Reloj() => new RelojFijo(new DateTimeOffset(2026, 8, 17, 11, 30, 0, TimeSpan.Zero));

    [Fact]
    public void Un_apunte_guarda_quien_que_y_cuando()
    {
        var r = RegistroAuditoria.Crear(Empresa, Actor, "contacto", Actor, Acciones.ContactoFusionado, """{"actividadesMovidas":3}""", Reloj());

        r.EmpresaId.Should().Be(Empresa);
        r.ActorId.Should().Be(Actor);
        r.Entidad.Should().Be("contacto");
        r.Accion.Should().Be(Acciones.ContactoFusionado);
        r.En.Should().Be(Reloj().AhoraUtc);
    }

    [Fact]
    public void Sin_actor_significa_que_lo_hizo_el_sistema()
    {
        RegistroAuditoria.Crear(Empresa, null, "contacto", null, Acciones.RetencionAplicada, null, Reloj())
            .ActorId.Should().BeNull();
    }

    [Fact]
    public void El_detalle_se_recorta_para_no_crecer_sin_limite()
    {
        var largo = new string('x', RegistroAuditoria.LongitudMaximaDetalle + 500);

        RegistroAuditoria.Crear(Empresa, Actor, "contacto", null, Acciones.ContactoBorrado, largo, Reloj())
            .Detalle!.Length.Should().Be(RegistroAuditoria.LongitudMaximaDetalle);
    }

    [Fact]
    public void Una_accion_sin_nombre_es_un_error_de_programacion_no_un_fallo_esperado()
    {
        // Aquí sí se lanza: la acción es una constante del código, no un dato que meta nadie. Si
        // llega vacía, el fallo está en quien llama, y taparlo con un Resultado lo esconderia.
        var lanzar = () => RegistroAuditoria.Crear(Empresa, Actor, "contacto", null, "  ", null, Reloj());

        lanzar.Should().Throw<ArgumentException>();
    }
}

/// <summary>
/// La red de seguridad del detalle. Estas pruebas son la razón de que exista: la regla «nunca datos
/// personales en la auditoría» tiene que fallar en la máquina, no en una revisión de código.
/// </summary>
public sealed class PruebasDetalles
{
    [Fact]
    public void Un_correo_se_tapa()
    {
        Detalles.Tapar("""{"email":"ana.lopez@empresa.es"}""")
            .Should().Be($$"""{"email":"{{Detalles.CorreoTapado}}"}""");
    }

    [Theory]
    [InlineData("+34 600 11 22 33")]
    [InlineData("600112233")]
    [InlineData("(+34) 600-11-22-33")]
    public void Un_telefono_se_tapa(string telefono)
    {
        Detalles.Tapar($$"""{"telefono":"{{telefono}}"}""")
            .Should().Contain(Detalles.TelefonoTapado).And.NotContain("600");
    }

    [Fact]
    public void Los_identificadores_sobreviven_intactos()
    {
        // El caso que rompería la auditoría en silencio: un UUID lleva tramos de doce dígitos que un
        // detector de teléfonos ingenuo se llevaría por delante, y un apunte sin identificador no
        // sirve para nada.
        const string json = """{"absorbido":"11111111-2222-3333-4444-123456789012"}""";

        Detalles.Tapar(json).Should().Be(json);
    }

    [Fact]
    public void Las_cifras_normales_no_se_tocan()
    {
        const string json = """{"leads":42,"filas":1275,"importe":40296.50}""";

        Detalles.Tapar(json).Should().Be(json);
    }

    [Fact]
    public void Una_fecha_con_hora_no_es_un_telefono()
    {
        const string json = """{"en":"2026-08-17T11:30:00+00:00"}""";

        Detalles.Tapar(json).Should().Be(json);
    }

    [Fact]
    public void Sin_detalle_no_hay_nada_que_tapar()
    {
        Detalles.Tapar(null).Should().BeNull();
        Detalles.Tapar("   ").Should().BeNull();
    }
}

public sealed class PruebasAcciones
{
    [Fact]
    public void Todas_las_acciones_estan_en_la_lista()
    {
        // Si alguien añade una constante y se olvida de la lista, la lista deja de servir para lo que
        // sirve: saber de un vistazo qué se audita en este sistema.
        var constantes = typeof(Acciones)
            .GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Acciones.Todas.Should().BeEquivalentTo(constantes);
    }

    [Fact]
    public void Las_acciones_no_se_repiten()
    {
        Acciones.Todas.Should().OnlyHaveUniqueItems();
    }
}
