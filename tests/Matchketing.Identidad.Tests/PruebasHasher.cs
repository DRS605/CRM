using FluentAssertions;
using Matchketing.Persistencia.Seguridad;
using Xunit;

namespace Matchketing.Identidad.Tests;

public sealed class PruebasHasher
{
    private readonly HasherContrasena hasher = new();

    [Fact]
    public void Una_contrasena_correcta_se_verifica()
    {
        var hash = hasher.Hashear("Levante2026");

        hasher.Verificar("Levante2026", hash).Should().BeTrue();
    }

    [Fact]
    public void Una_contrasena_incorrecta_no_se_verifica()
    {
        var hash = hasher.Hashear("Levante2026");

        hasher.Verificar("Levante2027", hash).Should().BeFalse();
    }

    [Fact]
    public void Dos_hashes_de_la_misma_contrasena_son_distintos_por_la_sal()
    {
        hasher.Hashear("Levante2026").Should().NotBe(hasher.Hashear("Levante2026"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("basura")]
    [InlineData("pbkdf2-sha256$no-es-un-numero$AAAA$BBBB")]
    public void Un_hash_con_formato_invalido_no_revienta_devuelve_falso(string hash)
    {
        hasher.Verificar("Levante2026", hash).Should().BeFalse();
    }
}
