using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Exceptions;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public sealed class CreditoComandaIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task MigracaoPreservaLinksAntigosComJsonValido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();
        var aplicadas = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var indice = aplicadas.FindIndex(m => m.EndsWith("ComandaClienteCompartilhada", StringComparison.Ordinal));
        indice.Should().BePositive();
        var empresa = Guid.NewGuid(); var agora = DateTime.UtcNow;
        db.SetMobileTenantContext(empresa);
        var conversa = Conversa.Abrir(empresa, "5511999994444", agora);
        db.AtendimentoConversas.Add(conversa); await db.SaveChangesAsync();
        await db.GetService<IMigrator>().MigrateAsync(aplicadas[indice - 1]);
        var id = Guid.NewGuid(); var hash = new string('a', 64);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO links_cardapio_conversa (\"Id\", \"EmpresaId\", \"ConversaId\", \"TokenHash\", \"CriadoEm\", \"ExpiraEm\") VALUES ({id}, {empresa}, {conversa.Id}, {hash}, {agora}, {agora.AddDays(1)})");
        await db.GetService<IMigrator>().MigrateAsync();
        db.ChangeTracker.Clear();
        var legado = await db.LinksCardapioConversa.SingleAsync(l => l.Id == id);
        legado.RascunhoJson.Should().Be("{}"); legado.Revisao.Should().Be(0); legado.UsadoEm.Should().BeNull();
        (await db.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id)).NumeroComanda.Should().BePositive();
    }

    [SkippableFact]
    public async Task UsoConcorrenteNaoGastaDuasVezesERlsImpedeOutraEmpresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid();
        var cliente = Cliente.Criar(empresa, "Cliente de crédito");
        var origem = Pedido.Criar(empresa); origem.ClienteId = cliente.Id;
        var destino = Pedido.Criar(empresa); destino.ClienteId = cliente.Id;
        var credito = CreditoClienteComanda.Criar(empresa, cliente.Id, origem.Id, Guid.NewGuid(), 10, DateTime.UtcNow);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa);
            seed.Empresas.Add(new Empresa { Id = empresa, Nome = "Empresa da carteira", Documento = empresa.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            seed.Clientes.Add(cliente); seed.Pedidos.AddRange(origem, destino); seed.Set<CreditoClienteComanda>().Add(credito);
            await seed.SaveChangesAsync();
        }
        async Task<bool> Usar()
        {
            await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
            await using var tx = await db.Database.BeginTransactionAsync();
            var repo = new CreditoClienteComandaRepository(db);
            var atual = await repo.ObterComLockAsync(empresa, cliente.Id, credito.Codigo, CancellationToken.None);
            try
            {
                var uso = atual!.Usar(destino.Id, 6, DateTime.UtcNow);
                await repo.AdicionarUsoAsync(uso, CancellationToken.None); await db.SaveChangesAsync(); await tx.CommitAsync();
                return true;
            }
            catch (RegraDeDominioVioladaException) { return false; }
        }
        (await Task.WhenAll(Usar(), Usar())).Count(r => r).Should().Be(1);
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            (await db.Set<CreditoClienteComanda>().SingleAsync()).Saldo.Should().Be(4);
            (await db.Set<UsoCreditoClienteComanda>().SingleAsync()).Valor.Should().Be(6);
        }
        var options = new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(fixture.RlsClientConnectionString).Options;
        await using var rls = new EasyStockDbContext(options);
        await rls.Database.OpenConnectionAsync();
        (await rls.Set<CreditoClienteComanda>().IgnoreQueryFilters().CountAsync()).Should().Be(0, "sem tenant o banco nega a carteira");
        await rls.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.ToString()}, false)");
        (await rls.Set<CreditoClienteComanda>().IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await rls.Set<UsoCreditoClienteComanda>().IgnoreQueryFilters().CountAsync()).Should().Be(1);
        await rls.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {Guid.NewGuid().ToString()}, false)");
        (await rls.Set<CreditoClienteComanda>().IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await rls.Set<UsoCreditoClienteComanda>().IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    // #1961: a comanda que não conseguiu cobrar desfaz o pedido e devolve o MESMO cupom, para o cliente reenviar.
    [SkippableFact]
    public async Task UsoDoPedidoDesfeitoVoltaAoMesmoCupom()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid();
        var cliente = Cliente.Criar(empresa, "Cliente do cupom devolvido");
        var origem = Pedido.Criar(empresa); origem.ClienteId = cliente.Id;
        var desfeito = Pedido.Criar(empresa); desfeito.ClienteId = cliente.Id;
        var credito = CreditoClienteComanda.Criar(empresa, cliente.Id, origem.Id, Guid.NewGuid(), 10, DateTime.UtcNow);
        var uso = credito.Usar(desfeito.Id, 6, DateTime.UtcNow);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa);
            seed.Empresas.Add(new Empresa { Id = empresa, Nome = "Empresa do cupom devolvido", Documento = empresa.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            seed.Clientes.Add(cliente); seed.Pedidos.AddRange(origem, desfeito); seed.Set<CreditoClienteComanda>().Add(credito);
            seed.Set<UsoCreditoClienteComanda>().Add(uso);
            await seed.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            await using var tx = await db.Database.BeginTransactionAsync();
            var repo = new CreditoClienteComandaRepository(db);
            foreach (var usado in await repo.ListarUsosDoPedidoAsync(empresa, desfeito.Id, CancellationToken.None))
            {
                var atual = await repo.ObterComLockPorIdAsync(empresa, usado.CreditoId, CancellationToken.None);
                atual!.Devolver(usado);
                repo.RemoverUso(usado);
            }
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }

        await using var relido = fixture.CreateDbContext();
        relido.SetMobileTenantContext(empresa);
        (await relido.Set<CreditoClienteComanda>().SingleAsync(c => c.Id == credito.Id)).Saldo.Should().Be(10);
        (await relido.Set<UsoCreditoClienteComanda>().CountAsync(u => u.CreditoId == credito.Id)).Should().Be(0);
    }
}
