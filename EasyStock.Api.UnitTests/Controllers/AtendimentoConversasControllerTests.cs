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
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.UseCases.AdicionarClienteEndereco;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;
using EasyStock.Application.UseCases.Atendimento.Endereco;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Application.UseCases.Storefront.Frete;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
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
    private readonly IAtendenteRepository _atendentes = Substitute.For<IAtendenteRepository>();
    private readonly ILinkCardapioConversaRepository _links = Substitute.For<ILinkCardapioConversaRepository>();
    private readonly AtendimentoConversasController _controller;

    internal static LinkCardapioConversaService LinkCardapio(ILinkCardapioConversaRepository links) =>
        new(links, new SaudacaoAtendimento(Substitute.For<IStorefrontRepository>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));

    public AtendimentoConversasControllerTests()
    {
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.UsuarioId.Returns(_usuarioId);
        _currentUser.TemPermissao(Permissao.AtenderConversas).Returns(true);
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
            new TransferirConversaUseCase(_repositorio, _atendentes, _unitOfWork),
            new ObterDossieClienteUseCase(
                Substitute.For<IClienteRepository>(), Substitute.For<IClienteCrmRepository>(),
                Substitute.For<IHistoricoPedidosClienteQueries>(), Substitute.For<IDomicilioQueries>(), _repositorio),
            new GerarLinkCardapioConversaUseCase(_repositorio, LinkCardapio(_links), _unitOfWork, TimeProvider.System),
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
            Substitute.For<IUsoIaRepository>(), _unitOfWork,
            new ObterDossieClienteUseCase(
                Substitute.For<IClienteRepository>(), Substitute.For<IClienteCrmRepository>(),
                Substitute.For<IHistoricoPedidosClienteQueries>(), Substitute.For<IDomicilioQueries>(), _repositorio),
            Substitute.For<ICadernoRepository>(), NullLogger<AgenteAtendimentoService>.Instance);

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
    public async Task LiberarForaDeAreaGravaNoContexto()
    {
        var conversa = ConversaComClienteAgora();

        var result = await _controller.LiberarForaDeArea(conversa.Id, new LiberarForaDeAreaBody("cliente fiel"), default);

        result.Should().BeOfType<OkObjectResult>();
        ContextoConversaJson.Ler<bool>(conversa, ContextoConversaJson.ForaDeAreaLiberado).Should().BeTrue();
        ContextoConversaJson.Ler<string>(conversa, ContextoConversaJson.ForaDeAreaMotivo).Should().Be("cliente fiel");
        _repositorio.Mensagens.Should().ContainSingle(m => m.Autor == AutorMensagem.Sistema && m.Texto!.Contains("cliente fiel"));
        await _unitOfWork.Received(1).CommitAsync();
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

        var result = await _controller.Listar(null, "mar", null, 1, 20, default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var data = (IReadOnlyList<ConversaResumoResult>)ok.Value!.GetType().GetProperty("Data")!.GetValue(ok.Value)!;
        data.Should().ContainSingle(c => c.Id == conversa.Id && c.UltimaMensagemTexto == "oi" && c.NaoLidas == 1);
    }

    // ── S41: atendentes e atribuição ──────────────────────────────────

    [Fact]
    public async Task DossieSemClienteNaoFalha()
    {
        var conversa = ConversaComClienteAgora();
        conversa.ClienteId.Should().BeNull("é um lead: a conversa não tem cliente vinculado");

        var dossie = Dados<DossieClienteDto>(await _controller.Dossie(conversa.Id, default));

        dossie.Cliente.Should().Be(new DossieClienteDados(null, "Maria", WaId, null, null, null));
        dossie.UltimosPedidos.Should().BeEmpty();
        dossie.Notas.Should().BeEmpty();
        dossie.Domicilio.Should().BeEmpty();
    }

    [Fact]
    public async Task DossieDeConversaInexistenteDevolve404()
    {
        var result = await _controller.Dossie(Guid.NewGuid(), default);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task LinkCardapioDevolveOLinkDaLojaComTokenDaConversa()
    {
        var conversa = ConversaComClienteAgora();

        var link = Dados<LinkCardapioConversaGerado>(await _controller.LinkCardapio(conversa.Id, default));

        // #1353: o mesmo link que o agente manda (S48), não a tela do console.
        link.Url.Should().StartWith($"{SaudacaoAtendimento.BaseUrlPadrao}{SaudacaoAtendimento.CaminhoCardapio}?c=");
        await _links.Received(1).AddAsync(Arg.Is<LinkCardapioConversa>(l => l.ConversaId == conversa.Id), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task LinkCardapioDeConversaDeOutraEmpresaDevolve404()
    {
        var result = await _controller.LinkCardapio(Guid.NewGuid(), default);

        result.Should().BeOfType<NotFoundObjectResult>();
        await _links.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    private static T Dados<T>(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return (T)ok.Value!.GetType().GetProperty("Data")!.GetValue(ok.Value)!;
    }

    private void Atendente(Guid usuarioId, NivelAcesso nivel = NivelAcesso.Operador, params Permissao[] explicitas) =>
        _atendentes.ObterUsuarioAtivoAsync(_empresaId, usuarioId, Arg.Any<CancellationToken>())
            .Returns(new UsuarioDaEmpresa(usuarioId, "Bia", "bia@x.com", [new PerfilNaEmpresa(nivel, explicitas)]));

    [Fact]
    public async Task SemPermissaoDeAtender403()
    {
        var conversa = ConversaComClienteAgora();
        _currentUser.TemPermissao(Permissao.AtenderConversas).Returns(false);

        (await _controller.Assumir(conversa.Id, default)).Should().BeOfType<ForbidResult>();
        (await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("oi"), default)).Should().BeOfType<ForbidResult>();
        (await _controller.Transferir(conversa.Id, new TransferirConversaBody(Guid.NewGuid()), default)).Should().BeOfType<ForbidResult>();
        (await _controller.LiberarAutomatico(conversa.Id, default)).Should().BeOfType<ForbidResult>();
        (await _controller.Encerrar(conversa.Id, default)).Should().BeOfType<ForbidResult>();
        (await _controller.LinkCardapio(conversa.Id, default)).Should().BeOfType<ForbidResult>();

        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task EnviarGravaQuemEnviou()
    {
        var conversa = ConversaComClienteAgora();

        var result = await _controller.EnviarMensagem(conversa.Id, new EnviarMensagemConsoleBody("Oi"), default);

        Dados<MensagemAtendimentoResult>(result).EnviadaPorUsuarioId.Should().Be(_usuarioId);
        _repositorio.Mensagens.Should().ContainSingle(m => m.Autor == AutorMensagem.Dona && m.EnviadaPorUsuarioId == _usuarioId);
    }

    [Fact]
    public async Task TransferirParaAtendenteTrocaResponsavel()
    {
        var conversa = ConversaComClienteAgora();
        conversa.Assumir(DateTime.UtcNow, _usuarioId);
        var bia = Guid.NewGuid();
        Atendente(bia);

        var result = await _controller.Transferir(conversa.Id, new TransferirConversaBody(bia), default);

        Dados<ConversaSituacaoResult>(result).AssumidaPorUsuarioId.Should().Be(bia);
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        _repositorio.Mensagens.Should().ContainSingle(m => m.Autor == AutorMensagem.Sistema
            && m.Texto!.Contains(_usuarioId.ToString()) && m.Texto.Contains(bia.ToString()));
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task TransferirParaQuemNaoAtende422()
    {
        var conversa = ConversaComClienteAgora();
        conversa.Assumir(DateTime.UtcNow, _usuarioId);
        var visualizador = Guid.NewGuid();
        Atendente(visualizador, NivelAcesso.Visualizador);
        var restrito = Guid.NewGuid();
        Atendente(restrito, NivelAcesso.Admin, Permissao.GerenciarProdutos);
        var desconhecido = Guid.NewGuid();

        foreach (var destino in new[] { visualizador, restrito, desconhecido })
        {
            var result = await _controller.Transferir(conversa.Id, new TransferirConversaBody(destino), default);
            result.Should().BeOfType<UnprocessableEntityObjectResult>();
        }

        conversa.AssumidaPorUsuarioId.Should().Be(_usuarioId);
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task ListarFiltraPorResponsavel()
    {
        var minha = ConversaComClienteAgora();
        minha.Assumir(DateTime.UtcNow, _usuarioId);
        var livre = Conversa.Abrir(_empresaId, "5511999997777", DateTime.UtcNow, "Joana");
        _repositorio.Conversas.Add(livre);
        var daBia = Conversa.Abrir(_empresaId, "5511999996666", DateTime.UtcNow, "Rita");
        var bia = Guid.NewGuid();
        daBia.Transferir(bia, DateTime.UtcNow);
        _repositorio.Conversas.Add(daBia);

        Dados<IReadOnlyList<ConversaResumoResult>>(await _controller.Listar(null, null, "eu", 1, 20, default))
            .Should().ContainSingle(c => c.Id == minha.Id);
        Dados<IReadOnlyList<ConversaResumoResult>>(await _controller.Listar(null, null, "ninguem", 1, 20, default))
            .Should().ContainSingle(c => c.Id == livre.Id);
        Dados<IReadOnlyList<ConversaResumoResult>>(await _controller.Listar(null, null, bia.ToString(), 1, 20, default))
            .Should().ContainSingle(c => c.Id == daBia.Id);
        (await _controller.Listar(null, null, "fulano", 1, 20, default)).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ListarAtendentesSoQuemTemPermissao()
    {
        var ana = new UsuarioDaEmpresa(Guid.NewGuid(), "Ana", "ana@x.com", [new PerfilNaEmpresa(NivelAcesso.Operador, [])]);
        var leitor = new UsuarioDaEmpresa(Guid.NewGuid(), "Leo", "leo@x.com", [new PerfilNaEmpresa(NivelAcesso.Visualizador, [])]);
        var semPerfil = new UsuarioDaEmpresa(Guid.NewGuid(), "Sem", "sem@x.com", []);
        // Com dois perfis vale o de maior nível (menor valor), como no login.
        var dupla = new UsuarioDaEmpresa(Guid.NewGuid(), "Duda", "duda@x.com",
            [new PerfilNaEmpresa(NivelAcesso.Visualizador, []), new PerfilNaEmpresa(NivelAcesso.Gerente, [])]);
        _atendentes.ListarUsuariosAtivosAsync(_empresaId, Arg.Any<CancellationToken>()).Returns([ana, leitor, semPerfil, dupla]);
        var controller = new AtendimentoAtendentesController(new ListarAtendentesUseCase(_atendentes), _currentUser);

        var lista = Dados<IReadOnlyList<AtendenteResult>>(await controller.Listar(default));

        lista.Select(a => a.Nome).Should().BeEquivalentTo(["Ana", "Duda"]);
    }

    // ── #1276: cadastro do cliente da conversa ──────────────────────────────────────────────

    private (CadastrarClienteDaConversaUseCase UseCase, IClienteRepository Clientes) CadastroDoCliente()
    {
        var clientes = Substitute.For<IClienteRepository>();
        clientes.AddAsync(Arg.Do<ClienteEntity>(c => clientes.GetByIdWithDetailsAsync(_empresaId, c.Id).Returns(c)));
        var storefronts = Substitute.For<IStorefrontRepository>();
        var cep = Substitute.For<ICepLookupClient>();
        var frete = new CalcularFreteUseCase(storefronts, Substitute.For<IFreteZonaRepository>(), cep,
            Substitute.For<IGeocodingClient>(), Substitute.For<IRotaClient>(), NullLogger<CalcularFreteUseCase>.Instance);
        var useCase = new CadastrarClienteDaConversaUseCase(
            _repositorio, clientes, _unitOfWork,
            new IdentificarClientePorTelefoneUseCase(clientes, Substitute.For<IClienteStorefrontRepository>(),
                NullLogger<IdentificarClientePorTelefoneUseCase>.Instance),
            new ValidarEnderecoUseCase(storefronts, frete, cep, Substitute.For<IConfiguracaoAtendimentoRepository>(),
                NullLogger<ValidarEnderecoUseCase>.Instance),
            new ConfirmarEnderecoClienteUseCase(clientes, _unitOfWork,
                new AdicionarClienteEnderecoUseCase(clientes, _unitOfWork, NullLogger<AdicionarClienteEnderecoUseCase>.Instance)));
        return (useCase, clientes);
    }

    private Conversa ConversaDoSite()
    {
        var conversa = Conversa.Abrir(_empresaId, "visitante-1", DateTime.UtcNow.AddMinutes(-5), "Visitante do site", canal: CanalConversa.ChatSite);
        _repositorio.Conversas.Add(conversa);
        return conversa;
    }

    [Fact]
    public async Task CadastrarClienteVinculaEDevolveNoEnvelope()
    {
        var conversa = ConversaDoSite();

        var dados = Dados<ClienteDaConversaResult>(await _controller.CadastrarCliente(
            conversa.Id, new CadastrarClienteConversaBody("Maria Souza", "(11) 98765-4321", null), CadastroDoCliente().UseCase, default));

        dados.Nome.Should().Be("Maria Souza");
        dados.Telefone.Should().Be("+5511987654321");
        dados.Novo.Should().BeTrue();
        conversa.ClienteId.Should().Be(dados.ClienteId);
    }

    [Fact]
    public async Task CadastrarClienteSemTelefone400()
    {
        var conversa = ConversaDoSite();

        var result = await _controller.CadastrarCliente(
            conversa.Id, new CadastrarClienteConversaBody("Maria", null, null), CadastroDoCliente().UseCase, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        conversa.ClienteId.Should().BeNull();
    }

    [Fact]
    public async Task CadastrarClienteSemPermissaoDeAtender403()
    {
        _currentUser.TemPermissao(Permissao.AtenderConversas).Returns(false);
        var conversa = ConversaDoSite();

        var result = await _controller.CadastrarCliente(
            conversa.Id, new CadastrarClienteConversaBody("Maria", "11987654321", null), CadastroDoCliente().UseCase, default);

        result.Should().BeOfType<ForbidResult>();
        conversa.ClienteId.Should().BeNull();
    }

    [Fact]
    public async Task CadastrarClienteConversaInexistente404()
    {
        var result = await _controller.CadastrarCliente(
            Guid.NewGuid(), new CadastrarClienteConversaBody("Maria", "11987654321", null), CadastroDoCliente().UseCase, default);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // #1287: mídia recebida do cliente sai do storage privado pelo endpoint autenticado.
    private (ObterMidiaMensagemUseCase UseCase, IFileStorage Storage) MidiaUseCase()
    {
        var storage = Substitute.For<IFileStorage>();
        return (new ObterMidiaMensagemUseCase(_repositorio, storage), storage);
    }

    private Mensagem FotoDoCliente(Guid empresaId, Guid conversaId)
    {
        var foto = Mensagem.Entrada(empresaId, conversaId, DateTime.UtcNow, TipoConteudoMensagem.Imagem, null, "wamid.foto");
        foto.AnexarMidia($"atendimento/{empresaId}/{conversaId}/wamid.foto.jpg", "image/jpeg");
        _repositorio.Mensagens.Add(foto);
        return foto;
    }

    [Fact]
    public async Task MidiaDevolveArquivoDoStorage()
    {
        var conversa = ConversaComClienteAgora();
        var foto = FotoDoCliente(_empresaId, conversa.Id);
        var (useCase, storage) = MidiaUseCase();
        storage.ExistsAsync(foto.MidiaChave!, Arg.Any<CancellationToken>()).Returns(true);
        storage.DownloadAsync(foto.MidiaChave!, Arg.Any<CancellationToken>()).Returns([1, 2, 3]);

        var result = await _controller.Midia(conversa.Id, foto.Id, useCase, default);

        var arquivo = result.Should().BeOfType<FileContentResult>().Subject;
        arquivo.ContentType.Should().Be("image/jpeg");
        arquivo.FileContents.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task MidiaDeOutraEmpresaDevolve404SemTocarNoStorage()
    {
        var outra = Conversa.Abrir(Guid.NewGuid(), WaId, DateTime.UtcNow, "Zé");
        _repositorio.Conversas.Add(outra);
        var foto = FotoDoCliente(outra.EmpresaId, outra.Id);
        var (useCase, storage) = MidiaUseCase();

        var result = await _controller.Midia(outra.Id, foto.Id, useCase, default);

        result.Should().BeOfType<NotFoundObjectResult>();
        await storage.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default);
    }

    [Fact]
    public async Task MidiaDeMensagemSemArquivoOuForaDoStorageDevolve404()
    {
        var conversa = ConversaComClienteAgora();
        var texto = _repositorio.Mensagens.Single(m => m.ConversaId == conversa.Id);
        var foto = FotoDoCliente(_empresaId, conversa.Id);
        var (useCase, storage) = MidiaUseCase();
        storage.ExistsAsync(foto.MidiaChave!, Arg.Any<CancellationToken>()).Returns(false);

        (await _controller.Midia(conversa.Id, texto.Id, useCase, default)).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.Midia(conversa.Id, foto.Id, useCase, default)).Should().BeOfType<NotFoundObjectResult>();
        await storage.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default);
    }

    /// <summary>Repositório em memória: basta para os use cases e para o turno do agente.</summary>
    private sealed class ConversaRepositoryEmMemoria : IConversaRepository
    {
        public List<Conversa> Conversas { get; } = [];
        public List<Mensagem> Mensagens { get; } = [];

        public Task<Conversa?> ObterPorIdAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Conversas.FirstOrDefault(c => c.EmpresaId == empresaId && c.Id == id));

        public Task<Guid?> TravarParaPedidoAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Conversas.FirstOrDefault(c => c.EmpresaId == empresaId && c.Id == id)?.PedidoEmAndamentoId);

        public Task<SituacaoConversa?> ObterSituacaoAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Conversas.FirstOrDefault(c => c.EmpresaId == empresaId && c.Id == id)?.Situacao);

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
            Guid empresaId, SituacaoConversa? situacao, string? busca, FiltroResponsavel? responsavel, int pagina, int tamanhoPagina, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConversaInboxItem>>(Conversas
                .Where(c => c.EmpresaId == empresaId && (situacao is null || c.Situacao == situacao))
                .Where(c => responsavel is null || c.AssumidaPorUsuarioId == responsavel.UsuarioId)
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

        public Task<Mensagem?> ObterMensagemAsync(Guid empresaId, Guid conversaId, Guid mensagemId, CancellationToken ct = default) =>
            Task.FromResult(Mensagens.FirstOrDefault(m => m.EmpresaId == empresaId && m.ConversaId == conversaId && m.Id == mensagemId));

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
