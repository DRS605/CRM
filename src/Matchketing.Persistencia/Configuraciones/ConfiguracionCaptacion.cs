using Matchketing.Captacion.Dominio;
using Matchketing.Cumplimiento.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionFormulario : IEntityTypeConfiguration<Formulario>
{
    public void Configure(EntityTypeBuilder<Formulario> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("formulario", "captacion");
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).HasColumnName("id");
        b.Property(f => f.EmpresaId).HasColumnName("empresa_id");
        b.Property(f => f.Nombre).HasColumnName("nombre").HasMaxLength(Formulario.LongitudMaximaNombre).IsRequired();
        b.Property(f => f.Clave).HasColumnName("clave").HasMaxLength(Formulario.LongitudClave).IsRequired();
        b.Property(f => f.TextoConsentimiento).HasColumnName("texto_consentimiento").HasMaxLength(Formulario.LongitudMaximaTexto).IsRequired();
        b.Property(f => f.PideTelefono).HasColumnName("pide_telefono");
        b.Property(f => f.PideEmpresa).HasColumnName("pide_empresa");
        b.Property(f => f.PideMensaje).HasColumnName("pide_mensaje");
        b.Property(f => f.PaginaGracias).HasColumnName("pagina_gracias").HasMaxLength(500);
        b.Property(f => f.Origen).HasColumnName("origen").HasMaxLength(60).IsRequired();
        b.Property(f => f.Activo).HasColumnName("activo");
        b.Property(f => f.CreadoEn).HasColumnName("creado_en");
        b.HasIndex(f => f.Clave).IsUnique().HasDatabaseName("ix_formulario_clave");
        b.Ignore(f => f.Eventos);
    }
}

public sealed class ConfiguracionEnvioFormulario : IEntityTypeConfiguration<EnvioFormulario>
{
    public void Configure(EntityTypeBuilder<EnvioFormulario> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("envio_formulario", "captacion");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.EmpresaId).HasColumnName("empresa_id");
        b.Property(e => e.FormularioId).HasColumnName("formulario_id");
        b.Property(e => e.Datos).HasColumnName("datos").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Ip).HasColumnName("ip").HasMaxLength(60);
        b.Property(e => e.Agente).HasColumnName("agente").HasMaxLength(400);
        b.Property(e => e.ContactoId).HasColumnName("contacto_id");
        b.Property(e => e.RecibidoEn).HasColumnName("recibido_en");
        b.HasIndex(e => new { e.EmpresaId, e.FormularioId, e.RecibidoEn }).HasDatabaseName("ix_envio_empresa_formulario_fecha");
        b.Ignore(e => e.Eventos);
    }
}

public sealed class ConfiguracionConsentimiento : IEntityTypeConfiguration<Consentimiento>
{
    public void Configure(EntityTypeBuilder<Consentimiento> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("consentimiento", "cumplimiento");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.EmpresaId).HasColumnName("empresa_id");
        b.Property(c => c.ContactoId).HasColumnName("contacto_id");
        b.Property(c => c.Finalidad).HasColumnName("finalidad").HasConversion<int>();
        b.Property(c => c.Base).HasColumnName("base_legal").HasConversion<int>();
        b.Property(c => c.Canal).HasColumnName("canal").HasMaxLength(60).IsRequired();
        b.Property(c => c.TextoAceptado).HasColumnName("texto_aceptado").HasMaxLength(1000);
        b.Property(c => c.Ip).HasColumnName("ip").HasMaxLength(60);
        b.Property(c => c.Agente).HasColumnName("agente").HasMaxLength(400);
        b.Property(c => c.OtorgadoEn).HasColumnName("otorgado_en");
        b.Property(c => c.RetiradoEn).HasColumnName("retirado_en");
        b.HasIndex(c => new { c.EmpresaId, c.ContactoId, c.Finalidad }).HasDatabaseName("ix_consentimiento_empresa_contacto");
        b.Ignore(c => c.Eventos);
        b.Ignore(c => c.Vigente);
    }
}
