using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

public class EnviarModeloConversaUseCaseTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IConsentimentoContatoRepository _consentimentos = Substitute.For<IConsentimentoContatoRepository>();
    private readonly ICatalogoModelosAtendimento _catalogo = Substitute.For<ICatalogoModelosAtendimento>();
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly List<Mensagem> _mensagens = [];
    private readonly Conversa _conversa;
    private readonly Cliente _cliente;
    private EnviarModeloConversaCommand Command => new(_empresa, _usuario, _conversa.Id, Guid.NewGuid(), "retomar", "pt_BR", ["Ana"]);
    private readonly IAtendenteRepository _atendentes = Substitute.For<IAtendenteRepository>();
    private EnviarModeloConversaUseCase UseCase => new(_conversas, _clientes, _catalogo, new(_consentimentos), new([_canal]), _uow, TimeProvider.System, _atendentes);

    public EnviarModeloConversaUseCaseTests()
    {
        _cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Ana" };
        _conversa = Conversa.Abrir(_empresa, "5511999990001", DateTime.UtcNow.AddDays(-2), clienteId: _cliente.Id);
        _conversa.RegistrarEntrada(DateTime.UtcNow.AddDays(-2));
        _conversas.ObterPorIdAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _conversas.ObterPorIdComLockAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _clientes.GetByIdAsync(_empresa, _cliente.Id).Returns(_cliente);
        _conversas.ObterMensagemAsync(_empresa, _conversa.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _mensagens.FirstOrDefault(m => m.Id == c.ArgAt<Guid>(2)));
        _conversas.ObterMensagemComLockAsync(_empresa, _conversa.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _mensagens.FirstOrDefault(m => m.Id == c.ArgAt<Guid>(2)));
        _conversas.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>()).Returns(c =>
        { _mensagens.Add(c.Arg<Mensagem>()); return Task.CompletedTask; });
        _canal.Canal.Returns(CanalConversa.WhatsApp);
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { _uow.CommitCount.Should().BeGreaterThan(0); return "wamid.modelo"; });
        Aprovar();
    }

    private void Aprovar(string categoria = "UTILITY", string? indisponivel = null) =>
        _catalogo.ListarAsync(_empresa, Arg.Any<CancellationToken>()).Returns([
            new ModeloAtendimento("retomar", "pt_BR", categoria, "Olá {{1}}, vamos continuar? {{1}}", 1, "Casa", "Até logo", indisponivel)]);

    [Fact]
    public async Task ForaDaJanela_GravaAntesDoCanal_AutoriaPreviaEPreservacaoDaJanela()
    {
        var entrada = _conversa.UltimaMensagemEntradaEm;
        var command = Command;
        var m = await UseCase.ExecuteAsync(command);
        m.Id.Should().Be(command.TentativaId);
        m.Status.Should().Be(StatusMensagem.Enviada);
        m.Texto.Should().Be("Casa\nOlá Ana, vamos continuar? Ana\nAté logo");
        m.EnviadaPorUsuarioId.Should().Be(_usuario);
        m.Modelo!.Nome.Should().Be("retomar");
        _conversa.AssumidaPorUsuarioId.Should().Be(_usuario);
        _conversa.UltimaMensagemEntradaEm.Should().Be(entrada);
        _conversa.DentroDaJanela(DateTime.UtcNow).Should().BeFalse();
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        await UseCase.ExecuteAsync(command);
        _mensagens.Should().ContainSingle();
        await _canal.Received(1).EnviarModeloAsync(Arg.Any<string>(), "retomar", "pt_BR", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // #2440 (A1): o modelo passa pela mesma guarda do texto (ATD-1, #2305).
    [Fact]
    public async Task ConversaDeOutraSemConfirmacao_RecusaENadaSai()
    {
        _conversa.Assumir(DateTime.UtcNow, Guid.NewGuid());

        var act = () => UseCase.ExecuteAsync(Command);

        await act.Should().ThrowAsync<ConversaAtribuidaOutraException>();
        _mensagens.Should().BeEmpty();
        _uow.CommitCount.Should().Be(0);
        await _canal.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task ConversaDeOutraComConfirmacao_EnviaETomaAConversa()
    {
        _conversa.Assumir(DateTime.UtcNow, Guid.NewGuid());

        var m = await UseCase.ExecuteAsync(Command with { Confirmada = true });

        m.Status.Should().Be(StatusMensagem.Enviada);
        _conversa.AssumidaPorUsuarioId.Should().Be(_usuario);
    }

    [Fact]
    public async Task ConversaPropria_NaoPedeConfirmacao()
    {
        _conversa.Assumir(DateTime.UtcNow, _usuario);

        (await UseCase.ExecuteAsync(Command)).Status.Should().Be(StatusMensagem.Enviada);
    }

    [Theory]
    [InlineData("nome")]
    [InlineData("idioma")]
    [InlineData("parametros")]
    [InlineData("usuario")]
    public async Task MesmaTentativaComOutroConteudoOuAutor_NaoReenvia(string alteracao)
    {
        var command = Command;
        await UseCase.ExecuteAsync(command);
        var outro = alteracao switch {
            "nome" => command with { Nome = "outro" }, "idioma" => command with { Idioma = "en_US" },
            "parametros" => command with { Parametros = ["Outro"] }, _ => command with { UsuarioId = Guid.NewGuid() } };
        var act = () => UseCase.ExecuteAsync(outro);
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("encerrada")]
    [InlineData("bloqueado")]
    [InlineData("revogado")]
    [InlineData("marketing")]
    [InlineData("semCliente")]
    [InlineData("outraEmpresa")]
    [InlineData("naoAprovado")]
    [InlineData("idioma")]
    [InlineData("quantidade")]
    [InlineData("formato")]
    [InlineData("vazio")]
    public async Task RecusasAntesDoEnvio(string caso)
    {
        var command = Command;
        switch (caso)
        {
            case "encerrada": _conversa.Encerrar(DateTime.UtcNow); break;
            case "bloqueado": _cliente.Bloquear("motivo", DateTime.UtcNow); break;
            case "revogado": _consentimentos.ListarDoClienteAsync(_empresa, _cliente.Id, Arg.Any<CancellationToken>()).Returns([
                ConsentimentoContato.Registrar(_empresa, _cliente.Id, CanalConversa.WhatsApp, FinalidadeContato.Transacional,
                    SituacaoConsentimento.Revogado, "teste", DateTime.UtcNow)]); break;
            case "marketing": Aprovar("MARKETING"); break;
            case "semCliente": _clientes.GetByIdAsync(_empresa, _cliente.Id).Returns((Cliente?)null); break;
            case "outraEmpresa": _conversas.ObterPorIdAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>())
                .Returns(Conversa.Abrir(Guid.NewGuid(), "5511999990002", DateTime.UtcNow)); break;
            case "naoAprovado": _catalogo.ListarAsync(_empresa, Arg.Any<CancellationToken>()).Returns([]); break;
            case "idioma": command = command with { Idioma = "en_US" }; break;
            case "quantidade": command = command with { Parametros = [] }; break;
            case "formato": Aprovar(indisponivel: "mídia não aceita"); break;
            case "vazio": command = command with { Parametros = [" "] }; break;
        }
        var act = () => UseCase.ExecuteAsync(command);
        await act.Should().ThrowAsync<Exception>();
        _mensagens.Should().BeEmpty();
        _uow.CommitCount.Should().Be(0);
        await _canal.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task SoCanalComAceitaModelo()
    {
        var conversa = Conversa.Abrir(_empresa, "sessao", DateTime.UtcNow, canal: CanalConversa.ChatSite);
        _conversas.ObterPorIdAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        var act = () => UseCase.ExecuteAsync(Command with { ConversaId = conversa.Id });
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*canal não aceita*");
        _catalogo.ReceivedCalls().Should().BeEmpty();
    }

    // #1550: recusa levantada antes da rede (empresa sem phone_number_id) é permanente e nada saiu. O "2xx sem
    // message id" continua incerto (a Meta respondeu e algo pode ter saído), assim como o 5xx.
    [Theory]
    [InlineData(true, null, TipoFalhaEnvio.Permanente)]
    [InlineData(true, 400, TipoFalhaEnvio.Permanente)]
    [InlineData(false, 429, TipoFalhaEnvio.Permanente)]
    [InlineData(true, 200, TipoFalhaEnvio.Incerta)]
    [InlineData(false, 503, TipoFalhaEnvio.Incerta)]
    [InlineData(false, null, TipoFalhaEnvio.Incerta)]
    public async Task FalhaDoCanal_ClassificaPeloQuePodeTerSaido(bool ehPermanente, int? statusHttp, TipoFalhaEnvio esperado)
    {
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new WhatsAppCloudException(0, "falha", ehPermanente, statusHttp));
        var m = await UseCase.ExecuteAsync(Command);
        m.Status.Should().Be(StatusMensagem.Falhou);
        _mensagens.Single().UltimaFalhaEnvio.Should().Be(esperado);
        (m.Erro!.Contains("incerto")).Should().Be(esperado == TipoFalhaEnvio.Incerta);
        _mensagens.Single().ProximoReenvioEm.Should().BeNull();
    }

    [Fact]
    public async Task Timeout_ConservaIntencao_NaoAgendaReenvioNemSms_NemRepeteAoRetomar()
    {
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new TimeoutException());
        var command = Command;
        var m = await UseCase.ExecuteAsync(command);
        m.Status.Should().Be(StatusMensagem.Falhou);
        m.Erro.Should().Contain("incerto");
        _mensagens.Single().PodeReenviar.Should().BeFalse();
        _mensagens.Single().ProximoReenvioEm.Should().BeNull();
        _mensagens.Single().PrecisaDeReservaSms.Should().BeFalse();
        await UseCase.ExecuteAsync(command);
        await _canal.Received(1).EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // #1599: a varredura de intenção presa marcou falha incerta com o POST ainda no ar, e o canal confirmou
    // depois. A entrega vence, e nada do que a varredura gravou fica pendurado na mensagem enviada.
    [Fact]
    public async Task CanalConfirmaDepoisDaVarreduraMarcarFalhaIncerta_SucessoVenceELimpaAFalha()
    {
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { VarreduraMarcaFalhaIncerta(); return "wamid.tardio"; });
        var m = await UseCase.ExecuteAsync(Command);
        m.Status.Should().Be(StatusMensagem.Enviada);
        m.ExternoId.Should().Be("wamid.tardio");
        m.Erro.Should().BeNull();
        var salva = _mensagens.Single();
        salva.TentativasEnvio.Should().Be(0);
        salva.UltimaFalhaEnvio.Should().BeNull();
        salva.ProximoReenvioEm.Should().BeNull();
    }

    // #1599: a varredura já encerrou a intenção e o canal falhou depois. A falha conta uma vez só.
    [Fact]
    public async Task CanalFalhaDepoisDaVarreduraMarcarFalhaIncerta_NaoSomaOutraFalha()
    {
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => { VarreduraMarcaFalhaIncerta(); throw new TimeoutException(); });
        var m = await UseCase.ExecuteAsync(Command);
        m.Status.Should().Be(StatusMensagem.Falhou);
        _mensagens.Single().TentativasEnvio.Should().Be(1);
        _mensagens.Single().UltimaFalhaEnvio.Should().Be(TipoFalhaEnvio.Incerta);
    }

    // #3205 (S80): mensagem padrão local sai como texto renderizado pela ponte e fica gravada como modelo de origem Local.
    private void AprovarLocal() =>
        _catalogo.ListarAsync(_empresa, Arg.Any<CancellationToken>()).Returns([
            new ModeloAtendimento("retomar", "pt_BR", "UTILITY", "Olá {{1}}, vamos continuar? {{1}}", 1, "Casa", "Até logo", null, OrigemModelo.Local)]);

    private Conversa ConversaDeLead(DateTime? ultimaEntrada)
    {
        var lead = Conversa.Abrir(_empresa, "5511999990003", DateTime.UtcNow.AddDays(-9));
        if (ultimaEntrada is { } entrada) lead.RegistrarEntrada(entrada);
        _conversas.ObterPorIdAsync(_empresa, lead.Id, Arg.Any<CancellationToken>()).Returns(lead);
        _conversas.ObterPorIdComLockAsync(_empresa, lead.Id, Arg.Any<CancellationToken>()).Returns(lead);
        return lead;
    }

    private async Task NadaSaiu()
    {
        _mensagens.Should().BeEmpty();
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        await _canal.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task ModeloLocal_SaiPorTextoRenderizado_EGravaOrigemLocal()
    {
        AprovarLocal();
        _canal.EnviarTextoAsync("5511999990001", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.texto");
        var command = Command;

        var m = await UseCase.ExecuteAsync(command);

        m.Status.Should().Be(StatusMensagem.Enviada);
        m.Texto.Should().Be("Casa\nOlá Ana, vamos continuar? Ana\nAté logo");
        m.Modelo!.Origem.Should().Be(OrigemModelo.Local);
        _mensagens.Single().ModeloJson.Should().Contain("\"Local\"");
        await _canal.Received(1).EnviarTextoAsync("5511999990001", "Casa\nOlá Ana, vamos continuar? Ana\nAté logo", Arg.Any<CancellationToken>());
        await _canal.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
        await UseCase.ExecuteAsync(command);
        _mensagens.Should().ContainSingle();
        await _canal.Received(1).EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModeloMeta_GravaOrigemMeta_ESegueSemTexto()
    {
        var m = await UseCase.ExecuteAsync(Command);

        m.Modelo!.Origem.Should().Be(OrigemModelo.Meta);
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public void ModeloJsonAntigoSemOrigem_LeComoMeta()
    {
        var antigo = System.Text.Json.JsonSerializer.Deserialize<ModeloMensagem>("{\"Nome\":\"retomar\",\"Idioma\":\"pt_BR\",\"Parametros\":[\"Ana\"]}");

        antigo!.Origem.Should().Be(OrigemModelo.Meta);
    }

    [Fact]
    public async Task ModeloLocal_ClienteNuncaEscreveu_Recusa()
    {
        AprovarLocal();
        var lead = ConversaDeLead(null);

        var act = () => UseCase.ExecuteAsync(Command with { ConversaId = lead.Id });

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*ainda não escreveu*");
        await NadaSaiu();
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(8, false)]
    public async Task ModeloLocal_SoAte7DiasDaUltimaEntradaDoCliente(int diasAtras, bool sai)
    {
        AprovarLocal();
        var lead = ConversaDeLead(DateTime.UtcNow.AddDays(-diasAtras));
        _canal.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.texto");

        var act = () => UseCase.ExecuteAsync(Command with { ConversaId = lead.Id });

        if (sai) (await act()).Status.Should().Be(StatusMensagem.Enviada);
        else
        {
            await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*mais de 7 dias*");
            await NadaSaiu();
        }
    }

    [Fact]
    public async Task ModeloLocal_LeadSemCadastro_Recebe()
    {
        AprovarLocal();
        var lead = ConversaDeLead(DateTime.UtcNow.AddDays(-1));
        _canal.EnviarTextoAsync("5511999990003", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.lead");

        var m = await UseCase.ExecuteAsync(Command with { ConversaId = lead.Id });

        m.Status.Should().Be(StatusMensagem.Enviada);
        m.Modelo!.Origem.Should().Be(OrigemModelo.Local);
        await _clientes.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Theory]
    [InlineData("SAIR")]
    [InlineData("Parar.")]
    [InlineData("Stop")]
    public async Task ModeloLocal_LeadQuePediuParaParar_NaoRecebe(string palavra)
    {
        AprovarLocal();
        var lead = ConversaDeLead(DateTime.UtcNow.AddHours(-1));
        _conversas.ListarMensagensAsync(_empresa, lead.Id, null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([
            Mensagem.Entrada(_empresa, lead.Id, DateTime.UtcNow.AddHours(-2), TipoConteudoMensagem.Texto, "oi"),
            Mensagem.Entrada(_empresa, lead.Id, DateTime.UtcNow.AddHours(-1), TipoConteudoMensagem.Texto, palavra)]);

        var act = () => UseCase.ExecuteAsync(Command with { ConversaId = lead.Id });

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*pediu para parar*");
        await NadaSaiu();
    }

    [Fact]
    public async Task ModeloLocal_LeadQueEscreveuFraseComAPalavra_Recebe()
    {
        AprovarLocal();
        var lead = ConversaDeLead(DateTime.UtcNow.AddHours(-1));
        _conversas.ListarMensagensAsync(_empresa, lead.Id, null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([
            Mensagem.Entrada(_empresa, lead.Id, DateTime.UtcNow.AddHours(-1), TipoConteudoMensagem.Texto, "quero sair do grupo")]);
        _canal.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.lead");

        (await UseCase.ExecuteAsync(Command with { ConversaId = lead.Id })).Status.Should().Be(StatusMensagem.Enviada);
    }

    [Theory]
    [InlineData("bloqueado")]
    [InlineData("revogado")]
    public async Task ModeloLocal_ComClienteCadastrado_BloqueioEConsentimentoValem(string caso)
    {
        AprovarLocal();
        if (caso == "bloqueado") _cliente.Bloquear("motivo", DateTime.UtcNow);
        else _consentimentos.ListarDoClienteAsync(_empresa, _cliente.Id, Arg.Any<CancellationToken>()).Returns([
            ConsentimentoContato.Registrar(_empresa, _cliente.Id, CanalConversa.WhatsApp, FinalidadeContato.Transacional,
                SituacaoConsentimento.Revogado, "teste", DateTime.UtcNow)]);

        var act = () => UseCase.ExecuteAsync(Command);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        await NadaSaiu();
    }

    [Fact]
    public async Task ModeloMeta_LeadSemCadastro_SegueExigindoCliente()
    {
        var lead = ConversaDeLead(DateTime.UtcNow.AddDays(-1));

        var act = () => UseCase.ExecuteAsync(Command with { ConversaId = lead.Id });

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Cadastre o cliente*");
        await NadaSaiu();
    }

    // Idempotência com a origem: a tentativa gravada como Meta não vira texto local se o catálogo mudou no meio.
    [Fact]
    public async Task TentativaGravadaComOutraOrigem_RecusaEmVezDeReenviar()
    {
        var command = Command;
        await UseCase.ExecuteAsync(command);
        AprovarLocal();
        var chamadas = 0;
        _conversas.ObterMensagemAsync(_empresa, _conversa.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => chamadas++ == 0 ? null : _mensagens.FirstOrDefault(m => m.Id == c.ArgAt<Guid>(2)));
        _conversas.ObterMensagemComLockAsync(_empresa, _conversa.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _mensagens.FirstOrDefault(m => m.Id == c.ArgAt<Guid>(2)));

        var act = () => UseCase.ExecuteAsync(command);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*outro conteúdo*");
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    private void VarreduraMarcaFalhaIncerta() =>
        _mensagens.Single().RegistrarFalhaEnvio(EnviarModeloConversaUseCase.AvisoEnvioIncerto, TipoFalhaEnvio.Incerta, DateTime.UtcNow);
}
