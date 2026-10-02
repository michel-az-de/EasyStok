using EasyStock.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
    {
        public void Configure(EntityTypeBuilder<Usuario> builder)
        {
            builder.ToTable("usuarios");
            builder.HasKey(u => u.Id);

            builder.Property(u => u.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            builder.Property(u => u.Nome)
                .IsRequired()
                .HasMaxLength(150)
                .HasColumnType("character varying(150)");

            builder.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(255)
                .HasColumnType("character varying(255)");

            builder.Property(u => u.AvatarUrl)
                .HasMaxLength(500)
                .HasColumnType("character varying(500)");

            builder.Property(u => u.TemaPreferido)
                .IsRequired()
                .HasMaxLength(20)
                .HasColumnType("character varying(20)")
                .HasDefaultValue("light");

            builder.Property(u => u.SenhaHash)
                .IsRequired()
                .HasMaxLength(500)
                .HasColumnType("character varying(500)");

            builder.Property(u => u.Ativo)
                .HasColumnType("boolean");

            builder.Property(u => u.EmailConfirmado)
                .IsRequired()
                .HasColumnType("boolean")
                .HasDefaultValue(false);

            builder.Property(u => u.UltimoAcessoEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(u => u.CriadoEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(u => u.AlteradoEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(u => u.FailedLoginAttempts)
                .HasColumnType("integer");

            builder.Property(u => u.LockoutEnd)
                .HasColumnType("timestamp with time zone");

            // #1352: corte das sessões. Nulo = nunca revogou, então a coluna nasce nula e sem default.
            // O SaveChanges nunca grava a coluna num UPDATE (Ignore): a linha inteira costuma ser regravada por
            // quem leu o usuário antes de uma revogação (login, troca de senha), e isso desfaria o corte ou o
            // faria recuar. Só o UPDATE atômico do UsuarioRepository (ExecuteUpdate, que ignora esta regra) grava.
            builder.Property(u => u.SessoesValidasDesde)
                .HasColumnType("timestamp with time zone")
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

            // N4: contato verificado. Três colunas nulas; a tabela não tem EmpresaId, então não entra RLS nova.
            builder.Property(u => u.Telefone)
                .HasConversion(t => t == null ? null : t.Value, v => v == null ? null : TelefoneE164.From(v))
                .HasMaxLength(TelefoneE164.TamanhoMaximo)
                .HasColumnType("character varying(16)");

            builder.Property(u => u.TelefoneVerificadoEm)
                .HasColumnType("timestamp with time zone");

            builder.Property(u => u.EmailPendente)
                .HasMaxLength(255)
                .HasColumnType("character varying(255)");

            builder.HasIndex(u => u.Email).IsUnique();
            builder.HasIndex(u => u.Ativo);
        }
    }
}
