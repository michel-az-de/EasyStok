using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// EF mapping do key ring do ASP.NET DataProtection (<c>data_protection_keys</c>).
///
/// <para>
/// Tabela GLOBAL, deliberadamente sem <c>EmpresaId</c>: as chaves nao pertencem a um
/// tenant, elas protegem segredos de todos. Sem essa coluna a tabela fica fora do
/// query filter de tenant e fora do RLS (a migration <c>AddRowLevelSecurity</c> so
/// alcanca tabelas que tenham <c>EmpresaId</c>) — que e exatamente o que se quer,
/// porque o DataProtection le o key ring fora de qualquer escopo de request.
/// </para>
/// </summary>
public class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> b)
    {
        b.ToTable("data_protection_keys");
        b.HasKey(k => k.Id);

        b.Property(k => k.Id).HasColumnName("id");
        b.Property(k => k.FriendlyName).HasColumnName("friendly_name");
        b.Property(k => k.Xml).HasColumnName("xml");
    }
}
