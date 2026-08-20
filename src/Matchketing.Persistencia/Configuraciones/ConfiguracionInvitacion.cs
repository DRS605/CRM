using Matchketing.Identidad.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionInvitacion : IEntityTypeConfiguration<Invitacion>
{
    public void Configure(EntityTypeBuilder<Invitacion> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("invitacion", "identidad");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(i => i.EmpresaId).HasColumnName("empresa_id");
        b.Property(i => i.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
        b.Property(i => i.Rol).HasColumnName("rol").HasConversion<int>();
        b.Property(i => i.InvitadoPor).HasColumnName("invitado_por");

        // 64 caracteres exactos: un SHA-256 en hexadecimal. Lo que se guarda es la huella, nunca el
        // token; ver el comentario de `Invitacion`.
        b.Property(i => i.HuellaToken).HasColumnName("huella_token").HasMaxLength(64).IsRequired();

        b.Property(i => i.CreadaEn).HasColumnName("creada_en");
        b.Property(i => i.CaducaEn).HasColumnName("caduca_en");
        b.Property(i => i.AceptadaEn).HasColumnName("aceptada_en");
        b.Property(i => i.RetiradaEn).HasColumnName("retirada_en");

        // Único en todo el sistema, no por empresa: es una llave, y dos iguales serían un fallo grave
        // aunque fueran de empresas distintas.
        b.HasIndex(i => i.HuellaToken).IsUnique().HasDatabaseName("ix_invitacion_huella");
        b.HasIndex(i => new { i.EmpresaId, i.CaducaEn }).HasDatabaseName("ix_invitacion_empresa");
        b.Ignore(i => i.Eventos);
    }
}
