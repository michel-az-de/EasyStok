using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Faturas.CancelarFatura;
using EasyStock.Application.UseCases.Faturas.Common;
using EasyStock.Application.UseCases.Faturas.EmitirFatura;
using EasyStock.Application.UseCases.Faturas.ListarFaturasCliente;
using EasyStock.Application.UseCases.Faturas.RegistrarPagamentoFatura;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Cobertura para os UCs do modulo Financeiro: emissao, registro de pagamento,
/// cancelamento, listagem cliente. Regressao aqui = bugs em billing — alta gravidade.
/// </summary>
public class FaturasUseCasesTests
{
    private readonly IFaturaRepository _repo = Substitute.For<IFaturaRepository>();
    private readonly IFaturaNumeradorService _numerador = Substitute.For<IFaturaNumeradorService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static FaturaItemInput[] ItensValidos => new[]
    {
        new FaturaItemInput("Mensalidade Plus", 1, 99.90m)
    };

    private static DadosFaturado FaturadoStub() => new("Cliente Teste", Documento: "12345678900");
    private static DadosEmissor EmissorStub() => new("EasyStock");

    // ════════════════════════════════════════════════════════════════════
    // EmitirFatura
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmitirFatura_LancaValidation_QuandoEmpresaIdVazio()
    {
        var uc = new EmitirFaturaUseCase(_repo, _numerador, _uow, Substitute.For<ILogger<EmitirFaturaUseCase>>());
        var cmd = new EmitirFaturaCommand(
            Guid.Empty, FaturadoStub(), EmissorStub(), OrigemFatura.Avulsa,
            DateTime.UtcNow.AddDays(7), ItensValidos);

        await FluentActions.Invoking(() => uc.ExecuteAsync(cmd))
            .Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task EmitirFatura_LancaValidation_QuandoSemItens()
    {
        var uc = new EmitirFaturaUseCase(_repo, _numerador, _uow, Substitute.For<ILogger<EmitirFaturaUseCase>>());
        var cmd = new EmitirFaturaCommand(
            Guid.NewGuid(), FaturadoStub(), EmissorStub(), OrigemFatura.Avulsa,
            DateTime.UtcNow.AddDays(7), Array.Empty<FaturaItemInput>());

        await FluentActions.Invoking(() => uc.ExecuteAsync(cmd))
            .Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task EmitirFatura_GeraNumero_AdicionaItens_EmiteEPersiste()
    {
        var empresaId = Guid.NewGuid();
        _numerador.GerarAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns("2026-000042");

        var uc = new EmitirFaturaUseCase(_repo, _numerador, _uow, Substitute.For<ILogger<EmitirFaturaUseCase>>());
        var cmd = new EmitirFaturaCommand(
            empresaId, FaturadoStub(), EmissorStub(), OrigemFatura.Avulsa,
            DateTime.UtcNow.AddDays(7), ItensValidos);

        var result = await uc.ExecuteAsync(cmd);

        result.Numero.Should().Be("2026-000042");
        result.Total.Should().Be(99.90m);
        await _repo.Received(1).AddAsync(Arg.Is<Fatura>(f =>
            f.Status == StatusFatura.Emitida
            && f.Numero == "2026-000042"
            && f.Total == 99.90m
            && f.Itens.Count == 1
            && f.Eventos.Count >= 2 // Criada + Emitida
        ), Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task EmitirFatura_Idempotente_QuandoFaturaAtivaJaExisteParaOrigem()
    {
        var empresaId = Guid.NewGuid();
        var assinaturaId = Guid.NewGuid();

        var existente = Fatura.Criar(
            empresaId, "2026-000010", FaturadoStub(), EmissorStub(),
            OrigemFatura.Assinatura, DateTime.UtcNow, DateTime.UtcNow.AddDays(7),
            origemRefId: assinaturaId);
        existente.AdicionarItem("Plano", 1, 99m);
        existente.Emitir();

        _repo.GetByOrigemAsync(empresaId, OrigemFatura.Assinatura, assinaturaId, Arg.Any<CancellationToken>())
            .Returns(existente);

        var uc = new EmitirFaturaUseCase(_repo, _numerador, _uow, Substitute.For<ILogger<EmitirFaturaUseCase>>());
        var cmd = new EmitirFaturaCommand(
            empresaId, FaturadoStub(), EmissorStub(), OrigemFatura.Assinatura,
            DateTime.UtcNow.AddDays(7), ItensValidos,
            OrigemRefId: assinaturaId);

        var result = await uc.ExecuteAsync(cmd);

        result.FaturaId.Should().Be(existente.Id);
        // Nao deve ter chamado o numerador nem add
        await _numerador.DidNotReceiveWithAnyArgs().GerarAsync(default, default);
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    // ════════════════════════════════════════════════════════════════════
    // RegistrarPagamentoFatura
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RegistrarPagamento_LancaValidation_QuandoFaturaNaoEncontrada()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Fatura?)null);

        var uc = new RegistrarPagamentoFaturaUseCase(_repo, _uow,
            Substitute.For<ILogger<RegistrarPagamentoFaturaUseCase>>());

        var act = () => uc.ExecuteAsync(new RegistrarPagamentoFaturaCommand(
            Guid.NewGuid(), Guid.NewGuid(), "manual", 50m));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*nao encontrada*");
    }

    [Fact]
    public async Task RegistrarPagamento_LancaValidation_QuandoValorZero()
    {
        var uc = new RegistrarPagamentoFaturaUseCase(_repo, _uow,
            Substitute.For<ILogger<RegistrarPagamentoFaturaUseCase>>());

        var act = () => uc.ExecuteAsync(new RegistrarPagamentoFaturaCommand(
            Guid.NewGuid(), Guid.NewGuid(), "manual", 0));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task RegistrarPagamento_Confirmado_AtualizaStatusEPersiste()
    {
        var empresaId = Guid.NewGuid();
        var fatura = NovaFaturaEmitida(empresaId, total: 100m);
        _repo.GetByIdAsync(empresaId, fatura.Id, Arg.Any<CancellationToken>()).Returns(fatura);

        var uc = new RegistrarPagamentoFaturaUseCase(_repo, _uow,
            Substitute.For<ILogger<RegistrarPagamentoFaturaUseCase>>());

        var result = await uc.ExecuteAsync(new RegistrarPagamentoFaturaCommand(
            empresaId, fatura.Id, "manual", 100m,
            StatusInicial: StatusFaturaPagamento.Confirmado));

        result.StatusFatura.Should().Be(nameof(StatusFatura.Paga));
        result.TotalPago.Should().Be(100m);
        result.Pendente.Should().Be(0);
        await _uow.Received(1).CommitAsync();
        fatura.Eventos.Should().Contain(e => e.Tipo == TipoEventoFatura.PagamentoConfirmado);
    }

    [Fact]
    public async Task RegistrarPagamento_Pendente_NaoFinalizaStatusFatura()
    {
        var empresaId = Guid.NewGuid();
        var fatura = NovaFaturaEmitida(empresaId, total: 100m);
        _repo.GetByIdAsync(empresaId, fatura.Id, Arg.Any<CancellationToken>()).Returns(fatura);

        var uc = new RegistrarPagamentoFaturaUseCase(_repo, _uow,
            Substitute.For<ILogger<RegistrarPagamentoFaturaUseCase>>());

        var result = await uc.ExecuteAsync(new RegistrarPagamentoFaturaCommand(
            empresaId, fatura.Id, "pix", 100m,
            StatusInicial: StatusFaturaPagamento.Pendente));

        result.StatusFatura.Should().Be(nameof(StatusFatura.Emitida));
        result.TotalPago.Should().Be(0);
    }

    // ════════════════════════════════════════════════════════════════════
    // CancelarFatura
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cancelar_LancaValidation_QuandoNaoEncontrada()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Fatura?)null);

        var uc = new CancelarFaturaUseCase(_repo, _uow, Substitute.For<ILogger<CancelarFaturaUseCase>>());

        var act = () => uc.ExecuteAsync(new CancelarFaturaCommand(Guid.NewGuid(), Guid.NewGuid()));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Cancelar_FaturaPaga_LancaValidation()
    {
        var empresaId = Guid.NewGuid();
        var fatura = NovaFaturaEmitida(empresaId, total: 100m);
        fatura.RegistrarPagamento(FaturaPagamento.CriarConfirmado(fatura.Id, "pix", 100m, "EfiPix", empresaId));
        _repo.GetByIdAsync(empresaId, fatura.Id, Arg.Any<CancellationToken>()).Returns(fatura);

        var uc = new CancelarFaturaUseCase(_repo, _uow, Substitute.For<ILogger<CancelarFaturaUseCase>>());

        var act = () => uc.ExecuteAsync(new CancelarFaturaCommand(empresaId, fatura.Id, "tentativa"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Cancelar_FaturaEmitida_RegistraEvento()
    {
        var empresaId = Guid.NewGuid();
        var fatura = NovaFaturaEmitida(empresaId, total: 100m);
        _repo.GetByIdAsync(empresaId, fatura.Id, Arg.Any<CancellationToken>()).Returns(fatura);

        var uc = new CancelarFaturaUseCase(_repo, _uow, Substitute.For<ILogger<CancelarFaturaUseCase>>());

        await uc.ExecuteAsync(new CancelarFaturaCommand(empresaId, fatura.Id, "duplicada"));

        fatura.Status.Should().Be(StatusFatura.Cancelada);
        fatura.Eventos.Should().Contain(e => e.Tipo == TipoEventoFatura.Cancelada);
        await _uow.Received(1).CommitAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // ListarFaturasCliente
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListarCliente_LancaValidation_QuandoEmpresaIdVazio()
    {
        var uc = new ListarFaturasClienteUseCase(_repo);
        var act = () => uc.ExecuteAsync(new ListarFaturasClienteCommand(Guid.Empty));
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task ListarCliente_PassaFiltrosERetornaPaginacaoNormalizada()
    {
        var empresaId = Guid.NewGuid();
        _repo.ListarClienteAsync(empresaId,
                Arg.Any<StatusFatura?>(),
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
                page: 1, pageSize: 50,
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<Fatura>() as IReadOnlyList<Fatura>, 0));

        var uc = new ListarFaturasClienteUseCase(_repo);
        var result = await uc.ExecuteAsync(new ListarFaturasClienteCommand(
            empresaId, Status: null, Page: 0, PageSize: 50)); // page=0 → 1

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(50);
        result.Total.Should().Be(0);
    }

    private static Fatura NovaFaturaEmitida(Guid empresaId, decimal total)
    {
        var f = Fatura.Criar(
            empresaId, "2026-000777", FaturadoStub(), EmissorStub(),
            OrigemFatura.Avulsa, DateTime.UtcNow, DateTime.UtcNow.AddDays(7));
        f.AdicionarItem("Servico", 1, total);
        f.Emitir();
        return f;
    }
}
