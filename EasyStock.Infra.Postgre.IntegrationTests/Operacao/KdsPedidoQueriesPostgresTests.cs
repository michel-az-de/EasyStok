using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Operacao;

/// <summary>
/// S19 (#1155): a leitura do KDS traduz para SQL no Postgres real (EXISTS da vaga ativa, COALESCE do
/// agendamento, <c>DateOnly</c> da vaga) e escolhe o dia de produção pela vaga quando ela existe.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class KdsPedidoQueriesPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task ListaDoDiaPelaVagaOuCriacao_SoDaEmpresa_ComJanela()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        db.SetMobileTenantContext(empresaA);
        db.Empresas.Add(NovaEmpresa(empresaA));
        db.Empresas.Add(NovaEmpresa(empresaB));

        var storefront = EasyStock.Domain.Entities.Storefront.Storefront.Criar(empresaA, $"sf-kds-{Guid.NewGuid():N}", "SF KDS", 0m);
        db.Storefronts.Add(storefront);
        var janela = JanelaEntrega.Criar(storefront.Id, 1, new TimeOnly(11, 0), new TimeOnly(13, 0), 10, "Almoço");
        db.JanelasEntrega.Add(janela);

        var hoje = HorarioBrasil.Hoje();
        var balcaoHoje = NovoPedido(empresaA, StatusPedidoMapper.Aguardando);
        var vagaHoje = NovoPedido(empresaA, StatusPedidoMapper.Preparando);
        var vagaAmanha = NovoPedido(empresaA, StatusPedidoMapper.Aguardando);
        var entregue = NovoPedido(empresaA, StatusPedidoMapper.Entregue);
        var outraEmpresa = NovoPedido(empresaB, StatusPedidoMapper.Aguardando);
        db.Pedidos.AddRange(balcaoHoje, vagaHoje, vagaAmanha, entregue, outraEmpresa);
        db.VagasOcupadas.Add(VagaOcupada.Ocupar(janela.Id, hoje, vagaHoje.Id));
        db.VagasOcupadas.Add(VagaOcupada.Ocupar(janela.Id, hoje.AddDays(1), vagaAmanha.Id));
        await db.SaveChangesAsync();

        var lidos = await new KdsPedidoQueries(db).ListarAsync(
            empresaA,
            [StatusPedidoMapper.Aguardando, StatusPedidoMapper.Preparando],
            hoje.AddDays(-1),
            hoje);

        lidos.Select(p => p.Id).Should().BeEquivalentTo([balcaoHoje.Id, vagaHoje.Id]);
        var comVaga = lidos.Single(p => p.Id == vagaHoje.Id);
        comVaga.Janela.Should().Be(new KdsJanelaLeitura("Almoço", hoje, new TimeOnly(11, 0), new TimeOnly(13, 0)));
        comVaga.DataProducao.Should().Be(hoje);
        comVaga.Itens.Should().ContainSingle().Which.Linha.Should().Be("prepararEmCasa");
    }

    /// <summary>
    /// F08 item 5 (#1238): pedido fora de área para daqui a 3 dias espera a dona aprovar hoje. O corte por
    /// dia de produção vale para a cozinha, não para a fila de aprovação.
    /// </summary>
    [SkippableFact]
    public async Task AguardandoAprovacao_ApareceSemCorteDeData()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresa = Guid.NewGuid();
        db.SetMobileTenantContext(empresa);
        db.Empresas.Add(NovaEmpresa(empresa));
        var storefront = EasyStock.Domain.Entities.Storefront.Storefront.Criar(empresa, $"sf-kds-{Guid.NewGuid():N}", "SF KDS", 0m);
        db.Storefronts.Add(storefront);
        var janela = JanelaEntrega.Criar(storefront.Id, 1, new TimeOnly(11, 0), new TimeOnly(13, 0), 10, "Almoço");
        db.JanelasEntrega.Add(janela);

        var hoje = HorarioBrasil.Hoje();
        var paraAprovar = NovoPedido(empresa, StatusPedidoMapper.AguardandoAprovacaoBaba);
        var cozinhaDaquiA3Dias = NovoPedido(empresa, StatusPedidoMapper.Aguardando);
        db.Pedidos.AddRange(paraAprovar, cozinhaDaquiA3Dias);
        db.VagasOcupadas.Add(VagaOcupada.Ocupar(janela.Id, hoje.AddDays(3), paraAprovar.Id));
        db.VagasOcupadas.Add(VagaOcupada.Ocupar(janela.Id, hoje.AddDays(3), cozinhaDaquiA3Dias.Id));
        await db.SaveChangesAsync();

        var lidos = await new KdsPedidoQueries(db).ListarAsync(
            empresa,
            [StatusPedidoMapper.AguardandoAprovacaoBaba, StatusPedidoMapper.Aguardando],
            hoje.AddDays(-1),
            hoje);

        lidos.Select(p => p.Id).Should().Equal(paraAprovar.Id);
    }

    private static Empresa NovaEmpresa(Guid empresaId) => new()
    {
        Id = empresaId,
        Nome = "Empresa KDS",
        Documento = empresaId.ToString("N")[..14],
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };

    private static Pedido NovoPedido(Guid empresaId, string status)
    {
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = status;
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Nome = "Lasanha",
            Quantidade = 1,
            PrecoUnitario = 10m,
            Subtotal = 10m,
            LinhaSnapshot = "prepararEmCasa",
            CriadoEm = DateTime.UtcNow,
        });
        pedido.RecalcularTotal();
        return pedido;
    }
}
