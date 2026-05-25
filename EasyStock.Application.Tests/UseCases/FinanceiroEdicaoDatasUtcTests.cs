using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.ContasPagar;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Domain.Entities.Financeiro;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Regressão da classe de bug DateTime Kind=Unspecified → timestamptz nos caminhos
/// de EDIÇÃO e PARCELA (não só no Criar). Antes do fix compartilhado (DataUtc),
/// atualizar competência ou adicionar parcela com data sem fuso quebrava o save
/// com 500 — exatamente "a alteração quebra o que já funcionava".
/// </summary>
public class FinanceiroEdicaoDatasUtcTests
{
    private readonly Guid _empresaId = Guid.NewGuid();

    private static DateTime SemFuso(int addDays = 0) =>
        DateTime.SpecifyKind(DateTime.UtcNow.AddDays(addDays), DateTimeKind.Unspecified);

    // ── AtualizarContaPagar / Receber: DataCompetencia ─────────────────────────

    [Fact]
    public async Task AtualizarContaPagar_DeveNormalizarDataCompetenciaParaUtc()
    {
        var repo = Substitute.For<IContaPagarRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var conta = ContaPagar.Criar(_empresaId, null, Guid.NewGuid(), "Aluguel", DateTime.UtcNow); // Rascunho
        repo.GetByIdAsync(_empresaId, conta.Id, Arg.Any<CancellationToken>()).Returns(conta);

        var sut = new AtualizarContaPagarUseCase(repo,
            Substitute.For<ICategoriaFinanceiraRepository>(),
            Substitute.For<ICentroCustoRepository>(), uow);

        await sut.ExecuteAsync(new AtualizarContaPagarCommand(_empresaId, conta.Id, DataCompetencia: SemFuso()));

        conta.DataCompetencia.Should().NotBeNull();
        conta.DataCompetencia!.Value.Kind.Should().Be(DateTimeKind.Utc);
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task AtualizarContaReceber_DeveNormalizarDataCompetenciaParaUtc()
    {
        var repo = Substitute.For<IContaReceberRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var conta = ContaReceber.Criar(_empresaId, null, Guid.NewGuid(), "Mensalidade", DateTime.UtcNow);
        repo.GetByIdAsync(_empresaId, conta.Id, Arg.Any<CancellationToken>()).Returns(conta);

        var sut = new AtualizarContaReceberUseCase(repo,
            Substitute.For<ICategoriaFinanceiraRepository>(),
            Substitute.For<ICentroCustoRepository>(), uow);

        await sut.ExecuteAsync(new AtualizarContaReceberCommand(_empresaId, conta.Id, DataCompetencia: SemFuso()));

        conta.DataCompetencia.Should().NotBeNull();
        conta.DataCompetencia!.Value.Kind.Should().Be(DateTimeKind.Utc);
        await uow.Received(1).CommitAsync();
    }

    // ── AdicionarParcela ContaPagar / Receber: DataVencimento ──────────────────

    [Fact]
    public async Task AdicionarParcelaContaPagar_DeveNormalizarVencimentoParaUtc()
    {
        var repo = Substitute.For<IContaPagarRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var conta = ContaPagar.Criar(_empresaId, null, Guid.NewGuid(), "Aluguel", DateTime.UtcNow);
        repo.GetByIdAsync(_empresaId, conta.Id, Arg.Any<CancellationToken>()).Returns(conta);

        var sut = new AdicionarParcelaContaPagarUseCase(repo, uow);

        await sut.ExecuteAsync(new AdicionarParcelaContaPagarCommand(
            _empresaId, conta.Id, Numero: 1, Valor: 100m, DataVencimento: SemFuso(30)));

        conta.Parcelas.Should().ContainSingle();
        conta.Parcelas.First().DataVencimento.Kind.Should().Be(DateTimeKind.Utc);
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task AdicionarParcelaContaReceber_DeveNormalizarVencimentoParaUtc()
    {
        var repo = Substitute.For<IContaReceberRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var conta = ContaReceber.Criar(_empresaId, null, Guid.NewGuid(), "Mensalidade", DateTime.UtcNow);
        repo.GetByIdAsync(_empresaId, conta.Id, Arg.Any<CancellationToken>()).Returns(conta);

        var sut = new AdicionarParcelaContaReceberUseCase(repo, uow);

        await sut.ExecuteAsync(new AdicionarParcelaContaReceberCommand(
            _empresaId, conta.Id, Numero: 1, Valor: 100m, DataVencimento: SemFuso(30)));

        conta.Parcelas.Should().ContainSingle();
        conta.Parcelas.First().DataVencimento.Kind.Should().Be(DateTimeKind.Utc);
        await uow.Received(1).CommitAsync();
    }
}
