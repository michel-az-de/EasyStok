using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Domain.Reposicao;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class ObterReposicaoUseCaseTests
{
    private readonly IAnalyticsRepository _analytics = Substitute.For<IAnalyticsRepository>();
    private readonly IConfiguracaoLojaRepository _config = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly ILogger<ObterReposicaoUseCase> _logger = Substitute.For<ILogger<ObterReposicaoUseCase>>();

    private ObterReposicaoUseCase CriarUseCase() => new(_analytics, _config, _logger);

    [Fact]
    public async Task ExecuteAsync_ComEmpresaIdVazio_LancaValidacao()
    {
        var useCase = CriarUseCase();
        var act = async () => await useCase.ExecuteAsync(new ObterReposicaoCommand(Guid.Empty));
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task ExecuteAsync_NuncaEstocado_ClassificaEsgotado_EFiltraOk()
    {
        // R2: produto nunca-estocado (vigente 0) SEMPRE entra como ESGOTADO; produto saudavel sai.
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoEsgotado = Guid.NewGuid();
        var produtoOk = Guid.NewGuid();

        var configuracao = ConfiguracaoLoja.CriarPadrao(lojaId);
        configuracao.QuantidadeMinimaPadrao = 5;
        configuracao.QuantidadeCriticaPadrao = 2;
        configuracao.DiasCoberturaAlvo = 10;
        configuracao.LeadTimePadraoDias = 3;
        _config.GetByLojaIdAsync(lojaId).Returns(configuracao);

        var snapshot = new List<ProdutoReposicaoSnapshot>
        {
            new(produtoEsgotado, null, "Nunca Estocado", 0m,
                ProdutoMinima: null, ProdutoCritica: null,
                CategoriaMinima: null, CategoriaCritica: null,
                ConfigMinima: 5, ConfigCritica: 2,
                VelocidadeMediaDia: 0m, DiasHistoricoVelocidade: 0,
                LeadTimeDias: 3, TamanhoLote: 1,
                ValidadeMediaDiasRestantes: null, FornecedorId: null),
            new(produtoOk, null, "Saudavel", 20m,
                ProdutoMinima: null, ProdutoCritica: null,
                CategoriaMinima: null, CategoriaCritica: null,
                ConfigMinima: 5, ConfigCritica: 2,
                VelocidadeMediaDia: 1m, DiasHistoricoVelocidade: 30,
                LeadTimeDias: 3, TamanhoLote: 1,
                ValidadeMediaDiasRestantes: null, FornecedorId: null)
        };

        _analytics.GetSnapshotReposicaoAsync(
                empresaId, lojaId, Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var useCase = CriarUseCase();
        var resultado = await useCase.ExecuteAsync(new ObterReposicaoCommand(empresaId, lojaId, DiasHistorico: 30));

        resultado.Should().HaveCount(1);
        var item = resultado.Single();
        item.ProdutoId.Should().Be(produtoEsgotado);
        item.Estado.Should().Be(EstadoReposicao.Esgotado);
        item.QuantidadeVigente.Should().Be(0m);

        // A porta recebeu lead time (3), minimo (5) e critico (2) vindos da config da loja.
        await _analytics.Received(1).GetSnapshotReposicaoAsync(
            empresaId, lojaId, 30, 3, 5, 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SemLojaId_UsaDefaultsGlobais_ENaoLeConfig()
    {
        var empresaId = Guid.NewGuid();
        _analytics.GetSnapshotReposicaoAsync(
                empresaId, null, Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProdutoReposicaoSnapshot>());

        var useCase = CriarUseCase();
        var resultado = await useCase.ExecuteAsync(new ObterReposicaoCommand(empresaId));

        // Sem LojaId nao consulta config; passa limiares nulos a porta e lead time padrao global (7).
        resultado.Should().BeEmpty();
        await _config.DidNotReceive().GetByLojaIdAsync(Arg.Any<Guid>());
        await _analytics.Received(1).GetSnapshotReposicaoAsync(
            empresaId, null, 30, 7, null, null, Arg.Any<CancellationToken>());
    }
}
