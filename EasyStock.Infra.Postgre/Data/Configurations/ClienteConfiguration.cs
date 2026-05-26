using EasyStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations
{
    public class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
    {
        public void Configure(EntityTypeBuilder<Cliente> builder)
        {
            builder.ToTable("clientes");
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Nome).IsRequired().HasMaxLength(150);
            builder.Property(c => c.Apt).HasMaxLength(32);
            builder.Property(c => c.Endereco).HasMaxLength(255);
            builder.Property(c => c.Telefone).HasMaxLength(32);
            builder.Property(c => c.Email).HasMaxLength(255);
            builder.Property(c => c.Documento).HasMaxLength(30);
            builder.Property(c => c.Observacoes).HasColumnType("text");

            // ── AUTH-002: identificação por telefone hash (SHA-256, 64 chars hex) ──
            builder.Property(c => c.TelefoneHash).HasMaxLength(64);
            // Lookup principal storefront OTP: empresaId + hash → cliente.
            builder.HasIndex(c => new { c.EmpresaId, c.TelefoneHash })
                .HasDatabaseName("ix_clientes_empresa_telefone_hash")
                .IsUnique()
                .HasFilter("\"TelefoneHash\" IS NOT NULL");

            // ── Aditivos storefront (TASK-EZ-005) ──────────────────────
            builder.Property(c => c.Cep).HasMaxLength(8);
            builder.Property(c => c.Complemento).HasMaxLength(100);
            builder.Property(c => c.Bairro).HasMaxLength(100);
            builder.Property(c => c.Cidade).HasMaxLength(100);
            builder.Property(c => c.Cpf).HasMaxLength(11);
            builder.Property(c => c.ConsentiuMarketing).HasDefaultValue(false);

            builder.HasOne(c => c.Empresa)
                .WithMany()
                .HasForeignKey(c => c.EmpresaId)
                .OnDelete(DeleteBehavior.Cascade);

            // Suporta GetNovosClientesPorMesAsync (filtra EmpresaId + CriadoEm).
            builder.HasIndex(c => new { c.EmpresaId, c.CriadoEm });

            builder.HasMany(c => c.Enderecos)
                .WithOne(e => e.Cliente)
                .HasForeignKey(e => e.ClienteId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(c => c.Telefones)
                .WithOne(t => t.Cliente)
                .HasForeignKey(t => t.ClienteId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(c => c.Documentos)
                .WithOne(d => d.Cliente)
                .HasForeignKey(d => d.ClienteId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(c => c.Alteracoes)
                .WithOne(a => a.Cliente)
                .HasForeignKey(a => a.ClienteId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class ClienteEnderecoConfiguration : IEntityTypeConfiguration<ClienteEndereco>
    {
        public void Configure(EntityTypeBuilder<ClienteEndereco> b)
        {
            b.ToTable("cliente_enderecos");
            b.HasKey(x => x.Id);
            b.Property(x => x.Tipo).HasMaxLength(32);
            b.Property(x => x.Logradouro).HasMaxLength(255);
            b.Property(x => x.Numero).HasMaxLength(32);
            b.Property(x => x.Complemento).HasMaxLength(120);
            b.Property(x => x.Bairro).HasMaxLength(120);
            b.Property(x => x.Cidade).HasMaxLength(120);
            b.Property(x => x.Estado).HasMaxLength(60);
            b.Property(x => x.Cep).HasMaxLength(20);
            b.Property(x => x.Pais).HasMaxLength(60);
            b.Property(x => x.Referencia).HasMaxLength(255);
        }
    }

    public class ClienteTelefoneConfiguration : IEntityTypeConfiguration<ClienteTelefone>
    {
        public void Configure(EntityTypeBuilder<ClienteTelefone> b)
        {
            b.ToTable("cliente_telefones");
            b.HasKey(x => x.Id);
            b.Property(x => x.Tipo).HasMaxLength(32);
            b.Property(x => x.Numero).IsRequired().HasMaxLength(32);
            b.Property(x => x.Observacao).HasMaxLength(255);
        }
    }

    public class ClienteDocumentoConfiguration : IEntityTypeConfiguration<ClienteDocumento>
    {
        public void Configure(EntityTypeBuilder<ClienteDocumento> b)
        {
            b.ToTable("cliente_documentos");
            b.HasKey(x => x.Id);
            b.Property(x => x.Tipo).IsRequired().HasMaxLength(32);
            b.Property(x => x.Valor).IsRequired().HasMaxLength(60);
            b.Property(x => x.Emissor).HasMaxLength(120);
        }
    }

    public class ClienteAlteracaoConfiguration : IEntityTypeConfiguration<ClienteAlteracao>
    {
        public void Configure(EntityTypeBuilder<ClienteAlteracao> b)
        {
            b.ToTable("cliente_alteracoes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Campo).IsRequired().HasMaxLength(60);
            b.Property(x => x.ValorAntigo).HasColumnType("text");
            b.Property(x => x.ValorNovo).HasColumnType("text");
            b.Property(x => x.AlteradoPorNome).HasMaxLength(120);
            b.Property(x => x.Origem).HasMaxLength(20);
            // F10-A: index composto inclui EmpresaId pra queries por tenant.
            // ApplyTenantQueryFilters do DbContext agora aplica filter automático
            // porque a entity tem EmpresaId.
            b.HasIndex(x => new { x.EmpresaId, x.ClienteId, x.AlteradoEm });
        }
    }
}
