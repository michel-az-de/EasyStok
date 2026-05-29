using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Bateria de estabilidade do fluxo "criar conta a receber". Espelha
/// <see cref="CriarContaPagarUseCaseTests"/>: regressão de DateTime + cenários
/// absurdos que devem virar UseCaseValidationException (→ 400), nunca 500.
/// </summary>
public class CriarContaReceberUseCaseTests
{
    private readonly IContaReceberRepository _repo = Substitute.For<IContaReceberRepository>();
    private readonly ICategoriaFinanceiraRepository _categoriaRepo = Substitute.For<ICategoriaFinanceiraRepository>();
    private readonly ICentroCustoRepository _centroRepo = Substitute.For<ICentroCustoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _categoriaId = Guid.NewGuid();

    private CriarContaReceberUseCase Sut() => new(_repo, _categoriaRepo, _centroRepo, _uow,
        Substitute.For<ILogger<CriarContaReceberUseCase>>());

    private void StubCategoria(TipoCategoriaFinanceira tipo = TipoCategoriaFinanceira.Receita, bool ativa = true)
    {
        var categoria = CategoriaFinanceira.Criar(_empresaId, "Cat", tipo);
        if (!ativa) categoria.Inativar();
        _categoriaRepo.GetByIdAsync(_empresaId, _categoriaId, Arg.Any<CancellationToken>()).Returns(categoria);
    }

    private CriarContaReceberCommand Cmd(
        IReadOnlyList<ParcelaSpec> parcelas,
        string descricao = "Mensalidade",
        DateTime? emissao = null,
        DateTime? competencia = null,
        string? documentoReferencia = null,
        bool emitir = false,
        Guid? empresaId = null) =>
        new(empresaId ?? _empresaId, ClienteId: null,
            CategoriaFinanceiraId: _categoriaId,
            Descricao: descricao, DataEmissao: emissao ?? DateTime.UtcNow,
            Parcelas: parcelas, DataCompetencia: competencia,
            DocumentoReferencia: documentoReferencia, EmitirAposCriar: emitir);

    private static DateTime SemFuso(int addDays = 0) =>
        DateTime.SpecifyKind(DateTime.UtcNow.AddDays(addDays), DateTimeKind.Unspecified);

    // ── Regressão: normalização UTC ────────────────────────────────────────────

    [Fact]
    public async Task DeveNormalizarTodasAsDatasParaUtc_QuandoClienteEnviaSemFuso()
    {
        StubCategoria();
        ContaReceber? salvo = null;
        _repo.When(r => r.AddAsync(Arg.Any<ContaReceber>(), Arg.Any<CancellationToken>()))
             .Do(ci => salvo = ci.Arg<ContaReceber>());

        await Sut().ExecuteAsync(Cmd(
            new[] { new ParcelaSpec(1, 100m, SemFuso(30)) },
            emissao: SemFuso(), competencia: SemFuso()));

        salvo.Should().NotBeNull();
        salvo!.DataEmissao.Kind.Should().Be(DateTimeKind.Utc);
        salvo.DataCompetencia!.Value.Kind.Should().Be(DateTimeKind.Utc);
        salvo.Parcelas.First().DataVencimento.Kind.Should().Be(DateTimeKind.Utc);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveNormalizarTodasParcelasParaUtc_EComputarValorTotal()
    {
        StubCategoria();
        ContaReceber? salvo = null;
        _repo.When(r => r.AddAsync(Arg.Any<ContaReceber>(), Arg.Any<CancellationToken>()))
             .Do(ci => salvo = ci.Arg<ContaReceber>());

        await Sut().ExecuteAsync(Cmd(new[]
        {
            new ParcelaSpec(1, 100m, SemFuso(30)),
            new ParcelaSpec(2, 50.50m, SemFuso(60)),
            new ParcelaSpec(3, 25.25m, SemFuso(90)),
        }));

        salvo!.Parcelas.Should().HaveCount(3);
        salvo.Parcelas.Should().OnlyContain(p => p.DataVencimento.Kind == DateTimeKind.Utc);
        salvo.ValorTotal.Should().Be(175.75m);
    }

    // ── Cenários absurdos: devem virar validação (nunca 500) ───────────────────

    [Fact]
    public async Task DeveLancarValidation_QuandoEmpresaIdVazio()
    {
        var act = () => Sut().ExecuteAsync(Cmd(
            new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }, empresaId: Guid.Empty));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoSemParcelas()
    {
        StubCategoria();

        var act = () => Sut().ExecuteAsync(Cmd(Array.Empty<ParcelaSpec>()));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*parcela*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoParcelaComValorZeroOuNegativo()
    {
        StubCategoria();

        var act = () => Sut().ExecuteAsync(Cmd(new[] { new ParcelaSpec(1, -10m, DateTime.UtcNow.AddDays(30)) }));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoNumeroDeParcelaDuplicado()
    {
        StubCategoria();

        var act = () => Sut().ExecuteAsync(Cmd(new[]
        {
            new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)),
            new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(60)),
        }));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoDescricaoVazia()
    {
        StubCategoria();

        var act = () => Sut().ExecuteAsync(Cmd(
            new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }, descricao: "   "));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoCategoriaInexistente()
    {
        var act = () => Sut().ExecuteAsync(Cmd(new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*ategoria*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoCategoriaInativa()
    {
        StubCategoria(ativa: false);

        var act = () => Sut().ExecuteAsync(Cmd(new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*inativa*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveRejeitarCategoriaDeDespesa_EmContaReceber()
    {
        StubCategoria(TipoCategoriaFinanceira.Despesa);

        var act = () => Sut().ExecuteAsync(Cmd(new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*despesa*");
        await _uow.DidNotReceive().CommitAsync();
    }

    // ── Cenários válidos não-óbvios ────────────────────────────────────────────

    [Fact]
    public async Task DeveAceitarCategoriaAmbas_EmContaReceber()
    {
        StubCategoria(TipoCategoriaFinanceira.Ambas);

        await Sut().ExecuteAsync(Cmd(new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }));

        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveEmitir_QuandoEmitirAposCriarTrue()
    {
        StubCategoria();
        ContaReceber? salvo = null;
        _repo.When(r => r.AddAsync(Arg.Any<ContaReceber>(), Arg.Any<CancellationToken>()))
             .Do(ci => salvo = ci.Arg<ContaReceber>());

        await Sut().ExecuteAsync(Cmd(new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }, emitir: true));

        salvo!.Status.Should().Be(StatusContaFinanceira.Aberta);
    }

    [Fact]
    public async Task DeveSerIdempotente_QuandoDocumentoReferenciaJaExiste()
    {
        StubCategoria();
        var existente = ContaReceber.Criar(_empresaId, null, _categoriaId, "Existente", DateTime.UtcNow);
        _repo.GetByDocumentoReferenciaAsync(_empresaId, "NF-123", Arg.Any<CancellationToken>())
             .Returns(existente);

        var result = await Sut().ExecuteAsync(Cmd(
            new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }, documentoReferencia: "NF-123"));

        result.Id.Should().Be(existente.Id);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContaReceber>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync();
    }
}
