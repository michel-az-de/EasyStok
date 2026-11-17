using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>QA v1.10 BUG-05: intervalo de periodo invertido (de > ate) deve ser barrado.</summary>
public class MovimentacaoControllerTests
{
    private readonly IMovimentacaoEstoqueRepository _movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly IItemEstoqueRepository _itemRepo = Substitute.For<IItemEstoqueRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly MovimentacaoController _controller;

    public MovimentacaoControllerTests()
    {
        _controller = new MovimentacaoController(_movRepo, _itemRepo, _currentUser);
    }

    [Fact]
    public async Task GetAll_intervalo_invertido_retorna_BadRequest_sem_consultar_repo()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);

        var result = await _controller.GetAll(
            empresaId,
            de: new DateTime(2026, 12, 31),
            ate: new DateTime(2026, 1, 1),
            tipo: null,
            natureza: null);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _movRepo.DidNotReceiveWithAnyArgs()
            .GetByEmpresaAsync(Guid.Empty, null, null, null, null, 0, 0);
    }

    // #2487: data pura é o dia de Brasília; o "até" inclui o dia inteiro em vez de parar em 00:00 UTC.
    [Fact]
    public async Task GetAll_data_pura_inclui_o_ultimo_dia_inteiro_de_Brasilia()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);
        _movRepo.GetByEmpresaAsync(default, default, default, default, default, default, default)
            .ReturnsForAnyArgs((Enumerable.Empty<EasyStock.Domain.Entities.MovimentacaoEstoque>(), 0));

        await _controller.GetAll(empresaId, de: new DateTime(2026, 11, 1), ate: new DateTime(2026, 11, 13),
            tipo: null, natureza: null);

        await _movRepo.Received(1).GetByEmpresaAsync(empresaId,
            new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc).AddTicks(-10),
            null, null, 1, 20);
    }

    // #2738: os cards de KPI usam o mesmo periodo da lista (dia civil de Brasilia, ultimo dia inclusivo).
    [Fact]
    public async Task GetKpis_data_pura_usa_o_mesmo_periodo_da_lista()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);
        _movRepo.GetKpisAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(new KpisMovimentacao(0m, 0m, 0, 0));

        await _controller.GetKpis(empresaId, de: new DateTime(2026, 11, 1), ate: new DateTime(2026, 11, 15),
            tipo: null, natureza: null);

        await _movRepo.Received(1).GetKpisAsync(empresaId,
            new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 16, 3, 0, 0, DateTimeKind.Utc).AddTicks(-10),
            null, null);
    }

    [Fact]
    public async Task GetKpis_intervalo_invertido_retorna_BadRequest_sem_consultar_repo()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);

        var result = await _controller.GetKpis(empresaId, de: new DateTime(2026, 12, 31),
            ate: new DateTime(2026, 1, 1), tipo: null, natureza: null);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _movRepo.DidNotReceiveWithAnyArgs().GetKpisAsync(Guid.Empty);
    }

    // #2744: quem não tem a permissão de relatórios segue vendo as movimentações, mas sem dinheiro.
    [Fact]
    public async Task GetKpis_sem_permissao_de_relatorios_omite_a_receita()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);
        _movRepo.GetKpisAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(new KpisMovimentacao(7m, 350m, 3, 1));

        var data = DataJson(await _controller.GetKpis(empresaId, null, null, null, null));

        data.GetProperty("receitaTotal").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        data.GetProperty("totalUnidades").GetDecimal().Should().Be(7m);
        data.GetProperty("totalVendas").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task GetKpis_com_permissao_de_relatorios_traz_a_receita()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);
        _currentUser.TemPermissao(EasyStock.Domain.Enums.Permissao.VisualizarRelatorios).Returns(true);
        _movRepo.GetKpisAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(new KpisMovimentacao(7m, 350m, 3, 1));

        var data = DataJson(await _controller.GetKpis(empresaId, null, null, null, null));

        data.GetProperty("receitaTotal").GetDecimal().Should().Be(350m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAll_so_mostra_os_valores_da_saida_a_quem_ve_relatorios(bool podeVerRelatorios)
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);
        _currentUser.TemPermissao(EasyStock.Domain.Enums.Permissao.VisualizarRelatorios).Returns(podeVerRelatorios);
        _movRepo.GetByEmpresaAsync(default, default, default, default, default, default, default)
            .ReturnsForAnyArgs((new[] { SaidaDeVenda(empresaId) }.AsEnumerable(), 1));

        var linha = DataJson(await _controller.GetAll(empresaId, null, null, null, null))[0];

        linha.GetProperty("quantidade").GetDecimal().Should().Be(3m);
        if (podeVerRelatorios)
        {
            linha.GetProperty("valorUnitario").GetDecimal().Should().Be(50m);
            linha.GetProperty("valorTotal").GetDecimal().Should().Be(150m);
        }
        else
        {
            linha.GetProperty("valorUnitario").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
            linha.GetProperty("valorTotal").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        }
    }

    // #2821 (revisão): o estorno de uma venda entra no estoque como Entrada, mas carrega o preço da venda.
    [Fact]
    public async Task GetAll_sem_permissao_de_relatorios_omite_o_valor_do_estorno_de_venda()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);
        var estorno = EasyStock.Domain.Entities.MovimentacaoEstoque.CriarEstorno(
            Guid.NewGuid(), SaidaDeVenda(empresaId), DateTime.UtcNow, null, DateTime.UtcNow);
        _movRepo.GetByEmpresaAsync(default, default, default, default, default, default, default)
            .ReturnsForAnyArgs((new[] { estorno }.AsEnumerable(), 1));

        var linha = DataJson(await _controller.GetAll(empresaId, null, null, null, null))[0];

        linha.GetProperty("tipo").GetString().Should().Be("Entrada");
        linha.GetProperty("valorUnitario").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        linha.GetProperty("valorTotal").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    private static EasyStock.Domain.Entities.MovimentacaoEstoque SaidaDeVenda(Guid empresaId)
    {
        var item = new EasyStock.Domain.Entities.ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = Guid.NewGuid(),
            QuantidadeInicial = EasyStock.Domain.ValueObjects.Quantidade.From(10),
            QuantidadeAtual = EasyStock.Domain.ValueObjects.Quantidade.From(10),
            CustoUnitario = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(20m),
            Status = EasyStock.Domain.Enums.StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
        return EasyStock.Domain.Entities.MovimentacaoEstoque.CriarSaida(
            Guid.NewGuid(), empresaId, item, Guid.NewGuid(), EasyStock.Domain.Enums.NaturezaMovimentacaoEstoque.Venda,
            EasyStock.Domain.ValueObjects.Quantidade.From(3), EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(50m),
            DateTime.UtcNow, null, null, DateTime.UtcNow);
    }

    private static System.Text.Json.JsonElement DataJson(IActionResult result)
    {
        var value = result.Should().BeOfType<OkObjectResult>().Subject.Value;
        var json = System.Text.Json.JsonSerializer.Serialize(value,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        return System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("data").Clone();
    }
}
