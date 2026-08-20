using Matchketing.Avisos.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionSuscripcionAviso : IEntityTypeConfiguration<SuscripcionAviso>
{
    public void Configure(EntityTypeBuilder<SuscripcionAviso> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("suscripcion", "avisos");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.EmpresaId).HasColumnName("empresa_id");
        b.Property(s => s.UsuarioId).HasColumnName("usuario_id");
        b.Property(s => s.Endpoint).HasColumnName("endpoint").HasMaxLength(SuscripcionAviso.LongitudMaximaEndpoint).IsRequired();
        b.Property(s => s.ClavePublica).HasColumnName("clave_publica").HasMaxLength(200).IsRequired();
        b.Property(s => s.Secreto).HasColumnName("secreto").HasMaxLength(40).IsRequired();
        b.Property(s => s.CreadoEn).HasColumnName("creado_en");
        b.Property(s => s.UltimoAvisoEn).HasColumnName("ultimo_aviso_en");

        // El endpoint identifica el aparato, así que es único **en todo el sistema**: el mismo móvil no
        // puede estar dado de alta en dos empresas con dos filas, porque entonces recibiría dos avisos.
        b.HasIndex(s => s.Endpoint).IsUnique().HasDatabaseName("ix_suscripcion_endpoint");
        b.HasIndex(s => new { s.EmpresaId, s.UsuarioId }).HasDatabaseName("ix_suscripcion_empresa_usuario");

        b.Ignore(s => s.Eventos);
    }
}
