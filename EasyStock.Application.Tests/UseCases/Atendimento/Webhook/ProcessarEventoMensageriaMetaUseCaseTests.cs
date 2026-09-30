using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

/// <summary>
/// Webhook do Instagram e do Messenger (S35): roteia pelo destinatário, exige as flags, grava com
/// idempotência pelo mid e abre a conversa na fila humana.
/// </summary>
public class ProcessarEventoMensageriaMetaUseCaseTests
{
    private readonly Empresa _empresa = Empresa.Criar("Casa da Baba", null);
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly ITenantFeatureFlagRepository _flags = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ProcessarEventoMensageriaMetaUseCase _useCase;
    private readonly List<Conversa> _criadas = [];

    public ProcessarEventoMensageriaMetaUseCaseTests()
    {
        _empresa.VincularMensageriaMeta("PAGE-1", "1784");
        _empresas.GetByFacebookPageIdAsync("PAGE-1", Arg.Any<CancellationToken>()).Returns(_empresa);
        _empresas.GetByInstagramAccountIdAsync("1784", Arg.Any<CancellationToken>()).Returns(_empresa);
        _flags.ListarAtivasAsync(_empresa.Id, Arg.Any<CancellationToken>()).Returns(
            [FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalInstagram, FeatureCatalogo.CanalMessenger]);
        _conversas.AddAsync(Arg.Do<Conversa>(_criadas.Add), Arg.Any<CancellationToken>());
        _useCase = new ProcessarEventoMensageriaMetaUseCase(_empresas, _flags, _conversas, Substitute.For<IOperacaoEventPublisher>(),
            _tenant, _uow, NullLogger<ProcessarEventoMensageriaMetaUseCase>.Instance);
    }

    private static string Dm(string objeto, string destinatario, string remetente, string mid, string texto = "oi", bool eco = false) => $$$"""
        {"object":"{{{objeto}}}","entry":[{"id":"{{{destinatario}}}","messaging":[
          {"sender":{"id":"{{{remetente}}}"},"recipient":{"id":"{{{destinatario}}}"},"timestamp":1790000000000,
           "message":{"mid":"{{{mid}}}","text":"{{{texto}}}"{{{(eco ? ",\"is_echo\":true" : "")}}}}}]}]}
        """;

    [Fact]
    public async Task InstagramDm_AbreConversaInstagramComIgsidNaFilaHumana()
    {
        (await _useCase.ExecuteAsync(Dm("instagram", "1784", "IGSID-9", "ig-mid-1", "tem bolo?"))).Should().BeTrue();

        var conversa = _criadas.Should().ContainSingle().Subject;
        conversa.Canal.Should().Be(CanalConversa.Instagram);
        conversa.ContatoIdExterno.Should().Be("IGSID-9");
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        conversa.AssumidaPorUsuarioId.Should().BeNull();
        conversa.UltimaMensagemEntradaEm.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1790000000000).UtcDateTime);
        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.ExternoId == "ig-mid-1" && m.Texto == "tem bolo?" && m.Direcao == DirecaoMensagem.Entrada),
            Arg.Any<CancellationToken>());
        _tenant.Received().SetCurrentTenant(_empresa.Id);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task MessengerDm_AbreConversaMessengerComPsid()
    {
        await _useCase.ExecuteAsync(Dm("page", "PAGE-1", "PSID-3", "m_1"));

        var conversa = _criadas.Should().ContainSingle().Subject;
        conversa.Canal.Should().Be(CanalConversa.Messenger);
        conversa.ContatoIdExterno.Should().Be("PSID-3");
    }

    [Fact]
    public async Task ConversaAbertaDoContato_ReaproveitaSemAbrirOutra()
    {
        var aberta = Conversa.Abrir(_empresa.Id, "PSID-3", DateTime.UtcNow, canal: CanalConversa.Messenger);
        _conversas.ObterAbertaPorContatoAsync(_empresa.Id, CanalConversa.Messenger, "PSID-3", Arg.Any<CancellationToken>()).Returns(aberta);

        await _useCase.ExecuteAsync(Dm("page", "PAGE-1", "PSID-3", "m_2"));

        _criadas.Should().BeEmpty();
        aberta.NaoLidas.Should().Be(1);
    }

    [Fact]
    public async Task PaginaDesconhecida_NaoGravaNada()
    {
        (await _useCase.ExecuteAsync(Dm("page", "OUTRA-PAGINA", "PSID-3", "m_3"))).Should().BeTrue();

        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task FlagDoCanalDesligada_NaoGravaNada()
    {
        _flags.ListarAtivasAsync(_empresa.Id, Arg.Any<CancellationToken>()).Returns([FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalMessenger]);

        await _useCase.ExecuteAsync(Dm("instagram", "1784", "IGSID-9", "ig-mid-2"));

        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
    }

    [Fact]
    public async Task MidJaGravadoOuEco_Ignora()
    {
        _conversas.ObterMensagemPorExternoIdAsync(_empresa.Id, "m_dup", Arg.Any<CancellationToken>())
            .Returns(Mensagem.Entrada(_empresa.Id, Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Texto, "oi", "m_dup"));

        await _useCase.ExecuteAsync(Dm("page", "PAGE-1", "PSID-3", "m_dup"));
        await _useCase.ExecuteAsync(Dm("page", "PAGE-1", "PSID-3", "m_eco", eco: true));

        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
    }

    [Fact]
    public async Task FalhaAoGravar_PedeReenvioEDescarta()
    {
        _uow.CommitAsync().Returns<int>(_ => throw new InvalidOperationException("índice da conversa aberta"));

        (await _useCase.ExecuteAsync(Dm("page", "PAGE-1", "PSID-3", "m_4"))).Should().BeFalse();

        _uow.Received(1).DescartarAlteracoesPendentes();
    }
}
