using FluentAssertions;
using Matchketing.Identidad.Dominio;
using Xunit;

namespace Matchketing.Identidad.Tests;

public sealed class PruebasPermisos
{
    private static readonly RelojFijo Reloj = new(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void El_propietario_tiene_todos_los_permisos()
    {
        PermisosDeRol.De(Rol.Propietario).Should().BeEquivalentTo(Permisos.Todos);
    }

    [Fact]
    public void El_comercial_opera_pero_no_toca_ajustes_ni_usuarios()
    {
        var permisos = PermisosDeRol.De(Rol.Comercial);

        permisos.Should().Contain(Permisos.OportunidadGestionar);
        permisos.Should().NotContain(Permisos.EmpresaAjustes);
        permisos.Should().NotContain(Permisos.UsuarioGestionar);
    }

    [Fact]
    public void Solo_lectura_no_puede_modificar_nada()
    {
        var permisos = PermisosDeRol.De(Rol.SoloLectura);

        permisos.Should().OnlyContain(p => p.EndsWith(".leer", StringComparison.Ordinal) || p == Permisos.DatosExportar);
    }

    [Fact]
    public void Una_membresia_desactivada_se_queda_sin_permisos()
    {
        var membresia = Membresia.Crear(Guid.NewGuid(), Guid.NewGuid(), Rol.Propietario, Reloj);
        membresia.Permisos.Should().NotBeEmpty();

        membresia.Desactivar();

        membresia.Permisos.Should().BeEmpty();
    }

    [Fact]
    public void No_se_puede_cambiar_el_rol_de_una_membresia_inactiva()
    {
        var membresia = Membresia.Crear(Guid.NewGuid(), Guid.NewGuid(), Rol.Comercial, Reloj);
        membresia.Desactivar();

        var r = membresia.CambiarRol(Rol.Propietario);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("membresia.inactiva");
    }
}
