using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Ia;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Ia;

/// <summary>
/// #3064 (S2b de #2948): rota, gasto do mês e cota de US$ 10 com Postgres real, pelo login sujeito a RLS (o papel da
/// produção). O HTTP do provedor e o cofre são falsos; banco, registro e soma são os reais.
/// </summary>
public class RoteadorIaIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    // Segunda, 11h UTC: fora do pico (fator 0,5).
    private static readonly DateTimeOffset Agora = new(2026, 11, 23, 11, 0, 0, TimeSpan.Zero);

    private ServiceProvider Provedor()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.RlsClientConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }

    /// <summary>Provedor ligado com orçamento, modelo padrão com preço DeepSeek flash e um modelo extra ligado.</summary>
    private async Task<(Guid Provedor, Guid Padrao, Guid Extra)> SemearLojaAsync(Guid empresa, decimal? orcamento)
    {
        var agora = Agora.UtcDateTime;
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        db.Empresas.Add(new Empresa { Id = empresa, Nome = "Loja IA", Documento = empresa.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
        var provedor = ProvedorIa.Criar(empresa, "DeepSeek", "https://api.deepseek.com/anthropic", AutenticacaoProvedorIa.XApiKey,
            orcamento, agora);
        var credencial = CredencialIntegracao.Criar(empresa, CategoriaIntegracao.Ia, provedor.ProviderKey, AmbienteIntegracao.Production,
            [1, 2, 3], "kek-teste", [4], [5], Guid.NewGuid());
        db.CredenciaisIntegracao.Add(credencial);
        provedor.VincularCredencial(credencial.Id, agora);
        provedor.Ligar(agora);
        db.ProvedoresIa.Add(provedor);
        var padrao = ModeloIa.Criar(empresa, provedor.Id, "deepseek-flash", "Flash", 0.30m, 1.20m, 0.006m, 0.5m, agora);
        padrao.Ligar(agora);
        padrao.TornarPadrao(agora);
        var extra = ModeloIa.Criar(empresa, provedor.Id, "deepseek-v4-pro", "Pro", 1.32m, 3.96m, 0.044m, 0.5m, agora);
        extra.Ligar(agora);
        db.ModelosIa.AddRange(padrao, extra);
        await db.SaveChangesAsync();
        return (provedor.Id, padrao.Id, extra.Id);
    }

    private static RoteadorIaLlmClient Roteador(IServiceProvider sp, Guid empresa, IClienteProvedorIa http)
    {
        var flags = Substitute.For<ITenantFeatureFlagRepository>();
        flags.ListarAtivasAsync(empresa, Arg.Any<CancellationToken>()).Returns([FeatureCatalogo.IaProvedorPorLoja]);
        var tenant = Substitute.For<ITenantAtual>();
        tenant.EmpresaId.Returns(empresa);
        var cofre = Substitute.For<IIntegrationCredentialResolver>();
        cofre.ObterAsync<ChaveProvedorIa>(empresa, Arg.Any<string>(), AmbienteIntegracao.Production, Arg.Any<CancellationToken>())
            .Returns(new ChaveProvedorIa("chave-falsa"));
        return new RoteadorIaLlmClient(Substitute.For<IAgenteLlmClient>(), flags, tenant, sp.GetRequiredService<IRotasIa>(), cofre, http,
            sp.GetRequiredService<IRegistroChamadaIa>(), new RelogioFixo(), NullLogger<RoteadorIaLlmClient>.Instance);
    }

    /// <summary>1M de entrada e 1M de saída no flash: (0,30 + 1,20) * 0,5 fora do pico = US$ 0,75 por chamada.</summary>
    private static IClienteProvedorIa HttpFalso()
    {
        var http = Substitute.For<IClienteProvedorIa>();
        http.EnviarAsync(Arg.Any<DestinoIa>(), Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [], 1_000_000, 1_000_000, 0, "deepseek-flash"));
        return http;
    }

    private static RequisicaoLlm Requisicao(Guid empresa, string? modelo = null) =>
        new("s", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], [], modelo)
        { EmpresaId = empresa, Finalidade = FinalidadeIa.Atendimento };

    [SkippableFact]
    public async Task OrcamentoDe10DolaresUsaAbaixoEstouraEGravaCotaEstourada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        var (provedor, padrao, _) = await SemearLojaAsync(empresa, 10m);
        await using var sp = Provedor();

        // 13 chamadas de US$ 0,75 = 9,75: abaixo do teto, todas saem. A 14ª começa com 9,75 < 10 e sai (cota suave),
        // levando a 10,50. A 15ª encontra o teto e não sai.
        for (var i = 0; i < 14; i++)
        {
            var roteador = Roteador(sp, empresa, HttpFalso());
            (await roteador.DisponivelAsync(empresa)).Should().BeTrue($"chamada {i + 1}");
            await roteador.EnviarAsync(Requisicao(empresa));
        }
        (await sp.GetRequiredService<IRotasIa>().GastoDoMesAsync(empresa, provedor, Agora.UtcDateTime)).Should().Be(10.50m);

        var estourado = Roteador(sp, empresa, HttpFalso());
        (await estourado.DisponivelAsync(empresa)).Should().BeFalse();
        await FluentActions.Awaiting(() => estourado.EnviarAsync(Requisicao(empresa)))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>();

        await using var db = fixture.CreateDbContext();
        var linhas = await db.ChamadasIa.IgnoreQueryFilters().Where(c => c.EmpresaId == empresa).ToListAsync();
        linhas.Should().HaveCount(15);
        linhas.Count(c => c.Desfecho == DesfechoChamadaIa.Ok).Should().Be(14);
        var cota = linhas.Single(c => c.Desfecho == DesfechoChamadaIa.CotaEstourada);
        (cota.ProvedorId, cota.ModeloId, cota.CustoUsd, cota.TokensEntrada).Should().Be((provedor, padrao, (decimal?)null, 0));
        linhas.Where(c => c.Desfecho == DesfechoChamadaIa.Ok).Should().OnlyContain(c => c.CustoUsd == 0.75m && c.ProvedorId == provedor);
    }

    [SkippableFact]
    public async Task ModeloPedidoCasaComIdExternoESemCasamentoCaiNoPadrao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        var (_, padrao, extra) = await SemearLojaAsync(empresa, null);
        await using var sp = Provedor();
        var rotas = sp.GetRequiredService<IRotasIa>();

        var pro = await rotas.ObterAsync(empresa, "deepseek-v4-pro");
        (pro!.Modelo.Id, pro.CaiuNoPadrao).Should().Be((extra, false));
        var velho = await rotas.ObterAsync(empresa, "claude-sonnet-5");
        (velho!.Modelo.Id, velho.CaiuNoPadrao).Should().Be((padrao, true));
        var semPedido = await rotas.ObterAsync(empresa, null);
        (semPedido!.Modelo.Id, semPedido.CaiuNoPadrao).Should().Be((padrao, false));
    }

    [SkippableFact]
    public async Task LeituraIsoladaUmaLojaNaoVeRotaNemGastoDaOutra()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        var (provedorA, _, _) = await SemearLojaAsync(empresaA, 10m);
        await using var sp = Provedor();
        await Roteador(sp, empresaA, HttpFalso()).EnviarAsync(Requisicao(empresaA));
        var rotas = sp.GetRequiredService<IRotasIa>();

        // Pelo repositório: a loja B não tem rota e o provedor da A não soma nada para a B.
        (await rotas.ObterAsync(empresaB, "deepseek-flash")).Should().BeNull();
        (await rotas.GastoDoMesAsync(empresaB, provedorA, Agora.UtcDateTime)).Should().Be(0m);
        (await rotas.GastoDoMesAsync(empresaA, provedorA, Agora.UtcDateTime)).Should().Be(0.75m);

        // Pelo login com RLS e o tenant da B, sem filtro de empresa na consulta: nada da A aparece.
        await using var escopo = sp.CreateAsyncScope();
        escopo.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresaB);
        var db = escopo.ServiceProvider.GetRequiredService<EasyStock.Infra.Postgre.Data.EasyStockDbContext>();
        (await db.ChamadasIa.IgnoreQueryFilters().CountAsync(c => c.EmpresaId == empresaA)).Should().Be(0);
        (await db.ProvedoresIa.IgnoreQueryFilters().CountAsync(p => p.EmpresaId == empresaA)).Should().Be(0);
        (await db.ModelosIa.IgnoreQueryFilters().CountAsync(m => m.EmpresaId == empresaA)).Should().Be(0);
        (await db.CredenciaisIntegracao.IgnoreQueryFilters().CountAsync(c => c.EmpresaId == empresaA)).Should().Be(0);
        escopo.ServiceProvider.GetRequiredService<ITenantAtual>().EmpresaId.Should().Be(empresaB);
    }

    private sealed class RelogioFixo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Agora;
    }
}
