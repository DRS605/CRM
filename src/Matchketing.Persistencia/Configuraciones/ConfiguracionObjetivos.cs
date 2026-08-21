using Matchketing.Objetivos.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionObjetivo : IEntityTypeConfiguration<Objetivo>
{
    public void Configure(EntityTypeBuilder<Objetivo> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("objetivo", "objetivos");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).HasColumnName("id");
        b.Property(o => o.EmpresaId).HasColumnName("empresa_id");
        b.Property(o => o.UsuarioId).HasColumnName("usuario_id");
        b.Property(o => o.Mes).HasColumnName("mes");
        b.Property(o => o.Importe).HasColumnName("importe").HasPrecision(14, 2);
        b.Property(o => o.FijadoEn).HasColumnName("fijado_en");

        // **Uno por persona y mes**, y es una regla, no una optimización. Dos objetivos del mismo mes
        // para la misma persona no son un dato ambiguo: son un porcentaje que sale distinto según qué
        // fila lea cada pantalla. El dominio normaliza el mes al día 1 para que este índice sirva de algo.
        b.HasIndex(o => new { o.EmpresaId, o.UsuarioId, o.Mes }).IsUnique().HasDatabaseName("ix_objetivo_unico");

        // El de la tabla del equipo: todos los de un mes, que es la consulta de la pantalla.
        b.HasIndex(o => new { o.EmpresaId, o.Mes }).HasDatabaseName("ix_objetivo_mes");

        b.Ignore(o => o.Eventos);
    }
}
