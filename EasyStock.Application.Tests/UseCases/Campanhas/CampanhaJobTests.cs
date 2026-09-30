using EasyStock.Application.Services.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.UseCases.Campanhas;

/// <summary>
/// S30: a rodada do <c>CampanhaJob</c> (<see cref="Application.UseCases.Campanhas.ProcessarCampanhaUseCase"/>)
/// dispara a primeira onda no horário, concilia o outbox e, no encerramento, lembra só quem recebeu e
/// não pediu (RN-43).
/// </summary>
public class CampanhaJobTests
{
    private readonly CenarioDisparoCampanha _c = new();

    [Fact]
    public async Task LembreteSoParaQuemNaoPediu()
    {
        var encerramento = CenarioDisparoCampanha.Disparo.AddDays(3);
        var campanha = _c.Campanha(encerramentoEm: encerramento, lembrete: true);
        var pediu = _c.Pendente(campanha, "Ana");
        var naoPediu = _c.Pendente(campanha, "Bia");
        var falhou = _c.Pendente(campanha, "Cida");
        var saiuDepois = _c.Pendente(campanha, "Dora");

        _c.Relogio.SetUtcNow(CenarioDisparoCampanha.Disparo);
        (await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id)).OndaDisparada.Should().Be(1);

        // Cida não recebe (Meta recusou); as outras três recebem.
        _c.Mensagens.Single(m => m.Id == _c.Destinatario(falhou).OutboxMensagemId)
            .MarcarFalhaTentativa("132001 template inexistente", TimeSpan.Zero, permanente: true);
        _c.OutboxProcessou();
        _c.Relogio.Advance(TimeSpan.FromMinutes(1));
        var conciliacao = await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);
        conciliacao.Enviados.Should().Be(3);
        conciliacao.Falhas.Should().Be(1);
        _c.Destinatario(falhou).Status.Should().Be(StatusCampanhaDestinatario.Falhou);
        _c.Destinatario(naoPediu).EnviadoEm.Should().NotBeNull();

        _c.Destinatario(pediu).RegistrarPedido(Guid.NewGuid());
        // Dora respondeu SAIR depois de receber: o lembrete também é marketing.
        _c.Candidatos[_c.Candidatos.IndexOf(saiuDepois)] = saiuDepois with { ConsentiuMarketing = false };

        _c.Relogio.SetUtcNow(encerramento.AddMinutes(-1));
        (await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id)).Encerrada.Should().BeFalse();

        var mensagensAntes = _c.Mensagens.Count;
        _c.Relogio.SetUtcNow(encerramento);
        var rodada = await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);

        rodada.Encerrada.Should().BeTrue();
        rodada.Lembretes.Should().Be(1);
        campanha.Status.Should().Be(StatusCampanha.Encerrada);
        var lembretes = _c.Mensagens.Skip(mensagensAntes).ToList();
        var lembrete = lembretes.Should().ContainSingle().Subject;
        lembrete.CorpoRenderizado.Should().Be("Bia, a campanha Bolo de fubá está terminando.");
        lembrete.Categoria.Should().Be(CategoriaConteudoNotificacao.Marketing);
        lembrete.ProximaTentativaEm.Should().Be(encerramento);
        lembrete.LerMetadados().Should().Contain("template", EnfileiradorMensagensCampanha.TemplateMetaLembrete);
        _c.EventosCriados.Last().Tipo.Should().Be(TipoEventoNotificacao.CampanhaLembreteEncerramento);

        // Encerrada não repete o lembrete.
        (await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id)).Lembretes.Should().Be(0);
        _c.Mensagens.Should().HaveCount(mensagensAntes + 1);
    }

    [Fact]
    public async Task EncerramentoSemLembreteSoEncerra()
    {
        var encerramento = CenarioDisparoCampanha.Disparo.AddDays(1);
        var campanha = _c.Campanha(encerramentoEm: encerramento);
        _c.Pendente(campanha, "Ana");
        _c.Relogio.SetUtcNow(CenarioDisparoCampanha.Disparo);
        await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);
        _c.OutboxProcessou();

        _c.Relogio.SetUtcNow(encerramento);
        var rodada = await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);

        rodada.Encerrada.Should().BeTrue();
        rodada.Lembretes.Should().Be(0);
        campanha.Status.Should().Be(StatusCampanha.Encerrada);
        _c.Mensagens.Should().ContainSingle();
    }

    [Fact]
    public async Task AntesDoDisparoNaoFazNada()
    {
        var campanha = _c.Campanha();
        _c.Pendente(campanha, "Ana");
        _c.Relogio.SetUtcNow(CenarioDisparoCampanha.Disparo.AddSeconds(-1));

        var rodada = await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);

        rodada.OndaDisparada.Should().BeNull();
        campanha.Status.Should().Be(StatusCampanha.Agendada);
        _c.Mensagens.Should().BeEmpty();
        await _c.Uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task CancelarTiraDaFilaQuemAindaNaoSaiu()
    {
        var campanha = _c.Campanha(tamanhoOnda: 2);
        var saiu = _c.Pendente(campanha, "Ana");
        var naFila = _c.Pendente(campanha, "Bia");
        var pendente = _c.Pendente(campanha, "Cida");
        _c.Relogio.SetUtcNow(CenarioDisparoCampanha.Disparo);
        await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);
        var mensagemAna = _c.Mensagens.Single(m => m.Id == _c.Destinatario(saiu).OutboxMensagemId);
        mensagemAna.MarcarEnviado("whatsapp:meta");
        var mensagemBia = _c.Mensagens.Single(m => m.Id == _c.Destinatario(naFila).OutboxMensagemId);

        var resultado = await new Application.UseCases.Campanhas.CancelarCampanhaUseCase(_c.Repo, _c.Uow)
            .ExecuteAsync(_c.EmpresaId, campanha.Id);

        resultado.DestinatariosExcluidos.Should().Be(1);
        resultado.MensagensCanceladas.Should().Be(1);
        mensagemBia.Status.Should().Be(StatusOutbox.Cancelado);
        _c.Destinatario(naFila).MotivoExclusao.Should().Be(MotivoExclusaoCampanha.Cancelada);
        _c.Destinatario(pendente).MotivoExclusao.Should().Be(MotivoExclusaoCampanha.Cancelada);
        mensagemAna.Status.Should().Be(StatusOutbox.Enviado);
        _c.Destinatario(saiu).Status.Should().Be(StatusCampanhaDestinatario.Enfileirado,
            "a mensagem já saiu: o job concilia como enviada");

        await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);
        _c.Destinatario(saiu).Status.Should().Be(StatusCampanhaDestinatario.Enviado);
        campanha.Status.Should().Be(StatusCampanha.Cancelada);
    }
}
