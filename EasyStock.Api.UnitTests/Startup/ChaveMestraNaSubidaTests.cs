using System.Security.Cryptography;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Integration;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Startup;

/// <summary>
/// I4.2 (#1708): em Production nenhum host sobe sem chave-mestra corrente, nem com linha ativa do cofre que a
/// chave conhecida não decifra. Fora de Production vale o aviso da I4.1. Toda KEK aqui é gerada no teste.
/// </summary>
public class ChaveMestraNaSubidaTests
{
    private sealed record TokenDeTeste(string Token);

    private static string KekNova() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static IConfiguration Config(string? atual, params (string Id, string Valor)[] keks)
    {
        var mapa = keks.ToDictionary(k => $"Crypto:Keks:{k.Id}", k => (string?)k.Valor);
        if (atual is not null) mapa["Crypto:CurrentKekId"] = atual;
        return new ConfigurationBuilder().AddInMemoryCollection(mapa).Build();
    }

    /// <summary>Linha ativa do cofre cifrada de verdade com a KEK <paramref name="kekId"/>.</summary>
    private static async Task<CredencialIntegracao> LinhaCifradaComAsync(string kekId, string kek)
    {
        CredencialIntegracao? gravada = null;
        var repo = Substitute.For<ICredencialIntegracaoRepository>();
        repo.When(r => r.AddAsync(Arg.Any<CredencialIntegracao>(), Arg.Any<CancellationToken>())).Do(c => gravada = c.Arg<CredencialIntegracao>());
        var resolver = new IntegrationCredentialResolver(repo, Substitute.For<IUnitOfWork>(), Config(kekId, (kekId, kek)),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<IntegrationCredentialResolver>.Instance);
        await resolver.SalvarAsync(Guid.NewGuid(), CategoriaIntegracao.Mensageria, "whatsapp", AmbienteIntegracao.Production,
            new TokenDeTeste("token-da-loja"), Guid.NewGuid());
        return gravada!;
    }

    private static ICredencialIntegracaoRepository RepoCom(params CredencialIntegracao[] ativas)
    {
        var repo = Substitute.For<ICredencialIntegracaoRepository>();
        repo.ListarAtivasAsync(Arg.Any<CancellationToken>()).Returns(ativas);
        return repo;
    }

    [Theory]
    [InlineData("Worker")]
    [InlineData("API")]
    public void Worker_Production_sem_KEK_nao_sobe(string host)
    {
        var act = () => ChaveMestraNaSubida.ExigirEmProducao(Config(atual: null), "Production", host);

        act.Should().Throw<ChaveMestraInvalidaException>()
            .Which.Message.Should().Contain(host).And.Contain(ChaveMestraConfiguracao.VarKekId);
    }

    [Fact]
    public void Production_com_KEK_invalida_nao_sobe_e_nao_escreve_o_valor()
    {
        var act = () => ChaveMestraNaSubida.ExigirEmProducao(Config("kek-2026-10", ("kek-2026-10", "nao-e-base64!!")), "Production", "API");

        act.Should().Throw<ChaveMestraInvalidaException>().Which.Message.Should().NotContain("nao-e-base64");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData(null)]
    public async Task Fora_de_Production_sobe_sem_KEK_e_nem_le_o_cofre(string? ambiente)
    {
        var repo = RepoCom();

        ChaveMestraNaSubida.ExigirEmProducao(Config(atual: null), ambiente, "API");
        await ChaveMestraNaSubida.ExigirLinhasAtivasLegiveisAsync(repo, Config(atual: null), ambiente, "API");

        await repo.DidNotReceive().ListarAtivasAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Startup_com_linha_ativa_de_kek_desconhecida_falha()
    {
        var antiga = KekNova();
        var linha = await LinhaCifradaComAsync("kek-2026-09", antiga);
        var soANova = Config("kek-2026-10", ("kek-2026-10", KekNova()));

        var act = () => ChaveMestraNaSubida.ExigirLinhasAtivasLegiveisAsync(RepoCom(linha), soANova, "Production", "Worker");

        var erro = (await act.Should().ThrowAsync<ChaveMestraInvalidaException>()).Which.Message;
        erro.Should().Contain("Worker").And.Contain("kek-2026-09").And.Contain(linha.Id.ToString());
        erro.Should().NotContain(antiga);
    }

    [Fact]
    public async Task Startup_com_kek_de_mesmo_id_e_outro_valor_falha()
    {
        // O id confere, o valor não: resolver a KEK não basta, tem de decifrar.
        var linha = await LinhaCifradaComAsync("kek-2026-10", KekNova());
        var mesmoIdOutraChave = Config("kek-2026-10", ("kek-2026-10", KekNova()));

        var act = () => ChaveMestraNaSubida.ExigirLinhasAtivasLegiveisAsync(RepoCom(linha), mesmoIdOutraChave, "Production", "API");

        (await act.Should().ThrowAsync<ChaveMestraInvalidaException>()).Which.Message.Should().Contain(linha.Id.ToString());
    }

    [Fact]
    public async Task Startup_com_todas_as_linhas_legiveis_sobe()
    {
        var atual = KekNova();
        var anterior = KekNova();
        var config = Config("kek-2026-10", ("kek-2026-10", atual), ("kek-2026-09", anterior));
        var repo = RepoCom(await LinhaCifradaComAsync("kek-2026-10", atual), await LinhaCifradaComAsync("kek-2026-09", anterior));

        await ChaveMestraNaSubida.ExigirLinhasAtivasLegiveisAsync(repo, config, "Production", "API");

        await repo.Received(1).ListarAtivasAsync(Arg.Any<CancellationToken>());
    }
}
