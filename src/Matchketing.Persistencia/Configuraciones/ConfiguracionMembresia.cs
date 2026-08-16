using Matchketing.Identidad.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionMembresia : IEntityTypeConfiguration<Membresia>
{
    public void Configure(EntityTypeBuilder<Membresia> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("membresia", "identidad");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).HasColumnName("id");
        b.Property(m => m.UsuarioId).HasColumnName("usuario_id");
        b.Property(m => m.EmpresaId).HasColumnName("empresa_id");
        b.Property(m => m.Rol).HasColumnName("rol").HasConversion<int>();
        b.Property(m => m.Activa).HasColumnName("activa");
        b.Property(m => m.Zonas).HasColumnName("zonas").HasMaxLength(400);
        b.Property(m => m.CreadoEn).HasColumnName("creado_en");
        b.HasIndex(m => new { m.UsuarioId, m.EmpresaId }).IsUnique().HasDatabaseName("ix_membresia_usuario_empresa");
        b.Ignore(m => m.Eventos);
        b.Ignore(m => m.Permisos);
        b.Ignore(m => m.ListaZonas);
    }
}
