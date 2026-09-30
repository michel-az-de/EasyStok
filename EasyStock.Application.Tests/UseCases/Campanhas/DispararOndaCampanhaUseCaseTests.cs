using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.UseCases.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.UseCases.Campanhas;

/// <summary>
/// S30 (US-053, US-054, RN-41, RN-42): a onda enfileira no outbox até <c>TamanhoOnda</c> pendentes, quem
/// já comprou o item primeiro; a primeira sai no horário agendado e as seguintes só pela dona.
/// </summary>
public class DispararOndaCampanhaUseCaseTests
{
    private readonly CenarioDisparoCampanha _c = new();

    public DispararOndaCampanhaUseCaseTests() => _c.Relogio.SetUtcNow(CenarioDisparoCampanha.Disparo);

    [Fact]
    public async Task PrimeiraOndaPrioridadeETamanho()
    {
        var itemId = Guid.NewGuid();
        var campanha = _c.Campanha(tamanhoOnda: 30, itemId: itemId);
        var agora = CenarioDisparoCampanha.Disparo;
        // 50 pendentes: 10 que já compraram o item (no fim da ordem alfabética, para a prioridade aparecer) e 40 que não.
        var compradores = Enumerable.Range(0, 10)
            .Select(i => _c.Pendente(campanha, $"Zuleica {i:00}", comprouEm: agora.AddDays(-i - 1)))
            .ToList();
        var demais = Enumerable.Range(0, 40).Select(i => _c.Pendente(campanha, $"Ana {i:00}")).ToList();

        var resultado = await _c.Disparar().ExecuteAsync(_c.EmpresaId, campanha.Id, OrigemOndaCampanha.Agendamento);

        resultado.Onda.Should().Be(1);
        resultado.Enfileirados.Should().Be(30);
        resultado.PendentesRestantes.Should().Be(20);
        campanha.Status.Should().Be(StatusCampanha.Enviando);
        campanha.OndaAtual.Should().Be(1);

        compradores.Should().OnlyContain(c => _c.Destinatario(c).Status == StatusCampanhaDestinatario.Enfileirado,
            "quem já comprou o item vai na primeira onda");
        demais.Take(20).Should().OnlyContain(c => _c.Destinatario(c).Status == StatusCampanhaDestinatario.Enfileirado);
        demais.Skip(20).Should().OnlyContain(c => _c.Destinatario(c).Status == StatusCampanhaDestinatario.Pendente);

        _c.Mensagens.Should().HaveCount(30);
        var enfileirados = _c.Destinatarios.Where(d => d.Status == StatusCampanhaDestinatario.Enfileirado).ToList();
        enfileirados.Should().OnlyContain(d => d.Onda == 1 && _c.Mensagens.Any(m => m.Id == d.OutboxMensagemId));
        _c.Mensagens.Should().OnlyContain(m =>
            m.ProximaTentativaEm == CenarioDisparoCampanha.Disparo
            && m.Categoria == CategoriaConteudoNotificacao.Marketing
            && m.Canal == CanalNotificacao.WhatsApp
            && m.EmpresaId == _c.EmpresaId
            && m.Destinatario == "+5511997573992");

        var ana = _c.Mensagens.Single(m => m.Id == _c.Destinatario(demais[0]).OutboxMensagemId);
        ana.CorpoRenderizado.Should().StartWith("Oi Ana 00, saiu bolo de fubá!");
        ana.LerMetadados().Should().Contain(new Dictionary<string, string>
        {
            ["template"] = EnfileiradorMensagensCampanha.TemplateMetaGenerico,
            ["idioma"] = "pt_BR",
            ["param1"] = "Ana 00",
            ["param2"] = "Oi Ana 00, saiu bolo de fubá!",
        });

        _c.EventosCriados.Should().ContainSingle().Which.Should().Match<EventoNotificacao>(e =>
            e.Tipo == TipoEventoNotificacao.CampanhaMarketing && e.RefEntidadeId == campanha.Id
            && e.Status == StatusEventoNotificacao.Processado);
        await _c.Uow.Received(1).CommitAsync();

        // Nada dispara sozinho depois: o job concilia e conclui a onda, mas não abre a segunda.
        _c.OutboxProcessou();
        _c.Relogio.Advance(TimeSpan.FromDays(1));
        var rodada = await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);
        rodada.OndaDisparada.Should().BeNull();
        rodada.Enviados.Should().Be(30);
        rodada.OndaConcluida.Should().BeTrue();
        campanha.Status.Should().Be(StatusCampanha.Enviada);
        (await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id)).OndaDisparada.Should().BeNull();
        _c.Destinatarios.Count(d => d.Status == StatusCampanhaDestinatario.Pendente).Should().Be(20);
        _c.Mensagens.Should().HaveCount(30);
    }

    [Fact]
    public async Task SegundaOndaSoManual()
    {
        var campanha = _c.Campanha(tamanhoOnda: 2);
        var clientes = Enumerable.Range(0, 3).Select(i => _c.Pendente(campanha, $"Cliente {i}")).ToList();

        // A dona não antecipa a primeira onda: ela sai no horário agendado.
        var antecipar = () => _c.Disparar().ExecuteAsync(_c.EmpresaId, campanha.Id, OrigemOndaCampanha.Dona);
        await antecipar.Should().ThrowAsync<RegraDeDominioVioladaException>();

        var rodada = await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);
        rodada.OndaDisparada.Should().Be(1);
        _c.OutboxProcessou();
        _c.Relogio.Advance(TimeSpan.FromDays(2));
        await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);
        campanha.Status.Should().Be(StatusCampanha.Enviada);

        // DisparoEm no passado não reativa a onda 1 nem abre a 2.
        var automatica = () => _c.Disparar().ExecuteAsync(_c.EmpresaId, campanha.Id, OrigemOndaCampanha.Agendamento);
        await automatica.Should().ThrowAsync<RegraDeDominioVioladaException>();
        (await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id)).OndaDisparada.Should().BeNull();
        _c.Destinatario(clientes[2]).Status.Should().Be(StatusCampanhaDestinatario.Pendente);

        var segunda = await _c.Disparar().ExecuteAsync(_c.EmpresaId, campanha.Id, OrigemOndaCampanha.Dona);

        segunda.Onda.Should().Be(2);
        segunda.Enfileirados.Should().Be(1);
        segunda.PendentesRestantes.Should().Be(0);
        campanha.OndaAtual.Should().Be(2);
        campanha.Status.Should().Be(StatusCampanha.Enviando);
        _c.Destinatario(clientes[2]).Should().Match<CampanhaDestinatario>(d =>
            d.Status == StatusCampanhaDestinatario.Enfileirado && d.Onda == 2);
        _c.Mensagens.Should().HaveCount(3);

        // Sem pendentes, a dona não abre onda vazia.
        _c.OutboxProcessou();
        await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);
        var vazia = () => _c.Disparar().ExecuteAsync(_c.EmpresaId, campanha.Id, OrigemOndaCampanha.Dona);
        await vazia.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*pendente*");
    }

    [Fact]
    public async Task DisparoRevalidaConsentimentoETelefone()
    {
        var campanha = _c.Campanha();
        var ok = _c.Pendente(campanha, "Ana");
        var saiu = _c.Pendente(campanha, "Bia", consentiu: false);
        var telefoneRuim = _c.Pendente(campanha, "Cida", telefone: "123");

        var resultado = await _c.Disparar().ExecuteAsync(_c.EmpresaId, campanha.Id, OrigemOndaCampanha.Agendamento);

        resultado.Enfileirados.Should().Be(1);
        resultado.Excluidos.Should().Be(2);
        _c.Destinatario(ok).Status.Should().Be(StatusCampanhaDestinatario.Enfileirado);
        _c.Destinatario(saiu).MotivoExclusao.Should().Be(MotivoExclusaoCampanha.SemConsentimento,
            "quem disse SAIR depois do cálculo do público não recebe");
        _c.Destinatario(telefoneRuim).MotivoExclusao.Should().Be(MotivoExclusaoCampanha.SemTelefone);
    }

    [Fact]
    public async Task TemplateDaCampanhaVaiNosMetadados()
    {
        var campanha = _c.Campanha(templateMeta: "novidade_semana");
        var ana = _c.Pendente(campanha, "Ana");

        await _c.Disparar().ExecuteAsync(_c.EmpresaId, campanha.Id, OrigemOndaCampanha.Agendamento);

        var metadados = _c.Mensagens.Single().LerMetadados();
        metadados.Should().Equal(new Dictionary<string, string>
        {
            ["template"] = "novidade_semana",
            ["idioma"] = "pt_BR",
            ["param1"] = "Ana",
        });
        _c.Destinatario(ana).Status.Should().Be(StatusCampanhaDestinatario.Enfileirado);
    }

    [Fact]
    public async Task KillSwitchDoWhatsAppSeguraOEnvioSemMudarACampanha()
    {
        var campanha = _c.Campanha();
        _c.Pendente(campanha, "Ana");
        _c.Bloqueios.ListarAtivosAsync(null, Arg.Any<CanalNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns([BloqueioNotificacao.Criar("Meta suspendeu o número", "ops", canal: CanalNotificacao.WhatsApp)]);

        var disparar = () => _c.Disparar().ExecuteAsync(_c.EmpresaId, campanha.Id, OrigemOndaCampanha.Agendamento);

        await disparar.Should().ThrowAsync<EnvioCampanhaIndisponivelException>();
        _c.Mensagens.Should().BeEmpty();
        await _c.Uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task SemTemplateDeNotificacaoFalhaRegistradaSemEnfileirar()
    {
        var campanha = _c.Campanha();
        _c.Pendente(campanha, "Ana");
        _c.Templates.GetAtivoAsync(EnfileiradorMensagensCampanha.CodigoTemplateOnda, CanalNotificacao.WhatsApp, null, Arg.Any<CancellationToken>())
            .Returns((TemplateNotificacao?)null);

        var disparar = () => _c.Disparar().ExecuteAsync(_c.EmpresaId, campanha.Id, OrigemOndaCampanha.Agendamento);

        await disparar.Should().ThrowAsync<EnvioCampanhaIndisponivelException>()
            .WithMessage($"*{EnfileiradorMensagensCampanha.CodigoTemplateOnda}*");
        _c.Mensagens.Should().BeEmpty();
        await _c.Uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task CampanhaDeOutraEmpresa_NaoEncontrada()
    {
        var campanha = _c.Campanha();

        var disparar = () => _c.Disparar().ExecuteAsync(Guid.NewGuid(), campanha.Id, OrigemOndaCampanha.Dona);

        await disparar.Should().ThrowAsync<CampanhaNaoEncontradaException>();
    }
}
