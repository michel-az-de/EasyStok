using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

public class ListarConversasAtendimentoUseCaseTests
{
    [Theory]
    [InlineData(null, 7)]
    [InlineData(2, 2)]
    public async Task InboxUsaMesmoPrazoDoAvaliadorEFiltraOutraEmpresa(int? configurado, int esperado)
    {
        var empresa = Guid.NewGuid();
        var repo = Substitute.For<IConversaRepository>();
        var config = Substitute.For<IConfiguracaoAtendimentoRepository>();
        var conversa = Conversa.Abrir(empresa, "5511999990001", DateTime.UtcNow);
        conversa.Assumir(DateTime.UtcNow, Guid.NewGuid());
        var alheia = Conversa.Abrir(Guid.NewGuid(), "5511999990002", DateTime.UtcNow);
        repo.ListarInboxAsync(default, default, default, default, default, default, default)
            .ReturnsForAnyArgs([ConversaInboxItem.De(conversa, null, true, "Ana"), ConversaInboxItem.De(alheia, null, true, "Pessoa de fora")]);
        if (configurado is { } minutos)
        {
            var salvo = ConfiguracaoAtendimento.CriarPadrao(empresa);
            salvo.SlaRespostaMinutos = minutos;
            config.GetByEmpresaIdAsync(empresa).Returns(salvo);
        }
        var caso = new ListarConversasAtendimentoUseCase(repo, config, Options.Create(new PrazosOptions { ClienteSemRespostaMin = 7 }));
        var resultado = await caso.ExecuteAsync(new ListarConversasAtendimentoQuery(empresa, null, null, 1, 10));
        var resumo = resultado.Should().ContainSingle().Subject;
        resumo.Id.Should().Be(conversa.Id);
        resumo.SlaRespostaMinutos.Should().Be(esperado);
        resumo.AguardaResposta.Should().BeTrue();
        resumo.AssumidaPorNome.Should().Be("Ana");
        resumo.AssumidaPorUsuarioId.Should().Be(conversa.AssumidaPorUsuarioId);
    }

    [Fact]
    public async Task InboxLevaOAssuntoDoEmailAoResumo()
    {
        // #1432 sobre o #1583: a inbox virou projeção, e o assunto do e-mail tem de atravessar a linha até o cartão.
        var empresa = Guid.NewGuid();
        var repo = Substitute.For<IConversaRepository>();
        var email = Conversa.Abrir(empresa, "maria@exemplo.com", DateTime.UtcNow, canal: CanalConversa.Email);
        email.DefinirAssunto("Encomenda de bolo");
        var whatsapp = Conversa.Abrir(empresa, "5511999990001", DateTime.UtcNow);
        repo.ListarInboxAsync(default, default, default, default, default, default, default)
            .ReturnsForAnyArgs([ConversaInboxItem.De(email, null), ConversaInboxItem.De(whatsapp, null)]);
        var caso = new ListarConversasAtendimentoUseCase(
            repo, Substitute.For<IConfiguracaoAtendimentoRepository>(), Options.Create(new PrazosOptions { ClienteSemRespostaMin = 7 }));

        var resultado = await caso.ExecuteAsync(new ListarConversasAtendimentoQuery(empresa, null, null, 1, 10));

        resultado.Single(r => r.Id == email.Id).Assunto.Should().Be("Encomenda de bolo");
        resultado.Single(r => r.Id == whatsapp.Id).Assunto.Should().BeNull("assunto é só do canal E-mail");
    }

    private static (ListarConversasAtendimentoUseCase Caso, IConfiguracaoAtendimentoRepository Config) Inbox(
        ICacheService cache, params (Guid Empresa, int Sla)[] configuradas)
    {
        var repo = Substitute.For<IConversaRepository>();
        repo.ListarInboxAsync(default, default, default, default, default, default, default)
            .ReturnsForAnyArgs(ci => [ConversaInboxItem.De(Conversa.Abrir(ci.ArgAt<Guid>(0), "5511999990001", DateTime.UtcNow), null)]);
        var config = Substitute.For<IConfiguracaoAtendimentoRepository>();
        foreach (var (empresa, sla) in configuradas)
        {
            var salvo = ConfiguracaoAtendimento.CriarPadrao(empresa);
            salvo.SlaRespostaMinutos = sla;
            config.GetByEmpresaIdAsync(empresa).Returns(salvo);
        }
        return (new ListarConversasAtendimentoUseCase(repo, config, Options.Create(new PrazosOptions { ClienteSemRespostaMin = 7 }), cache), config);
    }

    private static async Task<int> SlaDaInboxAsync(ListarConversasAtendimentoUseCase caso, Guid empresa) =>
        (await caso.ExecuteAsync(new ListarConversasAtendimentoQuery(empresa, null, null, 1, 10))).Single().SlaRespostaMinutos;

    [Fact]
    public async Task PollingDaInboxNaoReleAConfiguracaoEACadaEmpresaTemASuaChave()
    {
        // #1566: a inbox faz polling a cada 5 s e lia a configuração do banco em todo GET só para repetir o SLA.
        var empresa = Guid.NewGuid();
        var outra = Guid.NewGuid();
        var (caso, config) = Inbox(new CacheEmMemoria(), (empresa, 2), (outra, 9));

        (await SlaDaInboxAsync(caso, empresa)).Should().Be(2);
        (await SlaDaInboxAsync(caso, empresa)).Should().Be(2);
        (await SlaDaInboxAsync(caso, outra)).Should().Be(9, "a chave do cache leva a empresa");

        await config.Received(1).GetByEmpresaIdAsync(empresa);
        await config.Received(1).GetByEmpresaIdAsync(outra);
    }

    [Fact]
    public async Task SalvarAConfiguracaoInvalidaOSlaEmCache()
    {
        var empresa = Guid.NewGuid();
        var cache = new CacheEmMemoria();
        var (caso, config) = Inbox(cache, (empresa, 2));
        (await SlaDaInboxAsync(caso, empresa)).Should().Be(2);

        await new AtualizarConfiguracaoAtendimentoUseCase(config, Substitute.For<IUnitOfWork>(), cache).ExecuteAsync(NovoSla(empresa, 4));

        (await SlaDaInboxAsync(caso, empresa)).Should().Be(4, "a dona não espera o TTL para ver o prazo que acabou de salvar");
    }

    private static AtualizarConfiguracaoAtendimentoCommand NovoSla(Guid empresa, int minutos) =>
        new(empresa, null, null, null, null, null, null, null, null, null, SlaRespostaMinutos: minutos);

    [Fact]
    public async Task CacheForaDoArNaoDerrubaOSalvarDaConfiguracao()
    {
        // A configuração já foi persistida quando o cache é invalidado: a falha dele vira aviso e o TTL cobre.
        var empresa = Guid.NewGuid();
        var uow = Substitute.For<IUnitOfWork>();
        var caso = new AtualizarConfiguracaoAtendimentoUseCase(
            Substitute.For<IConfiguracaoAtendimentoRepository>(), uow, new CacheEmMemoria { ForaDoAr = true });

        var salvo = await caso.ExecuteAsync(NovoSla(empresa, 4));

        salvo.SlaRespostaMinutos.Should().Be(4);
        await uow.ReceivedWithAnyArgs(1).CommitAsync();
    }

    [Fact]
    public async Task CancelamentoNaInvalidacaoNaoViraFalhaDeCache()
    {
        var cache = Substitute.For<ICacheService>();
        cache.RemoveAsync(Arg.Any<string>()).Returns<Task>(_ => throw new OperationCanceledException());
        var caso = new AtualizarConfiguracaoAtendimentoUseCase(
            Substitute.For<IConfiguracaoAtendimentoRepository>(), Substitute.For<IUnitOfWork>(), cache);

        var act = () => caso.ExecuteAsync(NovoSla(Guid.NewGuid(), 4));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CacheForaDoArNaoDerrubaAInbox()
    {
        var empresa = Guid.NewGuid();
        var (caso, config) = Inbox(new CacheEmMemoria { ForaDoAr = true }, (empresa, 2));

        (await SlaDaInboxAsync(caso, empresa)).Should().Be(2);
        (await SlaDaInboxAsync(caso, empresa)).Should().Be(2);

        await config.Received(2).GetByEmpresaIdAsync(empresa);
    }

    private sealed class CacheEmMemoria : ICacheService
    {
        private readonly Dictionary<string, object?> _itens = [];
        public bool ForaDoAr { get; init; }

        private void Conferir() { if (ForaDoAr) throw new InvalidOperationException("cache indisponivel"); }

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) { Conferir(); _itens[key] = value; return Task.CompletedTask; }
        public Task<T?> GetAsync<T>(string key) { Conferir(); return Task.FromResult(_itens.TryGetValue(key, out var v) && v is T t ? t : default); }
        public Task RemoveAsync(string key) { Conferir(); _itens.Remove(key); return Task.CompletedTask; }
        public Task RemoveAsync(IEnumerable<string> keys) { Conferir(); foreach (var k in keys) _itens.Remove(k); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(string key) => Task.FromResult(_itens.ContainsKey(key));
        public Task<long> IncrementAsync(string key, long value = 1) => throw new NotSupportedException();
        public Task SetExpiryAsync(string key, TimeSpan ttl) => Task.CompletedTask;
    }
}
