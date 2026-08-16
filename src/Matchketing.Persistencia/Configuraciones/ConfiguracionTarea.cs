using Matchketing.Tareas.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionTarea : IEntityTypeConfiguration<Tarea>
{
    public void Configure(EntityTypeBuilder<Tarea> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("tarea", "tareas");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.EmpresaId).HasColumnName("empresa_id");
        b.Property(t => t.Titulo).HasColumnName("titulo").HasMaxLength(Tarea.LongitudMaximaTitulo).IsRequired();
        b.Property(t => t.ContactoId).HasColumnName("contacto_id");
        b.Property(t => t.OportunidadId).HasColumnName("oportunidad_id");
        b.Property(t => t.VenceEl).HasColumnName("vence_el");
        b.Property(t => t.ResponsableId).HasColumnName("responsable_id");
        b.Property(t => t.Origen).HasColumnName("origen").HasConversion<int>();
        b.Property(t => t.Estado).HasColumnName("estado").HasConversion<int>();
        b.Property(t => t.VecesAplazada).HasColumnName("veces_aplazada");
        b.Property(t => t.CerradaEn).HasColumnName("cerrada_en");
        b.Property(t => t.CreadoEn).HasColumnName("creado_en");
        b.Property(t => t.ActualizadoEn).HasColumnName("actualizado_en");

        // El índice que sostiene la pantalla Hoy: lo pendiente de una empresa, por fecha.
        b.HasIndex(t => new { t.EmpresaId, t.Estado, t.VenceEl }).HasDatabaseName("ix_tarea_empresa_estado_vence");
        b.HasIndex(t => new { t.EmpresaId, t.ContactoId }).HasDatabaseName("ix_tarea_empresa_contacto");
        b.Ignore(t => t.Eventos);
    }
}
