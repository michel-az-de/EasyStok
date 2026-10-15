using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.UseCases.Campanhas;

/// <param name="OndaDisparada">Número da onda que o horário agendado disparou, quando disparou.</param>
public sealed record ProcessamentoCampanhaResult(
    int? OndaDisparada, int Enviados, int Falhas, bool OndaConcluida, bool Encerrada, int Lembretes);

/// <summary>
/// Uma rodada do <c>CampanhaJob</c> numa campanha (S30): dispara a primeira onda quando chega o
/// <c>DisparoEm</c>; concilia os enfileirados com o outbox (enviado ou falhou) e conclui a onda quando
/// ninguém mais está na fila; no <c>EncerramentoEm</c>, encerra e, se pedido, manda o lembrete a
/// quem recebeu e não pediu (RN-43). Onda seguinte nunca sai daqui (RN-42).
/// </summary>
public sealed class ProcessarCampanhaUseCase(
    ICampanhaRepository repository,
    ICampanhaPublicoQueries queries,
    DispararOndaCampanhaUseCase dispararOnda,
    EnfileiradorMensagensCampanha enfileirador,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<ProcessamentoCampanhaResult> ExecuteAsync(Guid empresaId, Guid campanhaId, CancellationToken ct = default)
    {
        var campanha = await repository.ObterAsync(empresaId, campanhaId, ct)
            ?? throw new CampanhaNaoEncontradaException(campanhaId);
        var agora = relogio.GetUtcNow().UtcDateTime;

        int? ondaDisparada = null;
        if (campanha.Status == StatusCampanha.Agendada && campanha.DisparoEm <= agora)
            ondaDisparada = (await dispararOnda.ExecuteAsync(empresaId, campanhaId, OrigemOndaCampanha.Agendamento, ct)).Onda;

        var (enviados, falhas, naFila) = await ConciliarAsync(empresaId, campanhaId, ct);
        var mudou = enviados + falhas > 0;

        var ondaConcluida = false;
        if (campanha.Status == StatusCampanha.Enviando && naFila == 0)
        {
            campanha.ConcluirOnda();
            ondaConcluida = mudou = true;
        }

        var encerrada = false;
        var lembretes = 0;
        if (campanha.Status is StatusCampanha.Enviando or StatusCampanha.Enviada && campanha.EncerramentoEm <= agora)
        {
            campanha.Encerrar();
            encerrada = mudou = true;
            if (campanha.EnviarLembreteEncerramento)
                lembretes = await EnfileirarLembretesAsync(campanha, agora, ct);
        }

        if (mudou) await unitOfWork.CommitAsync();
        return new ProcessamentoCampanhaResult(ondaDisparada, enviados, falhas, ondaConcluida, encerrada, lembretes);
    }

    /// <summary>
    /// Traz o resultado do outbox para os enfileirados: enviado vira <c>Enviado</c> com o horário do
    /// envio; falha, supressão, cancelamento ou mensagem expurgada viram <c>Falhou</c>. Devolve quantos
    /// seguem na fila.
    /// </summary>
    private async Task<(int Enviados, int Falhas, int NaFila)> ConciliarAsync(Guid empresaId, Guid campanhaId, CancellationToken ct)
    {
        int enviados = 0, falhas = 0, naFila = 0;
        foreach (var (destinatario, mensagem) in await repository.ListarEnfileiradosAsync(empresaId, campanhaId, ct))
        {
            switch (mensagem?.Status)
            {
                case StatusOutbox.Pendente or StatusOutbox.EmEnvio:
                    naFila++;
                    break;
                case StatusOutbox.Enviado:
                    destinatario.MarcarEnviado(mensagem.EnviadoEm ?? relogio.GetUtcNow().UtcDateTime);
                    enviados++;
                    break;
                default:
                    destinatario.MarcarFalhou();
                    falhas++;
                    break;
            }
        }

        return (enviados, falhas, naFila);
    }

    /// <summary>
    /// RN-43: lembrete para quem recebeu e não pediu. É marketing: bloqueio, consentimento (SAIR) e
    /// telefone são conferidos de novo; o limite semanal não vale, a campanha é a mesma.
    /// </summary>
    private async Task<int> EnfileirarLembretesAsync(Campanha campanha, DateTime agora, CancellationToken ct)
    {
        var receberam = await repository.ListarEnviadosAsync(campanha.EmpresaId, campanha.Id, ct);
        if (receberam.Count == 0) return 0;

        var candidatos = (await queries.ListarCandidatosAsync(campanha.EmpresaId, campanha.Id, null, ct))
            .ToDictionary(c => c.ClienteId);
        var contatos = new List<ContatoCampanha>();
        foreach (var destinatario in receberam.Where(d => d.PedidoId is null))
        {
            if (!candidatos.TryGetValue(destinatario.ClienteId, out var cliente)) continue;
            if (cliente.Bloqueado || !cliente.ConsentiuMarketing) continue;
            if (DispararOndaCampanhaUseCase.TelefoneE164(cliente.Telefone) is not { } telefone) continue;
            contatos.Add(new ContatoCampanha(cliente.ClienteId, cliente.Nome, telefone));
        }

        await enfileirador.EnfileirarAsync(campanha, MensagemCampanha.LembreteEncerramento, contatos, agora, agora, ct);
        return contatos.Count;
    }
}
