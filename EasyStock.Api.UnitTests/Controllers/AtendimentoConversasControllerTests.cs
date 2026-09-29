using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Handoff pelo console (S07). Use cases reais sobre um repositório em memória; o canal é substituto
/// (nenhuma chamada à Meta).
/// </summary>
public class AtendimentoConversasControllerTests
{
    private const string WaId = "5511999998888";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly ConversaRepositoryEmMemoria _repositorio = new();
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly AtendimentoConversasController _controller;

    public AtendimentoConversasControllerTests()
    {
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.UsuarioId.Returns(_usuarioId);
        _canal.Canal.Returns(CanalConversa.WhatsApp);
        _canal.EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.dona1");

        var resolvedor = new ResolvedorCanal([_canal]);
        var uploads = new GerenciarUploadsUseCase(
            Substitute.For<IFileStorage>(), Substitute.For<IImageProcessor>(), Substitute.For<IProdutoRepository>(),
            Substitute.For<IUsuarioRepository>(), Substitute.For<ILojaRepository>(), Substitute.For<IStorefrontRepository>(),
            Substitute.For<ICardapioItemRepository>(), _unitOfWork);

        _controller = new AtendimentoConversasController(
            new ListarConversasAtendimentoUseCase(_repositorio),
            new ListarMensagensConversaUseCase(_repositorio),
            new EnviarMensagemConsoleUseCase(_repositorio, resolvedor, uploads, _unitOfWork),
            new GerenciarConversaAtendimentoUseCase(_repositorio, _unitOfWork),
            _currentUser);
    }

    private Conversa ConversaComClienteAgora(DateTime? ultimaEntrada = null)
    {
        var entrada = ultimaEntrada ?? DateTime.UtcNow.AddMinutes(-5);
        var conversa = Conversa.Abrir(_empresaId, WaId, entrada.AddMinutes(-1), "Maria");
        conversa.RegistrarEntrada(entrada);
        _repositorio.Conversas.Add(conversa);
        _repositorio.Mensagens.Add(Mensagem.Entrada(_empresaId, conversa.Id, entrada, TipoConteudoMensagem.Texto, "oi", "wamid.in1"));
        return conversa;
    }

    [Fact]
    public async Task EnviarMarcaAssumida()
    {
        var conversa = ConversaComClienteAgora();

        var result = await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("Oi Maria, é a Baba!"), default);

        result.Should().BeOfType<OkObjectResult>();
        await _canal.Received(1).EnviarTextoAsync(WaId, "Oi Maria, é a Baba!", Arg.Any<CancellationToken>());
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        conversa.AssumidaPorUsuarioId.Should().Be(_usuarioId);
        _repositorio.Mensagens.Should().ContainSingle(m => m.Autor == AutorMensagem.Dona
            && m.Direcao == DirecaoMensagem.Saida && m.ExternoId == "wamid.dona1" && m.Status == StatusMensagem.Enviada);
        await _unitOfWork.Received(1).CommitAsync();

        // A mensagem seguinte do cliente não aciona o agente (RN-04).
        var agora = DateTime.UtcNow;
        conversa.RegistrarEntrada(agora);
        _repositorio.Mensagens.Add(Mensagem.Entrada(_empresaId, conversa.Id, agora, TipoConteudoMensagem.Texto, "e aí?", "wamid.in2"));
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.Disponivel.Returns(true);
        var agente = new AgenteAtendimentoService(
            llm, _repositorio, Substitute.For<IConfiguracaoAtendimentoRepository>(), Substitute.For<IClienteRepository>(),
            Array.Empty<IFerramentaAgente>(), Substitute.For<IEscaladorConversa>(), Substitute.For<IWhatsAppCloudClient>(),
            Substitute.For<IUsoIaRepository>(), _unitOfWork, NullLogger<AgenteAtendimentoService>.Instance);

        var turno = await agente.ProcessarTurnoAsync(_empresaId, conversa.Id, agora);

        turno.ChamouLlm.Should().BeFalse();
        turno.Respondeu.Should().BeFalse();
        await llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task ForaDaJanela409()
    {
        var conversa = ConversaComClienteAgora(DateTime.UtcNow.AddHours(-25));

        var result = await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("Oi?"), default);

        var conflito = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflito.Value.Should().BeEquivalentTo(new { erro = "fora_da_janela_24h", sugestao = "template" });
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _repositorio.Mensagens.Should().NotContain(m => m.Direcao == DirecaoMensagem.Saida);
        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Meta131047Devolve409SemGravar()
    {
        var conversa = ConversaComClienteAgora();
        _canal.EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new WhatsAppCloudException(131047, "janela", ehPermanente: true));

        var result = await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("Oi?"), default);

        result.Should().BeOfType<ConflictObjectResult>()
            .Which.Value.Should().BeEquivalentTo(new { erro = "fora_da_janela_24h", sugestao = "template" });
        _repositorio.Mensagens.Should().NotContain(m => m.Direcao == DirecaoMensagem.Saida);
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task LiberarVoltaAutomatica()
    {
        var conversa = ConversaComClienteAgora();
        conversa.Assumir(DateTime.UtcNow, _usuarioId);

        var result = await _controller.LiberarAutomatico(conversa.Id, default);

        result.Should().BeOfType<OkObjectResult>();
        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        conversa.AssumidaPorUsuarioId.Should().BeNull();
        _repositorio.Mensagens.Should().ContainSingle(m => m.Autor == AutorMensagem.Sistema
            && m.ExternoId == null && m.Texto!.Contains(_usuarioId.ToString()));
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task EncerrarPermiteNovaConversa()
    {
        var conversa = ConversaComClienteAgora();

        var result = await _controller.Encerrar(conversa.Id, default);

        result.Should().BeOfType<OkObjectResult>();
        conversa.Situacao.Should().Be(SituacaoConversa.Encerrada);
        conversa.EncerradaEm.Should().NotBeNull();
        await _unitOfWork.Received(1).CommitAsync();
        // Sem conversa aberta para o contato, a próxima mensagem abre outra Automatica (S05, webhook).
        (await _repositorio.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, WaId)).Should().BeNull();
    }

    [Fact]
    public async Task AssumirSemEnviarSuspendeAgente()
    {
        var conversa = ConversaComClienteAgora();

        var result = await _controller.Assumir(conversa.Id, default);

        result.Should().BeOfType<OkObjectResult>();
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        conversa.AssumidaPorUsuarioId.Should().Be(_usuarioId);
    }

    [Fact]
    public async Task MarcarLidaZeraNaoLidas()
    {
        var conversa = ConversaComClienteAgora();
        conversa.NaoLidas.Should().Be(1);

        var result = await _controller.MarcarLida(conversa.Id, default);

        result.Should().BeOfType<OkObjectResult>();
        conversa.NaoLidas.Should().Be(0);
    }

    [Fact]
    public async Task ConversaDeOutraEmpresaDevolve404()
    {
        var outra = Conversa.Abrir(Guid.NewGuid(), WaId, DateTime.UtcNow, "Zé");
        _repositorio.Conversas.Add(outra);

        var result = await _controller.EnviarMensagem(outra.Id, new EnviarMensagemConsoleBody("oi"), default);

        result.Should().BeOfType<NotFoundObjectResult>();
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task EnviarEmConversaEncerradaDevolve409()
    {
        var conversa = ConversaComClienteAgora();
        conversa.Encerrar(DateTime.UtcNow);

        var result = await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("oi"), default);

        result.Should().BeOfType<ConflictObjectResult>();
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task ListarDevolveUltimaMensagem()
    {
        var conversa = ConversaComClienteAgora();

        var result = await _controller.Listar(null, "mar", 1, 20, default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var data = (IReadOnlyList<ConversaResumoResult>)ok.Value!.GetType().GetProperty("Data")!.GetValue(ok.Value)!;
        data.Should().ContainSingle(c => c.Id == conversa.Id && c.UltimaMensagemTexto == "oi" && c.NaoLidas == 1);
    }

    /// <summary>Repositório em memória: basta para os use cases e para o turno do agente.</summary>
    private sealed class ConversaRepositoryEmMemoria : IConversaRepository
    {
        public List<Conversa> Conversas { get; } = [];
        public List<Mensagem> Mensagens { get; } = [];

        public Task<Conversa?> ObterPorIdAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Conversas.FirstOrDefault(c => c.EmpresaId == empresaId && c.Id == id));

        public Task<Conversa?> ObterAbertaPorContatoAsync(Guid empresaId, CanalConversa canal, string contatoIdExterno, CancellationToken ct = default)
        {
            var contato = Conversa.NormalizarContato(canal, contatoIdExterno);
            return Task.FromResult(Conversas.FirstOrDefault(c =>
                c.EmpresaId == empresaId && c.Canal == canal && c.ContatoIdExterno == contato && c.EstaAberta));
        }

        public async Task<ConversaComMensagens?> ObterComMensagensAsync(Guid empresaId, Guid id, int ultimasN, CancellationToken ct = default)
        {
            var conversa = await ObterPorIdAsync(empresaId, id, ct);
            return conversa is null
                ? null
                : new ConversaComMensagens(conversa, Mensagens.Where(m => m.ConversaId == id).OrderBy(m => m.EnviadaEm).TakeLast(ultimasN).ToList());
        }

        public Task<IReadOnlyList<Conversa>> ListarAsync(Guid empresaId, SituacaoConversa? situacao, int pagina, int tamanhoPagina, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Conversa>>(Conversas.Where(c => c.EmpresaId == empresaId).ToList());

        public Task<IReadOnlyList<ConversaInboxItem>> ListarInboxAsync(
            Guid empresaId, SituacaoConversa? situacao, string? busca, int pagina, int tamanhoPagina, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConversaInboxItem>>(Conversas
                .Where(c => c.EmpresaId == empresaId && (situacao is null || c.Situacao == situacao))
                .Where(c => busca is null || (c.ContatoNome ?? "").Contains(busca, StringComparison.OrdinalIgnoreCase) || c.ContatoIdExterno.Contains(busca))
                .Select(c => new ConversaInboxItem(c, Mensagens.Where(m => m.ConversaId == c.Id).OrderBy(m => m.EnviadaEm).LastOrDefault()?.Texto))
                .ToList());

        public Task<IReadOnlyList<Mensagem>> ListarMensagensAsync(
            Guid empresaId, Guid conversaId, DateTime? antesDe, int limite, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Mensagem>>(Mensagens
                .Where(m => m.EmpresaId == empresaId && m.ConversaId == conversaId && (antesDe is null || m.EnviadaEm < antesDe))
                .OrderBy(m => m.EnviadaEm).TakeLast(limite).ToList());

        public Task<IReadOnlyList<Mensagem>> ListarMensagensDepoisAsync(
            Guid empresaId, Guid conversaId, DateTime? depoisDe, int limite, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Mensagem>>(Mensagens
                .Where(m => m.EmpresaId == empresaId && m.ConversaId == conversaId && (depoisDe is null || m.EnviadaEm > depoisDe))
                .OrderBy(m => m.EnviadaEm).Take(limite).ToList());

        public Task<IReadOnlyList<Conversa>> ListarPorClienteAsync(Guid empresaId, Guid clienteId, int max = 5, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Conversa>>(Conversas.Where(c => c.EmpresaId == empresaId && c.ClienteId == clienteId).ToList());

        public Task<Mensagem?> ObterMensagemPorExternoIdAsync(Guid empresaId, string externoId, CancellationToken ct = default) =>
            Task.FromResult(Mensagens.FirstOrDefault(m => m.EmpresaId == empresaId && m.ExternoId == externoId));

        public Task AddAsync(Conversa conversa, CancellationToken ct = default)
        {
            Conversas.Add(conversa);
            return Task.CompletedTask;
        }

        public Task AddMensagemAsync(Mensagem mensagem, CancellationToken ct = default)
        {
            Mensagens.Add(mensagem);
            return Task.CompletedTask;
        }
    }
}
