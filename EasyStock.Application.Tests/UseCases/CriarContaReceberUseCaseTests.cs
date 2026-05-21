using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class CriarContaReceberUseCaseTests
{
    private readonly IContaReceberRepository _repo = Substitute.For<IContaReceberRepository>();
    private readonly ICategoriaFinanceiraRepository _categoriaRepo = Substitute.For<ICategoriaFinanceiraRepository>();
    private readonly ICentroCustoRepository _centroRepo = Substitute.For<ICentroCustoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CriarContaReceberUseCase Sut() => new(_repo, _categoriaRepo, _centroRepo, _uow,
        Substitute.For<ILogger<CriarContaReceberUseCase>>());

    private void StubCategoria(Guid empresaId, Guid categoriaId, TipoCategoriaFinanceira tipo)
    {
        var categoria = CategoriaFinanceira.Criar(empresaId, "Cat", tipo);
        _categoriaRepo.GetByIdAsync(empresaId, categoriaId, Arg.Any<CancellationToken>()).Returns(categoria);
    }

    [Fact]
    public async Task DeveNormalizarDatasParaUtc_QuandoClienteEnviaDatasSemFuso()
    {
        // Regressão (estabilidade): emissão/competência/vencimento chegam do cliente
        // com Kind=Unspecified e o Postgres (timestamp with time zone) rejeita no save.
        // O use case deve normalizar TODAS pra UTC antes de persistir.
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        StubCategoria(empresaId, categoriaId, TipoCategoriaFinanceira.Receita);

        ContaReceber? salvo = null;
        _repo.When(r => r.AddAsync(Arg.Any<ContaReceber>(), Arg.Any<CancellationToken>()))
             .Do(ci => salvo = ci.Arg<ContaReceber>());

        var emissaoSemFuso = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var competenciaSemFuso = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var vencimentoSemFuso = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30), DateTimeKind.Unspecified);

        await Sut().ExecuteAsync(new CriarContaReceberCommand(
            empresaId, ClienteId: null, CategoriaFinanceiraId: categoriaId,
            Descricao: "Mensalidade", DataEmissao: emissaoSemFuso,
            Parcelas: new[] { new ParcelaSpec(1, 100m, vencimentoSemFuso) },
            DataCompetencia: competenciaSemFuso));

        salvo.Should().NotBeNull();
        salvo!.DataEmissao.Kind.Should().Be(DateTimeKind.Utc);
        salvo.DataCompetencia.Should().NotBeNull();
        salvo.DataCompetencia!.Value.Kind.Should().Be(DateTimeKind.Utc);
        salvo.Parcelas.Should().ContainSingle();
        salvo.Parcelas.First().DataVencimento.Kind.Should().Be(DateTimeKind.Utc);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoCategoriaInexistente()
    {
        // Antes do fix o erro vazava como 500 genérico ("nao retorna erro da api").
        // Categoria ausente deve virar UseCaseValidationException → 400 com mensagem clara.
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        // categoriaRepo não configurado → GetByIdAsync devolve null.

        var act = () => Sut().ExecuteAsync(new CriarContaReceberCommand(
            empresaId, ClienteId: null, CategoriaFinanceiraId: categoriaId,
            Descricao: "Mensalidade", DataEmissao: DateTime.UtcNow,
            Parcelas: new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*ategoria*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveRejeitarCategoriaDeDespesa_EmContaReceber()
    {
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        StubCategoria(empresaId, categoriaId, TipoCategoriaFinanceira.Despesa);

        var act = () => Sut().ExecuteAsync(new CriarContaReceberCommand(
            empresaId, ClienteId: null, CategoriaFinanceiraId: categoriaId,
            Descricao: "Mensalidade", DataEmissao: DateTime.UtcNow,
            Parcelas: new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*despesa*");
        await _uow.DidNotReceive().CommitAsync();
    }
}
