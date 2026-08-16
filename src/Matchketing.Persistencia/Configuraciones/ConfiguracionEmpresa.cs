using Matchketing.Organizacion.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionEmpresa : IEntityTypeConfiguration<Empresa>
{
    public void Configure(EntityTypeBuilder<Empresa> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("empresa", "organizacion");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(Empresa.LongitudMaximaNombre).IsRequired();
        b.Property(e => e.Nif).HasColumnName("nif").HasMaxLength(20);
        b.Property(e => e.Provincia).HasColumnName("provincia").HasMaxLength(60);
        b.Property(e => e.PesoEncaje).HasColumnName("peso_encaje").HasPrecision(3, 2);
        b.Property(e => e.HorasRebote).HasColumnName("horas_rebote");
        b.Property(e => e.Activa).HasColumnName("activa");
        b.Property(e => e.CreadoEn).HasColumnName("creado_en");
        b.Property(e => e.ActualizadoEn).HasColumnName("actualizado_en");
        b.Ignore(e => e.Eventos);
    }
}
