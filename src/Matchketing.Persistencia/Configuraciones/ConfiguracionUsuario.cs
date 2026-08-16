using Matchketing.Identidad.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionUsuario : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("usuario", "identidad");
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).HasColumnName("id");
        b.Property(u => u.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
        b.Property(u => u.HashContrasena).HasColumnName("hash_contrasena").HasMaxLength(400).IsRequired();
        b.Property(u => u.Nombre).HasColumnName("nombre").HasMaxLength(Usuario.LongitudMaximaNombre).IsRequired();
        b.Property(u => u.EmailVerificado).HasColumnName("email_verificado");
        b.Property(u => u.Activo).HasColumnName("activo");
        b.Property(u => u.CreadoEn).HasColumnName("creado_en");
        b.Property(u => u.ActualizadoEn).HasColumnName("actualizado_en");
        b.Property(u => u.UltimoAccesoEn).HasColumnName("ultimo_acceso_en");
        b.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ix_usuario_email");
        b.Ignore(u => u.Eventos);
    }
}
