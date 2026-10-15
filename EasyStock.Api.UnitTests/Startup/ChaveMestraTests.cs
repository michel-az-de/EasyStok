using System.Security.Cryptography;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Integration;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Startup;

/// <summary>
/// I4.1 (#1674): a chave-mestra (KEK) chega à API e ao Worker pelas variáveis <c>EZ_CRYPTO_*</c>, e a falta
/// dela só avisa: nenhum dos dois hosts deixa de subir. Toda KEK aqui é gerada no próprio teste.
/// </summary>
public class ChaveMestraTests
{
    private sealed record TokenDeTeste(string Token);

    private static string KekNova() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static Func<string, string?> Ambiente(params (string Nome, string Valor)[] vars)
    {
        var mapa = vars.ToDictionary(v => v.Nome, v => v.Valor);
        return nome => mapa.TryGetValue(nome, out var valor) ? valor : null;
    }

    private static IConfiguration SoComEz(params (string Nome, string Valor)[] vars) =>
        new ConfigurationBuilder().AddInMemoryCollection(ChaveMestraConfiguracao.MapearVariaveisEz(Ambiente(vars))).Build();

    private sealed class LogCapturado : ILogger
    {
        public List<(LogLevel Nivel, string Texto)> Linhas { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Linhas.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void Variaveis_EZ_viram_Crypto()
    {
        var atual = KekNova();
        var anterior = KekNova();

        var config = SoComEz(
            (ChaveMestraConfiguracao.VarKekId, " kek-2026-10 "), (ChaveMestraConfiguracao.VarKek, atual),
            (ChaveMestraConfiguracao.VarKekAnteriorId, "kek-2026-09"), (ChaveMestraConfiguracao.VarKekAnterior, anterior));

        config["Crypto:CurrentKekId"].Should().Be("kek-2026-10");
        config["Crypto:Keks:kek-2026-10"].Should().Be(atual);
        config["Crypto:Keks:kek-2026-09"].Should().Be(anterior);
    }

    [Fact]
    public void Sem_variaveis_nada_e_mapeado_e_vale_o_que_ja_estiver_em_Crypto()
    {
        ChaveMestraConfiguracao.MapearVariaveisEz(_ => null).Should().BeEmpty();
    }

    [Fact]
    public async Task Worker_so_com_EZ_resolve_e_decifra()
    {
        // O Worker não tem appsettings com Crypto:*: tudo o que ele sabe da KEK vem das variáveis EZ_CRYPTO_*.
        var config = SoComEz((ChaveMestraConfiguracao.VarKekId, "kek-2026-10"), (ChaveMestraConfiguracao.VarKek, KekNova()));
        var empresaId = Guid.NewGuid();
        CredencialIntegracao? gravada = null;
        var repo = Substitute.For<ICredencialIntegracaoRepository>();
        repo.When(r => r.AddAsync(Arg.Any<CredencialIntegracao>(), Arg.Any<CancellationToken>())).Do(c => gravada = c.Arg<CredencialIntegracao>());
        repo.GetAtivaAsync(empresaId, "whatsapp", AmbienteIntegracao.Production, Arg.Any<CancellationToken>()).Returns(_ => gravada);
        IntegrationCredentialResolver Resolver() => new(repo, Substitute.For<IUnitOfWork>(), config,
            new MemoryCache(new MemoryCacheOptions()), NullLogger<IntegrationCredentialResolver>.Instance);

        await Resolver().SalvarAsync(empresaId, CategoriaIntegracao.Mensageria, "whatsapp", AmbienteIntegracao.Production,
            new TokenDeTeste("token-da-loja"), Guid.NewGuid());
        var lido = await Resolver().ObterAsync<TokenDeTeste>(empresaId, "whatsapp", AmbienteIntegracao.Production);

        gravada.Should().NotBeNull();
        lido.Should().Be(new TokenDeTeste("token-da-loja"), "outra instância, sem cache, decifra só com a KEK das variáveis");
    }

    [Fact]
    public void Sem_KEK_sobe_e_avisa()
    {
        var log = new LogCapturado();

        var avisou = ChaveMestraConfiguracao.AvisarSeFaltar(new ConfigurationBuilder().Build(), log, "Worker");

        avisou.Should().BeTrue();
        var (nivel, texto) = log.Linhas.Should().ContainSingle().Subject;
        nivel.Should().Be(LogLevel.Warning, "só avisa: o host sobe");
        texto.Should().Contain("Worker").And.Contain(ChaveMestraConfiguracao.VarKekId).And.Contain(ChaveMestraConfiguracao.VarKek);
    }

    [Fact]
    public void Id_sem_valor_avisa_dizendo_qual_variavel_falta()
    {
        var log = new LogCapturado();

        ChaveMestraConfiguracao.AvisarSeFaltar(SoComEz((ChaveMestraConfiguracao.VarKekId, "kek-2026-10")), log, "API").Should().BeTrue();

        log.Linhas.Single().Texto.Should().Contain("kek-2026-10").And.Contain(ChaveMestraConfiguracao.VarKek);
    }

    [Theory]
    [InlineData("nao-e-base64!!")]
    [InlineData("AAAA")] // 3 bytes
    public void KEK_invalida_avisa_sem_escrever_o_valor(string valor)
    {
        var log = new LogCapturado();

        ChaveMestraConfiguracao.AvisarSeFaltar(
            SoComEz((ChaveMestraConfiguracao.VarKekId, "k1"), (ChaveMestraConfiguracao.VarKek, valor)), log, "API").Should().BeTrue();

        log.Linhas.Single().Texto.Should().Contain("32 bytes").And.NotContain(valor);
    }

    [Fact]
    public void KEK_valida_nao_avisa()
    {
        var log = new LogCapturado();

        ChaveMestraConfiguracao.AvisarSeFaltar(
            SoComEz((ChaveMestraConfiguracao.VarKekId, "k1"), (ChaveMestraConfiguracao.VarKek, KekNova())), log, "API").Should().BeFalse();

        log.Linhas.Should().BeEmpty();
    }
}