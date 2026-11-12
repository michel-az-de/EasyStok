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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>#2321: CPF/CNPJ do app chega ao ERP (CLI-5) e a edicao do console chega ao app (CLI-3).</summary>
public sealed class ClienteDocumentoSyncTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();

    public ClienteDocumentoSyncTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"cliente-doc-{Guid.NewGuid()}").Options,
            currentUser);
    }

    public void Dispose() => _db.Dispose();

    private Cliente Erp(string nome, string? doc = null)
    {
        var c = Cliente.Criar(_empresaId, nome);
        c.Documento = doc;
        c.Telefone = "11988887777";
        _db.Add(c);
        return c;
    }

    private Client App(string id, string nome, Guid? erpId = null)
    {
        var c = new Client { Id = id, Name = nome, Phone = "11988887777", EmpresaId = _empresaId, LastOrder = DateTime.UtcNow, ErpClienteId = erpId };
        _db.Add(c);
        return c;
    }

    [Fact]
    public async Task EdicaoNoApp_ComDocumento_GravaNoClienteLigadoDoErp()
    {
        var erp = Erp("Maria");
        App("c-1", "Maria", erp.Id);
        await _db.SaveChangesAsync();

        await Dispatcher().ApplyMutationAsync(Mutacao("c-1", "123.456.789-09"), "dev-a", null, _empresaId, Guid.NewGuid());
        await _db.SaveChangesAsync();

        (await _db.Set<Cliente>().IgnoreQueryFilters().SingleAsync(c => c.Id == erp.Id))
            .Documento.Should().Be("12345678909");
    }

    [Fact]
    public async Task EdicaoNoApp_SemDocumento_NaoApagaODocumentoDoErp()
    {
        var erp = Erp("Maria", "12345678909");
        App("c-2", "Maria", erp.Id);
        await _db.SaveChangesAsync();

        await Dispatcher().ApplyMutationAsync(Mutacao("c-2", null), "dev-a", null, _empresaId, Guid.NewGuid());
        await _db.SaveChangesAsync();

        (await _db.Set<Cliente>().IgnoreQueryFilters().SingleAsync(c => c.Id == erp.Id))
            .Documento.Should().Be("12345678909");
    }

    [Fact]
    public async Task ClienteNovoDoApp_ComDocumento_NasceNoErpComODocumento()
    {
        App("c-3", "Joana");
        await _db.SaveChangesAsync();

        await new ClientLinker(_db, NullLogger<ClientLinker>.Instance).ExecuteAsync(
            ["c-3"], _empresaId, new Dictionary<string, string> { ["c-3"] = "123.456.789-09" });
        await _db.SaveChangesAsync();

        (await _db.Set<Cliente>().IgnoreQueryFilters().SingleAsync(c => c.EmpresaId == _empresaId))
            .Documento.Should().Be("12345678909");
    }

    [Fact]
    public async Task EdicaoNoConsole_ClienteEspelhado_ChegaAoApp_ComODocumento()
    {
        var erp = Erp("Maria Souza", "12345678909");
        erp.AlteradoEm = DateTime.UtcNow;
        App("c-4", "Maria", erp.Id);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await Pull().AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        var p = mutations.Single(m => m.Type == "client.upsert").Payload;
        p.GetProperty("id").GetString().Should().Be("c-4");
        p.GetProperty("name").GetString().Should().Be("Maria Souza");
        p.GetProperty("document").GetString().Should().Be("12345678909");
    }

    [Fact]
    public async Task EdicaoNoConsole_JaNoPullNormal_MesclaSemDuplicar()
    {
        var erp = Erp("Maria Souza");
        erp.AlteradoEm = DateTime.UtcNow;
        var app = App("c-5", "Maria", erp.Id);
        app.Apt = "Apto 3";
        await _db.SaveChangesAsync();
        var mutations = new List<MutationDto>
        {
            new("m1", "dev-b", "client.upsert",
                JsonSerializer.SerializeToElement(new ClientDto("c-5", "Maria", "Apto 3", null, "11988887777", 1, 1), JsonOpts), 1)
        };

        await Pull().AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        var unica = mutations.Where(m => m.Type == "client.upsert").Should().ContainSingle().Subject;
        unica.Payload.GetProperty("name").GetString().Should().Be("Maria Souza");
        unica.Payload.GetProperty("apt").GetString().Should().Be("Apto 3"); // vazio no ERP nao apaga
    }

    private SyncReversePullService Pull() => new(
        _db, new ConfigurationBuilder().Build(), NullLogger<SyncReversePullService>.Instance,
        new NumeroCurtoDosLotesMobile(_db, Substitute.For<ILoteNumeroCurtoRepository>(), NullLogger<NumeroCurtoDosLotesMobile>.Instance));

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

    private static MutationDto Mutacao(string id, string? documento)
    {
        var agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = JsonSerializer.SerializeToElement(new ClientDto(id, "Maria", null, null, "11988887777", agora, 1, documento), JsonOpts);
        return new MutationDto(Guid.NewGuid().ToString(), "dev-a", "client.upsert", payload, agora);
    }
}
