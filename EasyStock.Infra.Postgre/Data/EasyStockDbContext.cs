using Microsoft.EntityFrameworkCore;
using EasyStok.Domain.Entities;
using System.Reflection;

namespace EasyStock.Infra.Postgre.Data
{
 public class EasyStockDbContext : DbContext
 {
 public EasyStockDbContext(DbContextOptions<EasyStockDbContext> options)
 : base(options)
 {
 }

 // Domain DbSets
 public DbSet<Empresa> Empresas { get; set; } = null!;
 public DbSet<Categoria> Categorias { get; set; } = null!;
 public DbSet<Produto> Produtos { get; set; } = null!;
 public DbSet<ItemEstoque> ItensEstoque { get; set; } = null!;
 public DbSet<Venda> Vendas { get; set; } = null!;
 public DbSet<ItemVenda> ItensVenda { get; set; } = null!;
 public DbSet<MovimentacaoEstoque> MovimentacoesEstoque { get; set; } = null!;

 protected override void OnModelCreating(ModelBuilder modelBuilder)
 {
 base.OnModelCreating(modelBuilder);

 // Apply all IEntityTypeConfiguration implementations in this assembly
 modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
 }
 }
}
