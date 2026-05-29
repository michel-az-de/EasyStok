using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Application.UseCases.Financeiro.ContasPagar;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Bateria de estabilidade do fluxo "criar conta a pagar". Cobre a regressão de
/// DateTime (Kind=Unspecified → timestamptz) e cenários absurdos de input que
/// NÃO podem virar 500 genérico — todos devem virar UseCaseValidationException
/// (→ 400 com mensagem clara) ou serem aceitos quando válidos.
/// </summary>
public class CriarContaPagarUseCaseTests
{
    private readonly IContaPagarRepository _repo = Substitute.For<IContaPagarRepository>();
    private readonly ICategoriaFinanceiraRepository _categoriaRepo = Substitute.For<ICategoriaFinanceiraRepository>();
    private readonly ICentroCustoRepository _centroRepo = Substitute.For<ICentroCustoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _categoriaId = Guid.NewGuid();

    private CriarContaPagarUseCase Sut() => new(_repo, _categoriaRepo, _centroRepo, _uow,
        Substitute.For<ILogger<CriarContaPagarUseCase>>());

    private void StubCategoria(TipoCategoriaFinanceira tipo = TipoCategoriaFinanceira.Despesa, bool ativa = true)
    {
        var categoria = CategoriaFinanceira.Criar(_empresaId, "Cat", tipo);
        if (!ativa) categoria.Inativar();
        _categoriaRepo.GetByIdAsync(_empresaId, _categoriaId, Arg.Any<CancellationToken>()).Returns(categoria);
    }

    private CriarContaPagarCommand Cmd(
        IReadOnlyList<ParcelaSpec> parcelas,
        string descricao = "Aluguel",
        DateTime? emissao = null,
        DateTime? competencia = null,
        string? documentoReferencia = null,
        bool emitir = false,
        Guid? categoriaId = null,
        Guid? empresaId = null) =>
        new(empresaId ?? _empresaId, FornecedorId: null,
            CategoriaFinanceiraId: categoriaId ?? _categoriaId,
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
        ContaPagar? salvo = null;
        _repo.When(r => r.AddAsync(Arg.Any<ContaPagar>(), Arg.Any<CancellationToken>()))
             .Do(ci => salvo = ci.Arg<ContaPagar>());

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
        ContaPagar? salvo = null;
        _repo.When(r => r.AddAsync(Arg.Any<ContaPagar>(), Arg.Any<CancellationToken>()))
             .Do(ci => salvo = ci.Arg<ContaPagar>());

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

        var act = () => Sut().ExecuteAsync(Cmd(new[] { new ParcelaSpec(1, 0m, DateTime.UtcNow.AddDays(30)) }));

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
        // categoriaRepo não configurado → GetByIdAsync devolve null.
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
    public async Task DeveRejeitarCategoriaDeReceita_EmContaPagar()
    {
        StubCategoria(TipoCategoriaFinanceira.Receita);

        var act = () => Sut().ExecuteAsync(Cmd(new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*receita*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoCentroCustoInvalido()
    {
        StubCategoria();
        var centroId = Guid.NewGuid();
        _centroRepo.GetByIdAsync(_empresaId, centroId, Arg.Any<CancellationToken>())
                   .Returns((CentroCusto?)null);

        var cmd = new CriarContaPagarCommand(_empresaId, null, _categoriaId, "Aluguel",
            DateTime.UtcNow, new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) },
            CentroCustoId: centroId);

        var act = () => Sut().ExecuteAsync(cmd);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*entro de custo*");
        await _uow.DidNotReceive().CommitAsync();
    }

    // ── Cenários válidos não-óbvios ────────────────────────────────────────────

    [Fact]
    public async Task DeveAceitarCategoriaAmbas_EmContaPagar()
    {
        StubCategoria(TipoCategoriaFinanceira.Ambas);

        await Sut().ExecuteAsync(Cmd(new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }));

        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveEmitir_QuandoEmitirAposCriarTrue()
    {
        StubCategoria();
        ContaPagar? salvo = null;
        _repo.When(r => r.AddAsync(Arg.Any<ContaPagar>(), Arg.Any<CancellationToken>()))
             .Do(ci => salvo = ci.Arg<ContaPagar>());

        await Sut().ExecuteAsync(Cmd(new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }, emitir: true));

        salvo!.Status.Should().Be(StatusContaFinanceira.Aberta);
    }

    [Fact]
    public async Task DeveSerIdempotente_QuandoDocumentoReferenciaJaExiste()
    {
        StubCategoria();
        var existente = ContaPagar.Criar(_empresaId, null, _categoriaId, "Existente", DateTime.UtcNow);
        _repo.GetByDocumentoReferenciaAsync(_empresaId, "NF-123", Arg.Any<CancellationToken>())
             .Returns(existente);

        var result = await Sut().ExecuteAsync(Cmd(
            new[] { new ParcelaSpec(1, 100m, DateTime.UtcNow.AddDays(30)) }, documentoReferencia: "NF-123"));

        result.Id.Should().Be(existente.Id);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContaPagar>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync();
    }
}
