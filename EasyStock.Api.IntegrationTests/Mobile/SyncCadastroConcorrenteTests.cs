using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.IntegrationTests.Mobile;

[Collection("MobileE2E")]
public sealed class SyncCadastroConcorrenteTests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task ProdutoDeOutraEmpresa_JaExistente_RecusaCom400ExplicitoSemAlterarDados()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaA, lojaA) = await fixture.SeedEmpresaELojaAsync();
        var (empresaB, lojaB) = await fixture.SeedEmpresaELojaAsync();
        var device = await fixture.SeedMobileDeviceAsync(empresaB, lojaB);
        var id = "foreign-" + Guid.NewGuid().ToString("N");
        var saldoId = id + "-saldo";
        using (var scope = fixture.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.AddRange(
                new Product { Id = id, Name = "Cadastro protegido", Stock = 7, EmpresaId = empresaA, LojaId = lojaA },
                new Product { Id = saldoId, Name = "Saldo local", Stock = 5, EmpresaId = empresaB, LojaId = lojaB });
            await db.SaveChangesAsync();
        }
        using var client = fixture.CreateMobileClient(device.ApiKey);
        var resposta = await client.PostAsJsonAsync("/api/mobile/sync", Requisicao(device.DeviceId, "product", id, saldoId, 3));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("Cadastro indisponivel para esta empresa.");
        using var check = fixture.Factory!.Services.CreateScope();
        var leitura = check.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var protegido = await leitura.Set<Product>().IgnoreQueryFilters().SingleAsync(p => p.Id == id);
        protegido.EmpresaId.Should().Be(empresaA);
        protegido.LojaId.Should().Be(lojaA);
        protegido.Name.Should().Be("Cadastro protegido");
        protegido.Stock.Should().Be(7);
        (await leitura.Set<Product>().IgnoreQueryFilters().SingleAsync(p => p.Id == saldoId)).Stock.Should().Be(5);
    }

    [SkippableFact]
    public async Task FalhaTransitoriaAposGravacaoParcial_ReverteOLoteSemReexecutarDispatcher()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var device = await fixture.SeedMobileDeviceAsync(empresa, loja);
        var id = "rollback-" + Guid.NewGuid().ToString("N");
        var aplicacoes = 0;
        await using var factory = fixture.Factory!.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddScoped<SyncMutationDispatcher>(sp =>
                new DispatcherQueGravaEFalha(sp.GetRequiredService<EasyStockDbContext>(), () => aplicacoes++))));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Mobile-Api-Key", device.ApiKey);
        var mutation = new MutationDto(Guid.NewGuid().ToString(), device.DeviceId, "product.upsert",
            JsonSerializer.SerializeToElement(new ProductDto(id, "Rollback", null, "extra", null, 1, 0, false), Json),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var resposta = await client.PostAsJsonAsync("/api/mobile/sync", new SyncPushRequest(device.DeviceId, [mutation]));

        resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        aplicacoes.Should().Be(1, "o dispatcher nao pode ser reaplicado automaticamente");
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.Set<Product>().IgnoreQueryFilters().AnyAsync(p => p.Id == id)).Should().BeFalse();
        (await db.MobileProcessedMutations.IgnoreQueryFilters().AnyAsync(m => m.MutationId == mutation.Id)).Should().BeFalse();
    }

    [SkippableTheory]
    [InlineData("product", true, false)]
    [InlineData("client", true, false)]
    [InlineData("product", false, false)]
    [InlineData("client", false, false)]
    [InlineData("product", true, true)]
    [InlineData("client", true, true)]
    public async Task CadastroSimultaneo_NaoDerrubaOutrasMutacoesNemDuplicaEstoqueNoReenvio(string tipo, bool mesmaEmpresa, bool mesmasMutacoes)
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaA, lojaA) = await fixture.SeedEmpresaELojaAsync();
        var (empresaB, lojaB) = await fixture.SeedEmpresaELojaAsync();
        if (mesmaEmpresa)
        {
            empresaB = empresaA;
            using var scope = fixture.Factory!.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var loja = new Loja { Id = Guid.NewGuid(), EmpresaId = empresaA, Nome = "Segunda loja da corrida", Ativa = true };
            db.Lojas.Add(loja);
            await db.SaveChangesAsync();
            lojaB = loja.Id;
        }
        var a = await fixture.SeedMobileDeviceAsync(empresaA, lojaA);
        var b = await fixture.SeedMobileDeviceAsync(empresaB, lojaB);
        var id = "conc-" + Guid.NewGuid().ToString("N");
        var saldoA = id + "-a";
        var saldoB = id + "-b";
        using (var scope = fixture.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.AddRange(
                new Product { Id = saldoA, Name = "Saldo A", Stock = 5, EmpresaId = empresaA, LojaId = lojaA },
                new Product { Id = saldoB, Name = "Saldo B", Stock = 5, EmpresaId = empresaB, LojaId = lojaB });
            await db.SaveChangesAsync();
        }

        var barreira = new EncontroAntesDeGravar(id);
        await using var factory = fixture.Factory!.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MobileSync:AutoLink:Product"] = "false", ["MobileSync:AutoLink:Client"] = "false"
            }));
            builder.ConfigureTestServices(services => services.ConfigureDbContext<EasyStockDbContext>((_, options) => options.AddInterceptors(barreira)));
        });
        using var clientA = factory.CreateClient();
        using var clientB = factory.CreateClient();
        clientA.DefaultRequestHeaders.Add("X-Mobile-Api-Key", a.ApiKey);
        clientB.DefaultRequestHeaders.Add("X-Mobile-Api-Key", b.ApiKey);
        var pedidoA = Requisicao(a.DeviceId, tipo, id, saldoA, 2);
        var pedidoB = mesmasMutacoes ? pedidoA with { DeviceId = b.DeviceId } : Requisicao(b.DeviceId, tipo, id, saldoB, 3);

        var respostas = await Task.WhenAll(
            clientA.PostAsJsonAsync("/api/mobile/sync", pedidoA),
            clientB.PostAsJsonAsync("/api/mobile/sync", pedidoB));
        if (mesmaEmpresa)
            respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
                "a corrida de cadastro dentro da empresa nao pode rejeitar o lote inteiro");
        else
            respostas.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest },
                "o Id global de outra empresa deve continuar rejeitado, sem alterar seu cadastro");

        foreach (var (resposta, pedido) in respostas.Zip(new[] { pedidoA, pedidoB }))
        {
            if (resposta.StatusCode != HttpStatusCode.OK)
            {
                (await resposta.Content.ReadAsStringAsync()).Should().Contain("Cadastro indisponivel para esta empresa.");
                continue;
            }
            var resultado = (await resposta.Content.ReadFromJsonAsync<SyncPushResponse>(Json))!;
            resultado.AcceptedIds.Should().BeEquivalentTo(pedido.Mutations.Select(m => m.Id),
                "o cadastro e todos os movimentos, inclusive o dependente, precisam ser aplicados");
            resultado.Rejected.Should().BeNullOrEmpty();
        }

        await ConferirAsync();
        // Reenvia os MESMOS MutationIds, inclusive o cadastro que perdeu a corrida.
        (await clientA.PostAsJsonAsync("/api/mobile/sync", pedidoA)).StatusCode.Should().Be(respostas[0].StatusCode);
        (await clientB.PostAsJsonAsync("/api/mobile/sync", pedidoB)).StatusCode.Should().Be(respostas[1].StatusCode);
        await ConferirAsync();

        async Task ConferirAsync()
        {
            using var scope = fixture.Factory!.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var saldos = await db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.Id == saldoA || p.Id == saldoB).ToDictionaryAsync(p => p.Id);
            saldos[saldoA].Stock.Should().Be(mesmasMutacoes || respostas[0].StatusCode == HttpStatusCode.OK ? 7 : 5);
            saldos[saldoA].EmpresaId.Should().Be(empresaA);
            saldos[saldoA].LojaId.Should().Be(lojaA);
            saldos[saldoB].Stock.Should().Be(!mesmasMutacoes && respostas[1].StatusCode == HttpStatusCode.OK ? 8 : 5);
            saldos[saldoB].EmpresaId.Should().Be(empresaB);
            saldos[saldoB].LojaId.Should().Be(lojaB);
            var registros = tipo == "product"
                ? await db.Set<Product>().IgnoreQueryFilters().Where(p => p.Id == id).Select(p => new { p.EmpresaId, p.LojaId }).ToListAsync()
                : await db.Set<Client>().IgnoreQueryFilters().Where(p => p.Id == id).Select(p => new { p.EmpresaId, p.LojaId }).ToListAsync();
            registros.Should().ContainSingle();
            var dono = registros.Single();
            ((dono.EmpresaId == empresaA && dono.LojaId == lojaA) || (dono.EmpresaId == empresaB && dono.LojaId == lojaB)).Should().BeTrue();
            if (!mesmaEmpresa)
            {
                dono.EmpresaId.Should().Be(respostas[0].StatusCode == HttpStatusCode.OK ? empresaA : empresaB);
                dono.LojaId.Should().Be(respostas[0].StatusCode == HttpStatusCode.OK ? lojaA : lojaB);
            }
            if (tipo == "product")
            {
                var saldoDependente = await db.Set<Product>().IgnoreQueryFilters().Where(p => p.Id == id).Select(p => p.Stock).SingleAsync();
                saldoDependente.Should().Be(mesmasMutacoes ? 2 : mesmaEmpresa ? 5 : respostas[0].StatusCode == HttpStatusCode.OK ? 2 : 3,
                    "a delta do produto criado no mesmo lote nao pode ser perdida nem duplicada");
            }
            var mutationIds = pedidoA.Mutations.Concat(pedidoB.Mutations).Select(m => m.Id).Distinct().ToList();
            var esperados = new[] { pedidoA, pedidoB }.Zip(respostas).Where(par => par.Second.StatusCode == HttpStatusCode.OK)
                .SelectMany(par => par.First.Mutations).Select(m => m.Id).Distinct().Count();
            (await db.MobileProcessedMutations.IgnoreQueryFilters().CountAsync(m => mutationIds.Contains(m.MutationId))).Should().Be(esperados);
        }
    }

    private static SyncPushRequest Requisicao(string device, string tipo, string id, string saldoId, int quantidade)
    {
        var agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        object cadastro = tipo == "product"
            ? new ProductDto(id, "Cadastro " + device, null, "extra", null, 10, quantidade, false, StockByDelta: true)
            : new ClientDto(id, "Cadastro " + device, null, null, null, agora, 0);
        var pedido = new SyncPushRequest(device,
        [
            new MutationDto(Guid.NewGuid().ToString(), device, tipo + ".upsert", JsonSerializer.SerializeToElement(cadastro, Json), agora),
            new MutationDto(Guid.NewGuid().ToString(), device, "stock.delta", JsonSerializer.SerializeToElement(new StockDeltaDto(Guid.NewGuid().ToString(), saldoId, quantidade), Json), agora)
        ]);
        if (tipo == "product")
            pedido.Mutations.Add(new MutationDto(Guid.NewGuid().ToString(), device, "stock.delta",
                JsonSerializer.SerializeToElement(new StockDeltaDto(Guid.NewGuid().ToString(), id, quantidade), Json), agora));
        return pedido;
    }

    private sealed class DispatcherQueGravaEFalha(EasyStockDbContext db, Action depoisDeGravar)
        : SyncMutationDispatcher(db, null!, null!, null!, null!, null!, null!, NullLogger<SyncMutationDispatcher>.Instance)
    {
        public override async Task ApplyMutationAsync(MutationDto mutation, string deviceId, string? operatorName, Guid? empresaId, Guid? lojaId)
        {
            db.Add(new Product { Id = mutation.Payload.GetProperty("id").GetString()!, Name = "Rollback", EmpresaId = empresaId, LojaId = lojaId });
            await db.SaveChangesAsync();
            depoisDeGravar();
            throw new TimeoutException("Falha sintetica depois do SaveChanges intermediario");
        }
    }

    // Mantem os dois lotes preparados antes do primeiro commit. O limite evita
    // bloquear uma implementacao que serialize o cadastro antes de chegar aqui.
    private sealed class EncontroAntesDeGravar(string id) : SaveChangesInterceptor
    {
        private int _chegadas;
        private readonly TaskCompletionSource _duas = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries().Any(e =>
                e.State == EntityState.Added && (e.Entity is Product p && p.Id == id || e.Entity is Client c && c.Id == id)
                || e.State == EntityState.Modified && e.Entity is Product saldo && (saldo.Id == id + "-a" || saldo.Id == id + "-b")))
            {
                if (Interlocked.Increment(ref _chegadas) >= 2) _duas.TrySetResult();
                await Task.WhenAny(_duas.Task, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
            }
            return result;
        }
    }
}
