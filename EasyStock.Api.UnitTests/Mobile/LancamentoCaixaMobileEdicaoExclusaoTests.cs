using System.Text.Json;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #1520 (ADR-0060): edicao e exclusao de lancamento de caixa feitas no PWA nao chegavam ao
/// servidor. O dispatcher respondia "aceito" e ignorava o lancamento que ja existia, e o PWA nem
/// emitia a exclusao. Reproduz o caminho do SyncController: dispatcher, SaveChanges, linker.
/// </summary>
public sealed class LancamentoCaixaMobileEdicaoExclusaoTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Lancamento = "cash-1";

    private readonly EasyStockDbContext _db;
    private readonly SyncMutationDispatcher _dispatcher;
    private readonly CashEntryLinker _linker;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();
    private readonly long _criadoEm = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();

    public LancamentoCaixaMobileEdicaoExclusaoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"caixa-edicao-exclusao-{Guid.NewGuid()}")
                .Options,
            currentUser);

        _dispatcher = new SyncMutationDispatcher(
            _db,
            new MobileStockReconciler(_db, new MobileSystemUserResolver(_db), NullLogger<MobileStockReconciler>.Instance),
            new LoteMobileEstadoReconciler(_db, NullLogger<LoteMobileEstadoReconciler>.Instance),
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance),
            Substitute.For<IProdutoRepository>(),
            DispatcherDeTeste.EstornoDeCaixa(_db),
            NullLogger<SyncMutationDispatcher>.Instance);
        _linker = new CashEntryLinker(_db, NullLogger<CashEntryLinker>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ---------- edicao ----------

    [Fact]
    public async Task Editar_AtualizaOLancamentoEOMovimentoVinculado()
    {
        await SincronizarAsync(Dto("expense", 30m, "Gas", "dinheiro"));

        await SincronizarAsync(Dto("income", 45.5m, "Troco do fornecedor", "pix"));

        var lancamento = LancamentoSalvo();
        lancamento.Type.Should().Be("income");
        lancamento.Amount.Should().Be(45.5m);
        lancamento.Description.Should().Be("Troco do fornecedor");
        lancamento.Metodo.Should().Be("pix");
        var movimento = Movimentos().Should().ContainSingle("editar nao cria outro movimento").Subject;
        movimento.Tipo.Should().Be("entrada");
        movimento.Valor.Should().Be(45.5m);
        movimento.Descricao.Should().Be("Troco do fornecedor");
        movimento.Metodo.Should().Be("pix");
        movimento.EstornadoEm.Should().BeNull();
    }

    [Fact]
    public async Task Editar_SemFormaDePagamento_MantemAFormaGravada()
    {
        await SincronizarAsync(Dto("expense", 30m, "Gas", "pix"));

        await SincronizarAsync(Dto("expense", 35m, "Gas", metodo: null));

        LancamentoSalvo().Metodo.Should().Be("pix", "aparelho antigo nao manda a forma (#1493)");
        Movimentos().Single().Metodo.Should().Be("pix");
        Movimentos().Single().Valor.Should().Be(35m);
    }

    [Fact]
    public async Task Editar_DiaComCaixaFechado_RecusaComMotivoENaoAltera()
    {
        await SincronizarAsync(Dto("expense", 30m, "Gas", "dinheiro"));
        FecharCaixaDoDiaDoLancamento();

        var editar = () => SincronizarAsync(Dto("expense", 99m, "Gas", "dinheiro"));

        (await editar.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*caixa*fechado*");
        _db.ChangeTracker.Clear();
        LancamentoSalvo().Amount.Should().Be(30m);
        Movimentos().Single().Valor.Should().Be(30m);
    }

    [Fact]
    public async Task ReenviarOMesmoLancamento_DiaFechado_NaoERecusado()
    {
        // "Sincronizar tudo" reenvia lancamentos de dias ja fechados, sem mudanca nenhuma.
        await SincronizarAsync(Dto("expense", 30m, "Gas", "dinheiro"));
        FecharCaixaDoDiaDoLancamento();

        await SincronizarAsync(Dto("expense", 30m, "Gas", "dinheiro"));

        Movimentos().Should().ContainSingle().Which.Valor.Should().Be(30m);
    }

    [Fact]
    public async Task Editar_MovimentoJaEstornadoNoErp_NaoMexeNaTrilhaDoEstorno()
    {
        await SincronizarAsync(Dto("expense", 30m, "Gas", "dinheiro"));
        var movimento = _db.Set<MovimentoCaixa>().IgnoreQueryFilters().Single(m => m.EmpresaId == _empresaId);
        movimento.Estornar(null, "Gerente", "lancado errado");
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        await SincronizarAsync(Dto("expense", 99m, "Gas", "dinheiro"));

        LancamentoSalvo().Amount.Should().Be(99m);
        Movimentos().Single().Valor.Should().Be(30m);
    }

    // ---------- exclusao ----------

    [Fact]
    public async Task Excluir_EstornaOMovimentoSemApagarALinha()
    {
        await SincronizarAsync(Dto("expense", 30m, "Gas", "dinheiro"));

        await ExcluirAsync();

        LancamentoSalvo().DeletedAt.Should().NotBeNull();
        LancamentoSalvo().DeletedBy.Should().Be("Thati");
        var movimento = Movimentos().Should().ContainSingle("o movimento fica, estornado").Subject;
        movimento.EstornadoEm.Should().NotBeNull();
        movimento.EstornadoPorNome.Should().Be("Thati");
        movimento.Valor.Should().Be(30m);
    }

    [Fact]
    public async Task Excluir_Reenvio_NaoEstornaDeNovoNemRecusa()
    {
        await SincronizarAsync(Dto("expense", 30m, "Gas", "dinheiro"));
        await ExcluirAsync();
        var excluidoEm = LancamentoSalvo().DeletedAt;
        var estornadoEm = Movimentos().Single().EstornadoEm;

        await ExcluirAsync();

        LancamentoSalvo().DeletedAt.Should().Be(excluidoEm);
        Movimentos().Should().ContainSingle().Which.EstornadoEm.Should().Be(estornadoEm);
    }

    [Fact]
    public async Task Excluir_DiaComCaixaFechado_RecusaComMotivoENaoEstorna()
    {
        await SincronizarAsync(Dto("expense", 30m, "Gas", "dinheiro"));
        FecharCaixaDoDiaDoLancamento();

        var excluir = () => ExcluirAsync();

        (await excluir.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*caixa*fechado*");
        _db.ChangeTracker.Clear();
        LancamentoSalvo().DeletedAt.Should().BeNull();
        Movimentos().Single().EstornadoEm.Should().BeNull();
    }

    [Fact]
    public async Task Excluir_LancamentoAindaSemMovimento_NaoViraMovimentoDepois()
    {
        // O vinculo com o ERP roda depois do SaveChanges; a exclusao pode chegar no mesmo lote.
        await AplicarAsync("cashEntry.upsert", Dto("expense", 30m, "Gas", "dinheiro"));
        await AplicarAsync("cashEntry.delete", new { id = Lancamento });

        await _linker.ExecuteAsync([Lancamento], _empresaId);

        LancamentoSalvo().DeletedAt.Should().NotBeNull();
        Movimentos().Should().BeEmpty("lancamento excluido nao entra no caixa do ERP");
    }

    [Fact]
    public async Task Excluir_LancamentoQueOServidorNaoConhece_AceitaSemEfeito()
    {
        await ExcluirAsync();

        _db.Set<CashEntry>().Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertDepoisDaExclusao_NaoRessuscitaOLancamento()
    {
        // Outro aparelho que ainda tem o lancamento pode reenvia-lo ("Sincronizar tudo").
        await SincronizarAsync(Dto("expense", 30m, "Gas", "dinheiro"));
        await ExcluirAsync();

        await SincronizarAsync(Dto("expense", 80m, "Gas", "dinheiro"));

        LancamentoSalvo().DeletedAt.Should().NotBeNull();
        LancamentoSalvo().Amount.Should().Be(30m);
        Movimentos().Should().ContainSingle().Which.EstornadoEm.Should().NotBeNull();
    }

    [Fact]
    public async Task LancamentoExcluido_DesceNoPullComoEstornado()
    {
        // O PWA ja remove do aparelho o cashEntry que chega com estornado=true (F7-B).
        await SincronizarAsync(Dto("expense", 30m, "Gas", "dinheiro"));
        (await EstornadoNoPullAsync()).Should().BeFalse();

        await ExcluirAsync();

        (await EstornadoNoPullAsync()).Should().BeTrue();
    }

    private async Task<bool> EstornadoNoPullAsync()
    {
        var http = new DefaultHttpContext();
        http.Items[MobileAuth.HttpContextItemDevice] = new MobileDevice
        {
            Id = "outro-aparelho", ApiKeyHash = "h", EmpresaId = _empresaId, LojaId = _lojaId
        };
        var controller = new SyncController(
            _db, null!, null!, null!, null!,
            Substitute.For<IProdutoRepository>(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["MobileSync:PullReverse:Enabled"] = "false" })
                .Build(),
            TimeProvider.System,
            NullLogger<SyncController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var resultado = await controller.Pull(0, "outro-aparelho");
        _db.ChangeTracker.Clear();

        var corpo = (resultado.Result as OkObjectResult)!.Value.Should().BeOfType<SyncPullResponse>().Subject;
        var payload = corpo.Mutations.Should().ContainSingle(m => m.Type == "cashEntry.upsert").Subject.Payload;
        return payload.TryGetProperty("estornado", out var e) && e.ValueKind == JsonValueKind.True;
    }

    // ---------- apoio ----------

    private CashEntryDto Dto(string tipo, decimal valor, string descricao, string? metodo) =>
        new(Lancamento, tipo, valor, descricao, _criadoEm, Metodo: metodo);

    private async Task AplicarAsync(string tipo, object payload)
    {
        var mutation = new MutationDto(
            $"m-{Guid.NewGuid():N}", "dev-1", tipo,
            JsonSerializer.SerializeToElement(payload, Json),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await _dispatcher.ApplyMutationAsync(mutation, "dev-1", "Thati", _empresaId, _lojaId);
        await _db.SaveChangesAsync();
    }

    private async Task SincronizarAsync(CashEntryDto dto)
    {
        await AplicarAsync("cashEntry.upsert", dto);
        await _linker.ExecuteAsync([dto.Id], _empresaId);
        _db.ChangeTracker.Clear();
    }

    private async Task ExcluirAsync()
    {
        // O PWA manda o lancamento inteiro; o servidor so precisa do id.
        await AplicarAsync("cashEntry.delete", Dto("expense", 30m, "Gas", "dinheiro"));
        await _linker.ExecuteAsync([Lancamento], _empresaId);
        _db.ChangeTracker.Clear();
    }

    private void FecharCaixaDoDiaDoLancamento()
    {
        var dia = HorarioBrasil.DataOperacional(DateTimeOffset.FromUnixTimeMilliseconds(_criadoEm).UtcDateTime);
        _db.Add(FechamentoCaixa.Criar(_empresaId, dia, 0, 0, 0, 0, 0, _lojaId));
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private CashEntry LancamentoSalvo() =>
        _db.Set<CashEntry>().AsNoTracking().Single(c => c.Id == Lancamento);

    private List<MovimentoCaixa> Movimentos() =>
        _db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.EmpresaId == _empresaId).ToList();
}
