using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2478: a tela Pedidos de Cadastros filtra "Na fila", que no console junta o pago comum (`aguardando`) e o
/// aprovado pela dona (`aprovado_baba`), como a Cozinha. A lista aceita situações separadas por vírgula, como o KDS.
/// </summary>
public sealed class PedidoListaPorSituacaoIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task ListaFiltraUmaOuVariasSituacoesSemMisturarEmpresas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Empresa.Criar("Situações #2478", "55555555000191");
        var outra = Empresa.Criar("Situações alheias #2478", "66666666000191");
        var cliente = Cliente.Criar(empresa.Id, "Cliente #2478");
        var alheio = Cliente.Criar(outra.Id, "Alheio #2478");
        var pago = ComSituacao(Pedido.Criar(empresa.Id, cliente), StatusPedidoMapper.Aguardando);
        var aprovado = ComSituacao(Pedido.Criar(empresa.Id, cliente), StatusPedidoMapper.AprovadoBaba);
        var pronto = ComSituacao(Pedido.Criar(empresa.Id, cliente), StatusPedidoMapper.Pronto);
        var alheioNaFila = ComSituacao(Pedido.Criar(outra.Id, alheio), StatusPedidoMapper.Aguardando);
        await using (var gravar = fixture.CreateDbContext())
        {
            using var bypass = gravar.UseRowLevelSecurityBypass();
            await gravar.Database.MigrateAsync();
            gravar.Empresas.AddRange(empresa, outra);
            gravar.Clientes.AddRange(cliente, alheio);
            gravar.Pedidos.AddRange(pago, aprovado, pronto, alheioNaFila);
            await gravar.SaveChangesAsync();
        }
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        var pedidos = new PedidoRepository(db);

        var umaSo = await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, status: "pronto");
        umaSo.items.Select(p => p.Id).Should().BeEquivalentTo(new[] { pronto.Id });
        umaSo.total.Should().Be(1);

        var naFila = await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, status: " aguardando , aprovado_baba ,");
        naFila.items.Select(p => p.Id).Should().BeEquivalentTo(new[] { pago.Id, aprovado.Id });
        naFila.total.Should().Be(2);

        var paginada = await pedidos.GetByEmpresaAsync(empresa.Id, 2, 1, status: "aguardando,aprovado_baba");
        paginada.items.Should().ContainSingle();
        paginada.total.Should().Be(2);

        (await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, status: " , ")).total.Should().Be(3, "vírgula sozinha não filtra");

        var comDesconhecida = await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, status: "aguardando,xyz");
        comDesconhecida.items.Select(p => p.Id).Should().BeEquivalentTo(new[] { pago.Id }, "situação desconhecida não amplia nem zera o filtro");
    }

    private static Pedido ComSituacao(Pedido pedido, string situacao)
    {
        pedido.Status = situacao;
        return pedido;
    }
}
