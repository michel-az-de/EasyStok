using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.UseCases.Campanhas;

/// <summary>
/// N2: a conciliação da campanha (<see cref="Application.UseCases.Campanhas.ProcessarCampanhaUseCase"/>) só conta
/// como enviada a mensagem <see cref="StatusOutbox.Enviado"/>. Todo status terminal que não é entrega
/// (<see cref="StatusOutbox.Simulado"/>, <see cref="StatusOutbox.Indeterminado"/> e o <c>Expirado</c> da N1) cai no
/// <c>default</c> da conciliação e conta como falha: nada saiu, ou não há como confirmar.
/// </summary>
public class ProcessarCampanhaUseCaseTests
{
    private readonly CenarioDisparoCampanha _c = new();

    private OutboxMensagemNotificacao MensagemDe(CampanhaDestinatario destinatario) =>
        _c.Mensagens.Single(m => m.Id == destinatario.OutboxMensagemId);

    [Fact]
    public async Task Simulado_expirado_e_indeterminado_contam_como_falha()
    {
        // Percorre o enum em vez de listar os status: o Expirado entra no teste sozinho quando a N1 o criar.
        var semEntrega = Enum.GetValues<StatusOutbox>()
            .Where(s => s is not (StatusOutbox.Pendente or StatusOutbox.EmEnvio or StatusOutbox.Enviado))
            .ToList();
        semEntrega.Should().Contain([StatusOutbox.Simulado, StatusOutbox.Indeterminado]);

        var campanha = _c.Campanha();
        var entregue = _c.Pendente(campanha, "Entregue");
        var clientes = semEntrega.ToDictionary(status => status, status => _c.Pendente(campanha, status.ToString()));

        _c.Relogio.SetUtcNow(CenarioDisparoCampanha.Disparo);
        await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);

        // O dispatcher decidiu o desfecho de cada mensagem enfileirada.
        MensagemDe(_c.Destinatario(entregue)).MarcarEnviado("whatsapp:meta");
        foreach (var (status, cliente) in clientes)
            MensagemDe(_c.Destinatario(cliente)).Status = status;

        _c.Relogio.Advance(TimeSpan.FromMinutes(1));
        var conciliacao = await _c.Job().ExecuteAsync(_c.EmpresaId, campanha.Id);

        conciliacao.Enviados.Should().Be(1, "só a mensagem Enviado conta como entregue");
        conciliacao.Falhas.Should().Be(semEntrega.Count);
        conciliacao.OndaConcluida.Should().BeTrue("ninguém mais está na fila");
        _c.Destinatario(entregue).Status.Should().Be(StatusCampanhaDestinatario.Enviado);
        foreach (var (status, cliente) in clientes)
        {
            var destinatario = _c.Destinatario(cliente);
            destinatario.Status.Should().Be(StatusCampanhaDestinatario.Falhou, status.ToString());
            destinatario.EnviadoEm.Should().BeNull(status.ToString());
        }
    }
}
