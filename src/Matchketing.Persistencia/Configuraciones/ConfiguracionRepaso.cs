using Matchketing.Repaso.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionPospuesta : IEntityTypeConfiguration<Pospuesta>
{
    public void Configure(EntityTypeBuilder<Pospuesta> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("pospuesta", "repaso");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.EmpresaId).HasColumnName("empresa_id");
        b.Property(p => p.Clave).HasColumnName("clave").HasMaxLength(ClavePregunta.LongitudMaxima).IsRequired();
        b.Property(p => p.UsuarioId).HasColumnName("usuario_id");
        b.Property(p => p.Hasta).HasColumnName("hasta");
        b.Property(p => p.En).HasColumnName("en");

        // Se consulta siempre igual: las claves de esta empresa que todavía no han vencido.
        b.HasIndex(p => new { p.EmpresaId, p.Hasta }).HasDatabaseName("ix_pospuesta_empresa_hasta");

        // Es un histórico, no un estado: cada vez que alguien contesta se añade una fila, y por eso no
        // hay clave única sobre la clave de pregunta. La fila más reciente manda porque `Hasta` de las
        // viejas ya pasó, y a cambio queda el rastro de cuántas veces se ha aparcado lo mismo, que es
        // un dato interesante: si una oportunidad se ha aparcado cinco semanas seguidas, no sigue viva.
        b.HasIndex(p => new { p.EmpresaId, p.UsuarioId, p.En }).HasDatabaseName("ix_pospuesta_empresa_usuario_fecha");

        b.Ignore(p => p.Eventos);
    }
}
