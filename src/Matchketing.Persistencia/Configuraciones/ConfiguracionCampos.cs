using Matchketing.Campos.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionCampoPropio : IEntityTypeConfiguration<CampoPropio>
{
    public void Configure(EntityTypeBuilder<CampoPropio> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("campo", "campos");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.EmpresaId).HasColumnName("empresa_id");
        b.Property(c => c.Ambito).HasColumnName("ambito").HasConversion<int>();
        b.Property(c => c.Nombre).HasColumnName("nombre")
            .HasMaxLength(CampoPropio.LongitudMaximaNombre).IsRequired();
        b.Property(c => c.Clave).HasColumnName("clave")
            .HasMaxLength(CampoPropio.LongitudMaximaNombre).IsRequired();
        b.Property(c => c.Tipo).HasColumnName("tipo").HasConversion<int>();
        b.Property(c => c.Orden).HasColumnName("orden");
        b.Property(c => c.CreadoEn).HasColumnName("creado_en");

        // Las opciones van en un **`text[]`** y no en una columna de texto con separador. Los otros
        // módulos que guardan listas usan comas porque lo que guardan son nombres de un catálogo cerrado
        // —«oportunidad.ganada»— y ahí la coma no puede aparecer. Aquí las escribe la empresa: «Gas, de
        // ciudad» es una opción perfectamente razonable, y con separador se habría partido en dos el día
        // que alguien la teclee. Un array no tiene ese problema y Npgsql lo mapea sin ayuda.
        b.Property(c => c.Opciones)
            .HasColumnName("opciones")
            .HasColumnType("text[]")
            .IsRequired()
            .HasConversion(
                new ValueConverter<IReadOnlyList<string>, string[]>(
                    v => v.ToArray(),
                    v => v.ToList()),

                // Sin comparador, EF compara la lista por referencia y cambiar las opciones de una lista
                // no se detectaría: el `UPDATE` no se emitiría nunca. Es el mismo fallo silencioso que ya
                // se documentó en los webhooks, y se arregla igual.
                new ValueComparer<IReadOnlyList<string>>(
                    (a, x) => a != null && x != null && a.SequenceEqual(x, StringComparer.Ordinal),
                    v => v.Aggregate(0, (acumulado, o) => HashCode.Combine(acumulado, o.GetHashCode(StringComparison.Ordinal))),
                    v => v.ToList()));

        // **Una clave, una vez, por ámbito.** Es una regla y no una optimización: dos campos con la misma
        // clave dan dos columnas iguales en la exportación y dos filas casi iguales en la ficha. El
        // servicio ya lo comprueba; esto evita el otro camino, dos peticiones a la vez.
        b.HasIndex(c => new { c.EmpresaId, c.Ambito, c.Clave })
            .IsUnique().HasDatabaseName("ix_campo_clave_unica");

        // El de pintar la ficha y la pantalla de ajustes: los de un ámbito, en su orden.
        b.HasIndex(c => new { c.EmpresaId, c.Ambito, c.Orden }).HasDatabaseName("ix_campo_ambito_orden");

        b.Ignore(c => c.Eventos);
    }
}

public sealed class ConfiguracionValorCampo : IEntityTypeConfiguration<ValorCampo>
{
    public void Configure(EntityTypeBuilder<ValorCampo> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("valor", "campos");
        b.HasKey(v => v.Id);
        b.Property(v => v.Id).HasColumnName("id");
        b.Property(v => v.EmpresaId).HasColumnName("empresa_id");
        b.Property(v => v.CampoId).HasColumnName("campo_id");
        b.Property(v => v.Ambito).HasColumnName("ambito").HasConversion<int>();
        b.Property(v => v.EntidadId).HasColumnName("entidad_id");
        b.Property(v => v.Texto).HasColumnName("texto")
            .HasMaxLength(ValorCampo.LongitudMaximaTexto).IsRequired();
        b.Property(v => v.ActualizadoEn).HasColumnName("actualizado_en");

        // **Un valor por campo y ficha.** Dos filas del mismo campo para el mismo contacto se pintarían
        // las dos en la ficha, una debajo de la otra, y no habría forma de saber cuál es la buena.
        b.HasIndex(v => new { v.CampoId, v.EntidadId }).IsUnique().HasDatabaseName("ix_valor_unico");

        // Éste es el de la supresión del artículo 17 tanto como el de pintar la ficha: borrar los valores
        // de un contacto es una búsqueda por `entidad_id`, y sin índice sería un recorrido de la tabla
        // entera justo en la operación que tiene que terminar.
        b.HasIndex(v => new { v.EmpresaId, v.EntidadId }).HasDatabaseName("ix_valor_entidad");

        b.Ignore(v => v.Eventos);
    }
}
