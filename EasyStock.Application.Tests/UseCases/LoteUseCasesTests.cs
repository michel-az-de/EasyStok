using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ConferirEtiqueta;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.FinalizarLote;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Cobertura para os UCs do ciclo de produção em Lote: Criar, Finalizar
/// (gera etiquetas), Conferir etiqueta. Critico para rastreabilidade
/// na cadeia produtiva.
/// </summary>
public class LoteUseCasesTests
{
    private readonly ILoteRepository _repo = Substitute.For<ILoteRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    // ════════════════════════════════════════════════════════════════════
    // CriarLote
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CriarLote_DeveLancarValidation_QuandoEmpresaIdVazio()
    {
        var useCase = new CriarLoteUseCase(_repo, _produtoRepo, _uow,
            Substitute.For<ILogger<CriarLoteUseCase>>());

        var act = () => useCase.ExecuteAsync(new CriarLoteCommand(Guid.Empty));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task CriarLote_DeveLancarValidation_QuandoCodigoCustomDuplicado()
    {
        var empresaId = Guid.NewGuid();
        var existente = Lote.Criar(empresaId, "LOT-CUSTOM-1");
        _repo.FindByCodigoAsync(empresaId, "LOT-CUSTOM-1").Returns(existente);

        var useCase = new CriarLoteUseCase(_repo, _produtoRepo, _uow,
            Substitute.For<ILogger<CriarLoteUseCase>>());

        var act = () => useCase.ExecuteAsync(new CriarLoteCommand(empresaId,
            CodigoCustom: "LOT-CUSTOM-1"));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*já existe*");
    }

    [Fact]
    public async Task CriarLote_DeveGerarCodigoSequencial_QuandoSemCodigoCustom()
    {
        var empresaId = Guid.NewGuid();
        var dataProd = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        var data = DateOnly.FromDateTime(dataProd);
        _repo.GetNextSequencialDoDiaAsync(empresaId, data).Returns(7);

        Lote? capturado = null;
        await _repo.AddAsync(Arg.Do<Lote>(l => capturado = l));

        var useCase = new CriarLoteUseCase(_repo, _produtoRepo, _uow,
            Substitute.For<ILogger<CriarLoteUseCase>>());

        var result = await useCase.ExecuteAsync(new CriarLoteCommand(empresaId,
            DataProducao: dataProd));

        capturado.Should().NotBeNull();
        capturado!.Codigo.Should().Be("LOT-260501-007");
        result.Codigo.Should().Be("LOT-260501-007");
    }

    [Fact]
    public async Task CriarLote_DeveLancarValidation_QuandoItemTemQuantidadeZero()
    {
        var empresaId = Guid.NewGuid();
        _repo.GetNextSequencialDoDiaAsync(empresaId, Arg.Any<DateOnly>()).Returns(1);

        var useCase = new CriarLoteUseCase(_repo, _produtoRepo, _uow,
            Substitute.For<ILogger<CriarLoteUseCase>>());

        var act = () => useCase.ExecuteAsync(new CriarLoteCommand(empresaId, Itens: new[]
        {
            new CriarLoteItemInput("Pão", 0)
        }));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*maior que zero*");
        await _repo.DidNotReceive().AddAsync(Arg.Any<Lote>());
    }

    [Fact]
    public async Task CriarLote_DeveCalcularExpiraEmComBaseEmDataProducao()
    {
        var empresaId = Guid.NewGuid();
        var dataProd = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        _repo.GetNextSequencialDoDiaAsync(empresaId, Arg.Any<DateOnly>()).Returns(1);

        var produtoPaoId = Guid.NewGuid();
        var produtoBoloId = Guid.NewGuid();
        _produtoRepo.GetTipoEmbalagemMapAsync(empresaId, Arg.Any<IEnumerable<Guid>>())
            .Returns((IReadOnlyDictionary<Guid, TipoEmbalagem>)new Dictionary<Guid, TipoEmbalagem>());

        Lote? capturado = null;
        await _repo.AddAsync(Arg.Do<Lote>(l => capturado = l));

        var useCase = new CriarLoteUseCase(_repo, _produtoRepo, _uow,
            Substitute.For<ILogger<CriarLoteUseCase>>());

        await useCase.ExecuteAsync(new CriarLoteCommand(empresaId,
            DataProducao: dataProd,
            Itens: new[]
            {
                new CriarLoteItemInput("Pão de queijo", 10, ProdutoId: produtoPaoId, ValidadeDias: 5),
                new CriarLoteItemInput("Bolo", 3, ProdutoId: produtoBoloId, ValidadeDias: null)  // sem validade
            }));

        capturado!.Itens.Should().HaveCount(2);
        capturado.Itens.First(i => i.Nome == "Pão de queijo").ExpiraEm
            .Should().Be(dataProd.AddDays(5));
        capturado.Itens.First(i => i.Nome == "Bolo").ExpiraEm.Should().BeNull();
        capturado.TotalUnidades.Should().Be(13);
    }

    // ════════════════════════════════════════════════════════════════════
    // FinalizarLote
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FinalizarLote_DeveRetornarNull_QuandoLoteNaoExiste()
    {
        var empresaId = Guid.NewGuid();
        var loteId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(empresaId, loteId).Returns((Lote?)null);

        var useCase = new FinalizarLoteUseCase(_repo, _produtoRepo, _uow,
            Substitute.For<ILogger<FinalizarLoteUseCase>>());

        var result = await useCase.ExecuteAsync(new FinalizarLoteCommand(empresaId, loteId));

        result.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task FinalizarLote_DeveSerIdempotente_QuandoJaFinalizado()
    {
        var empresaId = Guid.NewGuid();
        var lote = Lote.Criar(empresaId, "LOT-001");
        lote.Finalizar();
        _repo.GetByIdWithDetailsAsync(empresaId, lote.Id).Returns(lote);

        var useCase = new FinalizarLoteUseCase(_repo, _produtoRepo, _uow,
            Substitute.For<ILogger<FinalizarLoteUseCase>>());

        var result = await useCase.ExecuteAsync(new FinalizarLoteCommand(empresaId, lote.Id));

        result.Should().NotBeNull();
        await _repo.DidNotReceive().AddEtiquetaAsync(Arg.Any<LoteEtiqueta>());
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Lote>());
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task FinalizarLote_DeveLancarValidation_QuandoLoteSemItens()
    {
        var empresaId = Guid.NewGuid();
        var lote = Lote.Criar(empresaId, "LOT-002");
        _repo.GetByIdWithDetailsAsync(empresaId, lote.Id).Returns(lote);

        var useCase = new FinalizarLoteUseCase(_repo, _produtoRepo, _uow,
            Substitute.For<ILogger<FinalizarLoteUseCase>>());

        var act = () => useCase.ExecuteAsync(new FinalizarLoteCommand(empresaId, lote.Id));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*sem itens*");
    }

    [Fact]
    public async Task FinalizarLote_DeveGerarUmaEtiquetaPorUnidadeComSequencialGlobal()
    {
        var empresaId = Guid.NewGuid();
        var lote = Lote.Criar(empresaId, "LOT-260501-005");
        var item1 = new LoteItem { Id = Guid.NewGuid(), LoteId = lote.Id, Nome = "A", Quantidade = 3 };
        var item2 = new LoteItem { Id = Guid.NewGuid(), LoteId = lote.Id, Nome = "B", Quantidade = 2 };
        lote.Itens.Add(item1);
        lote.Itens.Add(item2);
        _repo.GetByIdWithDetailsAsync(empresaId, lote.Id).Returns(lote);

        var etiquetas = new List<LoteEtiqueta>();
        await _repo.AddEtiquetaAsync(Arg.Do<LoteEtiqueta>(e => etiquetas.Add(e)));

        var useCase = new FinalizarLoteUseCase(_repo, _produtoRepo, _uow,
            Substitute.For<ILogger<FinalizarLoteUseCase>>());

        var result = await useCase.ExecuteAsync(new FinalizarLoteCommand(empresaId, lote.Id));

        result.Should().NotBeNull();
        etiquetas.Should().HaveCount(5); // 3 + 2
        etiquetas.Select(e => e.Sequencial).Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
        etiquetas.Select(e => e.Codigo).Should().BeEquivalentTo(new[]
        {
            "LOT-260501-005-0001", "LOT-260501-005-0002", "LOT-260501-005-0003",
            "LOT-260501-005-0004", "LOT-260501-005-0005"
        });
        etiquetas.Should().AllSatisfy(e => e.Status.Should().Be("pendente"));
        etiquetas.Where(e => e.LoteItemId == item1.Id).Should().HaveCount(3);
        etiquetas.Where(e => e.LoteItemId == item2.Id).Should().HaveCount(2);
        lote.Status.Should().Be("finalizado");
        await _uow.Received(1).CommitAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // ConferirEtiqueta
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConferirEtiqueta_DeveLancarValidation_QuandoCodigoVazio()
    {
        var useCase = new ConferirEtiquetaUseCase(_repo, _uow,
            Substitute.For<ILogger<ConferirEtiquetaUseCase>>());

        var act = () => useCase.ExecuteAsync(
            new ConferirEtiquetaCommand(Guid.NewGuid(), "  ", "conferida"));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*Código*");
    }

    [Fact]
    public async Task ConferirEtiqueta_DeveLancarValidation_QuandoStatusInvalido()
    {
        var useCase = new ConferirEtiquetaUseCase(_repo, _uow,
            Substitute.For<ILogger<ConferirEtiquetaUseCase>>());

        var act = () => useCase.ExecuteAsync(
            new ConferirEtiquetaCommand(Guid.NewGuid(), "LOT-001-0001", "exotica"));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*Status inválido*");
    }

    [Fact]
    public async Task ConferirEtiqueta_DeveRetornarNull_QuandoEtiquetaNaoEncontrada()
    {
        var empresaId = Guid.NewGuid();
        _repo.FindEtiquetaPorCodigoAsync(empresaId, "INEXISTENTE")
            .Returns((LoteEtiqueta?)null);

        var useCase = new ConferirEtiquetaUseCase(_repo, _uow,
            Substitute.For<ILogger<ConferirEtiquetaUseCase>>());

        var result = await useCase.ExecuteAsync(
            new ConferirEtiquetaCommand(empresaId, "INEXISTENTE", "conferida"));

        result.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task ConferirEtiqueta_DeveAtualizarStatusComMetadados_QuandoSucesso()
    {
        var empresaId = Guid.NewGuid();
        var operadorId = Guid.NewGuid();
        var etq = new LoteEtiqueta
        {
            Id = Guid.NewGuid(),
            LoteId = Guid.NewGuid(),
            LoteItemId = Guid.NewGuid(),
            Sequencial = 1,
            Codigo = "LOT-260501-005-0001",
            Status = "pendente",
            CriadoEm = DateTime.UtcNow.AddHours(-1)
        };
        _repo.FindEtiquetaPorCodigoAsync(empresaId, "LOT-260501-005-0001").Returns(etq);

        var useCase = new ConferirEtiquetaUseCase(_repo, _uow,
            Substitute.For<ILogger<ConferirEtiquetaUseCase>>());

        var result = await useCase.ExecuteAsync(
            new ConferirEtiquetaCommand(empresaId, "LOT-260501-005-0001", "DIVERGENTE",
                Observacao: "peso fora", ConferidaPorUserId: operadorId, ConferidaPorNome: "Joao"));

        result.Should().NotBeNull();
        result!.Status.Should().Be("divergente"); // case-insensitive
        result.ConferidaPorUserId.Should().Be(operadorId);
        result.ConferidaPorNome.Should().Be("Joao");
        result.ObservacaoConferencia.Should().Be("peso fora");
        result.ConferidaEm.Should().NotBeNull();
        await _repo.Received(1).UpdateEtiquetaAsync(etq);
        await _uow.Received(1).CommitAsync();
    }
}
