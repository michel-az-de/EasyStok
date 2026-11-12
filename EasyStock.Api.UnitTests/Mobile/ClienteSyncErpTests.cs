using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>#2314: vínculo (CLI-7, CLI-6) e edição (CLI-2) do cliente do app com o Cliente do ERP.</summary>
public sealed class ClienteSyncErpTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();

    public ClienteSyncErpTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"cliente-sync-{Guid.NewGuid()}").Options,
            currentUser);
    }

    public void Dispose() => _db.Dispose();

    private Cliente Erp(string nome, string? telefone, Guid? empresa = null)
    {
        var c = Cliente.Criar(empresa ?? _empresaId, nome);
        c.Telefone = telefone;
        _db.Add(c);
        return c;
    }

    private Client App(string id, string nome, string? telefone)
    {
        var c = new Client { Id = id, Name = nome, Phone = telefone, EmpresaId = _empresaId, LastOrder = DateTime.UtcNow };
        _db.Add(c);
        return c;
    }

    private async Task Vincular(params string[] ids)
    {
        await _db.SaveChangesAsync();
        await new ClientLinker(_db, NullLogger<ClientLinker>.Instance).ExecuteAsync(ids, _empresaId);
        await _db.SaveChangesAsync();
    }

    private Task<int> TotalErp()
        => _db.Set<Cliente>().IgnoreQueryFilters().CountAsync(c => c.EmpresaId == _empresaId);

    [Fact]
    public async Task MesmoNome_TelefonesDiferentes_NaoFunde()
    {
        var maria1 = Erp("Maria Silva", "11988887777");
        var app = App("c-2", "Maria Silva", "11911112222");
        await Vincular("c-2");

        app.ErpClienteId.Should().NotBeNull().And.NotBe(maria1.Id);
        (await TotalErp()).Should().Be(2);
    }

    [Theory]
    [InlineData("(11) 98888-7777")]
    [InlineData("+55 11 98888-7777")]
    [InlineData("5511988887777")]
    public async Task MesmoTelefone_FormatosDiferentes_FundeNoMesmoCliente(string telefoneDoApp)
    {
        var erp = Erp("Maria Silva", "11988887777");
        var app = App("c-1", "Maria S.", telefoneDoApp);
        await Vincular("c-1");

        app.ErpClienteId.Should().Be(erp.Id);
        (await TotalErp()).Should().Be(1);
    }

    [Fact]
    public async Task SemTelefoneNosDois_MesmoNome_Funde()
    {
        var erp = Erp("Joana", null);
        var app = App("c-3", "joana", null);
        await Vincular("c-3");

        app.ErpClienteId.Should().Be(erp.Id);
    }

    [Fact]
    public async Task AppSemTelefone_ErpComTelefone_MesmoNome_NaoFunde()
    {
        var erp = Erp("Joana", "11977776666");
        var app = App("c-4", "Joana", null);
        await Vincular("c-4");

        app.ErpClienteId.Should().NotBeNull().And.NotBe(erp.Id);
    }

    [Fact]
    public async Task EdicaoNoApp_ClienteLigado_AtualizaOClienteDoErp()
    {
        var erp = Erp("Maria Silva", "11988887777");
        var app = App("c-5", "Maria Silva", "11988887777");
        app.ErpClienteId = erp.Id;
        var outraEmpresa = Erp("Maria Silva", "11988887777", Guid.NewGuid());
        await _db.SaveChangesAsync();

        await Dispatcher().ApplyMutationAsync(Mutacao("c-5", "Maria Souza", "(11) 95555-4444", "Apto 12", "Rua B, 20"),
            "dev-a", null, _empresaId, Guid.NewGuid());
        await _db.SaveChangesAsync();

        var atual = await _db.Set<Cliente>().IgnoreQueryFilters().SingleAsync(c => c.Id == erp.Id);
        atual.Nome.Should().Be("Maria Souza");
        atual.Telefone.Should().Be("(11) 95555-4444");
        atual.Apt.Should().Be("Apto 12");
        atual.Endereco.Should().Be("Rua B, 20");
        var outra = await _db.Set<Cliente>().IgnoreQueryFilters().SingleAsync(c => c.Id == outraEmpresa.Id);
        outra.Nome.Should().Be("Maria Silva");
    }

    private SyncMutationDispatcher Dispatcher()
        => new(
            _db,
            new MobileStockReconciler(_db, new MobileSystemUserResolver(_db), NullLogger<MobileStockReconciler>.Instance),
            new LoteMobileEstadoReconciler(_db, NullLogger<LoteMobileEstadoReconciler>.Instance),
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance),
            Substitute.For<IProdutoRepository>(),
            DispatcherDeTeste.EstornoDeCaixa(_db),
            NullLogger<SyncMutationDispatcher>.Instance);

    private static MutationDto Mutacao(string id, string nome, string? fone, string? apt, string? end)
    {
        var agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = JsonSerializer.SerializeToElement(new ClientDto(id, nome, apt, end, fone, agora, 1));
        return new MutationDto(Guid.NewGuid().ToString(), "dev-a", "client.upsert", payload, agora);
    }
}
