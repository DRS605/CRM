using Matchketing.Auditoria.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionRegistroAuditoria : IEntityTypeConfiguration<RegistroAuditoria>
{
    public void Configure(EntityTypeBuilder<RegistroAuditoria> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("registro", "auditoria");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.EmpresaId).HasColumnName("empresa_id");
        b.Property(r => r.ActorId).HasColumnName("actor_id");
        b.Property(r => r.Entidad).HasColumnName("entidad").HasMaxLength(40).IsRequired();
        b.Property(r => r.EntidadId).HasColumnName("entidad_id");
        b.Property(r => r.Accion).HasColumnName("accion").HasMaxLength(60).IsRequired();
        b.Property(r => r.Detalle).HasColumnName("detalle").HasMaxLength(RegistroAuditoria.LongitudMaximaDetalle);
        b.Property(r => r.En).HasColumnName("en");

        // Se lee siempre igual: lo último de esta empresa, primero.
        b.HasIndex(r => new { r.EmpresaId, r.En }).HasDatabaseName("ix_auditoria_empresa_fecha");
        b.HasIndex(r => new { r.EmpresaId, r.EntidadId }).HasDatabaseName("ix_auditoria_empresa_entidad");
    }
}
