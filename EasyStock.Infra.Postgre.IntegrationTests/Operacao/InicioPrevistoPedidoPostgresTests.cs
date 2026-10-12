using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Operacao;

/// <summary>
/// S21 (#1165): as leituras do início previsto traduzem para SQL no Postgres real. A do prazo junta vaga
/// ativa, janela, itens do cardápio (frete sem cardápio fica fora) e a configuração da empresa; a do job
/// acha, cross-tenant, só os aguardando com o início vencido e sem aviso.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class InicioPrevistoPedidoPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task PrazoLeJanelaItensDoCardapioEConfiguracao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(NovaEmpresa(empresaId));
        var storefront = EasyStock.Domain.Entities.Storefront.Storefront.Criar(empresaId, $"sf-s21-{Guid.NewGuid():N}", "SF S21", 0m);
        db.Storefronts.Add(storefront);
        var janela = JanelaEntrega.Criar(storefront.Id, 3, new TimeOnly(12, 0), new TimeOnly(14, 0), 10, "Almoço");
        db.JanelasEntrega.Add(janela);
        var lasanha = CardapioItem.CriarAvulso(storefront.Id, "Lasanha", 30m);
        lasanha.DefinirPreparo(null, 45, null);
        var bolo = CardapioItem.CriarAvulso(storefront.Id, "Bolo", 20m);
        db.CardapioItens.AddRange(lasanha, bolo);
        var config = ConfiguracaoAtendimento.CriarPadrao(empresaId);
        config.RespiroMinutos = 25;
        config.TempoPreparoPadraoMinutos = 30;
        db.ConfiguracoesAtendimento.Add(config);

        var pedido = Pedido.Criar(empresaId);
        pedido.Itens.Add(Item(pedido, "Lasanha", lasanha.Id));
        pedido.Itens.Add(Item(pedido, "Bolo", bolo.Id));
        pedido.Itens.Add(Item(pedido, "Frete Centro", null));
        db.Pedidos.Add(pedido);
        var dia = new DateOnly(2026, 9, 30);
        db.VagasOcupadas.Add(VagaOcupada.Ocupar(janela.Id, dia, pedido.Id));
        await db.SaveChangesAsync();

        var leitura = await new PrazoPreparoPedidoQueries(db).ObterAsync(empresaId, pedido.Id);
        var deOutraEmpresa = await new PrazoPreparoPedidoQueries(db).ObterAsync(Guid.NewGuid(), pedido.Id);

        leitura.Should().NotBeNull();
        leitura!.DataJanela.Should().Be(dia);
        leitura.HoraInicioJanela.Should().Be(new TimeOnly(12, 0));
        leitura.TemposPreparoMinutos.Should().BeEquivalentTo(new int?[] { 45, null }, "frete não é item do cardápio");
        leitura.TempoPreparoPadraoMinutos.Should().Be(30);
        leitura.RespiroMinutos.Should().Be(25);
        deOutraEmpresa.Should().BeNull();
    }

    [SkippableFact]
    public async Task JobListaSoAguardandoVencidoSemAviso_EntreEmpresas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        db.SetMobileTenantContext(empresaA);
        db.Empresas.Add(NovaEmpresa(empresaA));
        db.Empresas.Add(NovaEmpresa(empresaB));

        var agora = DateTime.UtcNow;
        var vencidoA = NovoPedido(empresaA, StatusPedidoMapper.Aguardando, agora.AddMinutes(-10));
        var vencidoB = NovoPedido(empresaB, StatusPedidoMapper.Aguardando, agora.AddMinutes(-20));
        var noPrazo = NovoPedido(empresaA, StatusPedidoMapper.Aguardando, agora.AddMinutes(30));
        var preparando = NovoPedido(empresaA, StatusPedidoMapper.Preparando, agora.AddMinutes(-10));
        var avisado = NovoPedido(empresaA, StatusPedidoMapper.Aguardando, agora.AddMinutes(-10));
        avisado.MarcarAtrasoNotificado(agora).Should().BeTrue();
        var semInicio = NovoPedido(empresaA, StatusPedidoMapper.Aguardando, null);
        db.Pedidos.AddRange(vencidoA, vencidoB, noPrazo, preparando, avisado, semInicio);
        await db.SaveChangesAsync();

        var candidatos = await new PedidoStorefrontRepository(db).ListarAtrasoNaoNotificadoAsync(agora, 500);

        var meus = candidatos.Where(c => c.EmpresaId == empresaA || c.EmpresaId == empresaB).ToList();
        meus.Select(c => c.PedidoId).Should().Equal(vencidoB.Id, vencidoA.Id);
        meus.Single(c => c.PedidoId == vencidoB.Id).EmpresaId.Should().Be(empresaB);
    }

    private static Empresa NovaEmpresa(Guid empresaId) => new()
    {
        Id = empresaId,
        Nome = "Empresa S21",
        Documento = empresaId.ToString("N")[..14],
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };

    private static Pedido NovoPedido(Guid empresaId, string status, DateTime? inicioPrevisto)
    {
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = status;
        pedido.DefinirInicioPrevisto(inicioPrevisto);
        return pedido;
    }

    private static PedidoItem Item(Pedido pedido, string nome, Guid? cardapioItemId) => new()
    {
        Id = Guid.NewGuid(),
        PedidoId = pedido.Id,
        Nome = nome,
        Quantidade = 1,
        PrecoUnitario = 10m,
        Subtotal = 10m,
        CardapioItemId = cardapioItemId,
        CriadoEm = DateTime.UtcNow,
    };
}
