using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.ChatSite;

/// <summary>#2876: o formulário do site cria o lead; telefone de cadastro existente vai para conferência da equipe.</summary>
public class ChatSiteIdentificacaoClienteTests
{
    private const string Slug = "casa-da-baba";
    private readonly StorefrontEntity _loja;
    private readonly ISessaoChatSiteRepository _sessoes = Substitute.For<ISessaoChatSiteRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IConsentimentoContatoRepository _consentimentos = Substitute.For<IConsentimentoContatoRepository>();
    private readonly IEscaladorConversa _escalador = Substitute.For<IEscaladorConversa>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly List<Conversa> _criadas = [];
    private readonly AcessoChatSite _acesso;

    public ChatSiteIdentificacaoClienteTests()
    {
        _loja = StorefrontEntity.Criar(empresaId: Guid.NewGuid(), slug: Slug, tituloPublico: "Casa da Baba", pedidoMinimoEntrega: 0m);
        _loja.Ativar();
        var lojas = Substitute.For<IStorefrontRepository>();
        lojas.GetBySlugAsync(Slug, Arg.Any<CancellationToken>()).Returns(_loja);
        var flags = Substitute.For<ITenantFeatureFlagRepository>();
        flags.ListarAtivasAsync(_loja.EmpresaId, Arg.Any<CancellationToken>())
            .Returns([FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalChatSite]);
        _acesso = new AcessoChatSite(lojas, flags, Substitute.For<ITenantContextAccessor>(), _sessoes);
        _sessoes.ObterSnapshotPorTokenHashAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => _sessoes.ObterPorTokenHashAsync(c.Arg<Guid>(), c.Arg<string>(), c.Arg<CancellationToken>()));
        _conversas.When(x => x.AddAsync(Arg.Any<Conversa>(), Arg.Any<CancellationToken>())).Do(ci => _criadas.Add(ci.Arg<Conversa>()));
        _conversas.ObterPorIdComLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => _criadas.FirstOrDefault(c => c.Id == ci.ArgAt<Guid>(1)));
        _uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<Conversa>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<Conversa>>>()(ci.Arg<CancellationToken>()));
        _escalador.When(e => e.EscalarAsync(Arg.Any<Guid>(), Arg.Any<Conversa>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
            .Do(ci => ci.Arg<Conversa>().Escalar(ci.Arg<DateTime>(), ci.Arg<string>()));
    }

    private string Sessao()
    {
        var token = AcessoChatSite.NovoToken();
        var sessao = SessaoChatSite.Abrir(_loja.EmpresaId, _loja.Id, AcessoChatSite.HashDoToken(token), DateTime.UtcNow);
        _sessoes.ObterPorTokenHashAsync(_loja.EmpresaId, AcessoChatSite.HashDoToken(token), Arg.Any<CancellationToken>()).Returns(sessao);
        return token;
    }

    private IdentificarVisitanteChatSiteUseCase Sut() => new(_acesso, _conversas, _uow, Substitute.For<IOperacaoEventPublisher>(),
        new ConversaChatSiteService(_conversas, _uow, _sessoes),
        new IdentificarClientePorTelefoneUseCase(_clientes, Substitute.For<IClienteStorefrontRepository>(),
            Substitute.For<IVinculoClienteConversaRepository>(), NullLogger<IdentificarClientePorTelefoneUseCase>.Instance),
        _consentimentos, _escalador, _notificador);

    private static IdentificacaoVisitanteInput Formulario() => new("Maria Souza", "(11) 98765-4321", "maria@exemplo.com", true);

    [Fact]
    public async Task TelefoneNovo_CriaLeadLigaConversaRegistraAceiteEAvisaEquipe()
    {
        var token = Sessao();

        await Sut().ExecuteAsync(Slug, token, Formulario());

        var conversa = _criadas.Should().ContainSingle().Subject;
        await _clientes.Received(1).AddAsync(Arg.Is<ClienteEntity>(c =>
            c.Nome == "Maria Souza" && c.Email == "maria@exemplo.com" && c.Telefone == "+5511987654321"));
        conversa.ClienteId.Should().NotBeNull();
        conversa.VinculoEmRevisao.Should().BeFalse();
        await _consentimentos.Received(1).AddAsync(Arg.Is<ConsentimentoContato>(c =>
            c.Canal == CanalConversa.ChatSite && c.Origem == CadastrarClienteDaConversaUseCase.OrigemAceiteChatSite),
            Arg.Any<CancellationToken>());
        await _notificador.ReceivedWithAnyArgs().EnfileirarEventoAsync(default, default, default!, default, default, default);
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task TelefoneDeCadastroExistente_NaoLigaClienteExigeConferenciaEEscala()
    {
        var existente = ClienteEntity.Criar(_loja.EmpresaId, "Outra Pessoa");
        existente.Telefone = "+5511987654321";
        _clientes.FindByTelefoneAsync(_loja.EmpresaId, "+5511987654321").Returns(existente);
        var token = Sessao();

        await Sut().ExecuteAsync(Slug, token, Formulario());

        var conversa = _criadas.Should().ContainSingle().Subject;
        conversa.ClienteId.Should().BeNull("o telefone digitado não prova quem é o visitante");
        conversa.VinculoEmRevisao.Should().BeTrue();
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        await _escalador.Received(1).EscalarAsync(_loja.EmpresaId, conversa, IdentificarVisitanteChatSiteUseCase.MotivoConferencia,
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _clientes.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _consentimentos.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
