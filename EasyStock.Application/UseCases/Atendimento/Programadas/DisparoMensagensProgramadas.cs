using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Programadas;

public sealed record MensagemReservada(Guid EmpresaId, Guid Id);

/// <summary>
/// S39, passo 1 do disparo (cross-tenant, o host liga o bypass de RLS): pega as agendadas vencidas
/// com <c>FOR UPDATE SKIP LOCKED</c> e as passa para <c>Enviando</c> na mesma transação. Dois
/// disparadores nunca pegam a mesma mensagem.
/// </summary>
public sealed class ReservarMensagensProgramadasUseCase(
    IMensagemProgramadaRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public Task<IReadOnlyList<MensagemReservada>> ExecuteAsync(int limite, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionSemRetryAsync<IReadOnlyList<MensagemReservada>>(async token =>
        {
            var agora = relogio.GetUtcNow().UtcDateTime;
            var vencidas = await repository.ListarVencidasComLockAsync(agora, limite, token);
            foreach (var mensagem in vencidas)
                mensagem.Reservar(agora);
            await unitOfWork.CommitAsync();
            return vencidas.Select(m => new MensagemReservada(m.EmpresaId, m.Id)).ToList();
        }, ct);
}

/// <summary>
/// S39, passo 2 (escopo com o tenant da mensagem): confere de novo destino, janela e consentimento,
/// envia pela porta do canal (S34/S37) e grava o resultado. Com conversa aberta, a saída entra no
/// histórico com o selo <see cref="Mensagem.Programada"/>. Nunca lança: falha vira <c>Falhou</c>.
/// </summary>
public sealed class DispararMensagemProgramadaUseCase(
    IMensagemProgramadaRepository repository,
    IClienteRepository clientes,
    IConversaRepository conversas,
    PoliticaEnvioCliente politica,
    ResolvedorCanal canais,
    IOperacaoEventPublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<DispararMensagemProgramadaUseCase> logger)
{
    public async Task ExecuteAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        var mensagem = await repository.ObterAsync(empresaId, id, ct);
        if (mensagem is null || mensagem.Situacao != SituacaoMensagemProgramada.Enviando) return;

        var agora = relogio.GetUtcNow().UtcDateTime;
        Conversa? conversa = null;
        try
        {
            var cliente = await clientes.GetByIdAsync(empresaId, mensagem.ClienteId)
                ?? throw new RegraDeDominioVioladaException("Cliente não existe mais.");
            if (cliente.Bloqueado)
                throw new RegraDeDominioVioladaException("Cliente bloqueado: mensagem programada não enviada.");
            conversa = await DestinoMensagemProgramada.ConversaAsync(conversas, empresaId, cliente, mensagem.Canal, mensagem.ConversaId, ct);
            var contato = DestinoMensagemProgramada.Contato(cliente, mensagem.Canal, conversa);

            // A janela pode ter vencido desde o agendamento; o cliente pode ter dito SAIR.
            mensagem.GarantirPodeSairPor(conversa, agora);
            if (!await politica.PodeEnviarAsync(empresaId, mensagem.ClienteId, mensagem.Canal, mensagem.Finalidade, ct))
                throw new RegraDeDominioVioladaException("O cliente revogou o consentimento neste canal.");

            var canal = canais.Obter(mensagem.Canal);
            var idExterno = mensagem.Modelo is { } modelo
                ? await canal.EnviarModeloAsync(contato, modelo.Nome, modelo.Idioma, modelo.Parametros, ct)
                : await canal.EnviarTextoAsync(contato, mensagem.Texto!, ct);

            mensagem.RegistrarEnvio(idExterno, agora);
            if (conversa is not null)
                await RegistrarNoHistoricoAsync(empresaId, conversa, mensagem, idExterno, agora, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mensagem programada {Id} não saiu.", id);
            mensagem.RegistrarFalha(ex.Message, agora);
        }

        await unitOfWork.CommitAsync();

        await publisher.PublicarAsync(
            mensagem.Situacao == SituacaoMensagemProgramada.Enviada ? "mensagem_programada.enviada" : "mensagem_programada.falhou",
            empresaId, new { id = mensagem.Id, clienteId = mensagem.ClienteId, conversaId = conversa?.Id, erro = mensagem.Erro }, ct);
    }

    private async Task RegistrarNoHistoricoAsync(
        Guid empresaId, Conversa conversa, MensagemProgramada mensagem, string idExterno, DateTime agora, CancellationToken ct)
    {
        var texto = mensagem.Texto ?? $"[modelo {mensagem.ModeloNome}]";
        var saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Dona, agora, TipoConteudoMensagem.Texto, texto, idExterno);
        saida.MarcarComoProgramada();
        conversa.RegistrarSaida(agora);
        await conversas.AddMensagemAsync(saida, ct);
    }
}
