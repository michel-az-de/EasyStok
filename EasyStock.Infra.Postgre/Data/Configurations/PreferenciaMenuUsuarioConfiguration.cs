using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// Mapeia <see cref="PreferenciaMenuUsuario"/> (favoritos do menu, ADR-0032 fatia 4).
/// FavoritosMenu vai como jsonb com value converter E <see cref="ValueComparer{T}"/> —
/// sem o comparer, mutacao in-place de List nao marca a entidade como modificada e o
/// update some silenciosamente (bug latente da FaturaConfiguration, sinalizado a parte).
/// </summary>
public class PreferenciaMenuUsuarioConfiguration : IEntityTypeConfiguration<PreferenciaMenuUsuario>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Configure(EntityTypeBuilder<PreferenciaMenuUsuario> b)
    {
        b.ToTable("preferencias_menu_usuario");
        b.HasKey(x => x.Id);

        b.Property(x => x.UsuarioId).IsRequired();
        b.Property(x => x.LojaId).IsRequired();
        b.Property(x => x.EmpresaId).IsRequired();
        b.Property(x => x.AtualizadaEm).IsRequired();

        b.Property(x => x.FavoritosMenu)
            .HasColumnName("favoritos_menu")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, c) => (a ?? new List<string>()).SequenceEqual(c ?? new List<string>()),
                v => v.Aggregate(17, (acc, s) => HashCode.Combine(acc, s == null ? 0 : s.GetHashCode())),
                v => v.ToList()));

        // Um conjunto de favoritos por usuario+loja; indice tenant-aware para queries.
        b.HasIndex(x => new { x.UsuarioId, x.LojaId }).IsUnique();
        b.HasIndex(x => new { x.EmpresaId, x.LojaId });

        b.HasOne(x => x.Usuario)
            .WithMany()
            .HasForeignKey(x => x.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        b.HasOne(x => x.Loja)
            .WithMany()
            .HasForeignKey(x => x.LojaId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // Empresa = tenant. Restrict (nao cascade) p/ evitar multiplos caminhos de cascade
        // ate a mesma linha (usuario/loja ja cascateiam); EmpresaId continua obrigatorio.
        b.HasOne(x => x.Empresa)
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
