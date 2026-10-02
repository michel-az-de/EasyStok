using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Reenvio;

/// <summary>
/// S57 (#1355): reenvia um texto que falhou, pelo botão do console ou pelo serviço de fundo. Se falhar de novo, a
/// falha é registrada e, sendo temporária, reagendada pela próxima espera.
///
/// <para>
/// S58 (#1391): a janela que vale é a da conversa da mensagem ou, se o cliente escreveu de novo, a da conversa aberta
/// dele. Fora da janela (pelo relógio ou pela Meta, 131047), com modelo de retomada configurado, o modelo sai uma vez
/// por contato e a mensagem espera o cliente responder; sem modelo, a falha é permanente com o motivo.
/// S60: quando o WhatsApp desiste, a reserva por SMS tenta (se estiver ligada).
/// </para>
/// </summary>
public sealed class ReenviarMensagemUseCase(
    IConversaRepository conversaRepository,
    IConfiguracaoAtendimentoRepository configuracoes,
    ResolvedorCanal resolvedorCanal,
    ReservaSmsAtendimento reservaSms,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public const string MotivoForaDaJanela = "Fora da janela de 24 h: só um modelo aprovado alcança o cliente.";
    public const string MotivoAguardandoCliente = "Fora da janela de 24 h: o modelo de retomada saiu; reenvia quando o cliente responder.";

    /// <summary>Um modelo de retomada por contato a cada janela: outra mensagem que falhar só entra na espera.</summary>
    private static readonly TimeSpan IntervaloEntreModelos = TimeSpan.FromHours(24);

    public async Task<MensagemAtendimentoResult> ExecuteAsync(
        Guid empresaId, Guid conversaId, Guid mensagemId, CancellationToken ct = default)
    {
        var mensagem = await conversaRepository.ObterMensagemParaAlterarAsync(empresaId, conversaId, mensagemId, ct)
            ?? throw new MensagemNaoEncontradaException(mensagemId);
        if (!mensagem.PodeReenviar)
            throw new UseCaseValidationException("Só texto que falhou pode ser reenviado.");
        var conversa = await conversaRepository.ObterPorIdAsync(empresaId, conversaId, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);

        var agora = relogio.GetUtcNow().UtcDateTime;
        var destino = await DestinoDentroDaJanelaAsync(empresaId, conversa, agora, ct);
        if (destino is null)
            await ForaDaJanelaAsync(empresaId, conversa, mensagem, agora, ct);
        else
            await EnviarAsync(empresaId, destino, mensagem, agora, ct);

        await reservaSms.TentarAsync(conversa, mensagem, agora, ct);
        await unitOfWork.CommitAsync();
        return MensagemAtendimentoResult.De(mensagem);
    }

    private async Task<Conversa?> DestinoDentroDaJanelaAsync(Guid empresaId, Conversa conversa, DateTime agora, CancellationToken ct)
    {
        if (conversa.DentroDaJanela(agora)) return conversa;
        var aberta = await conversaRepository.ObterAbertaPorContatoAsync(empresaId, conversa.Canal, conversa.ContatoIdExterno, ct);
        return aberta is not null && aberta.DentroDaJanela(agora) ? aberta : null;
    }

    private async Task EnviarAsync(Guid empresaId, Conversa destino, Mensagem mensagem, DateTime agora, CancellationToken ct)
    {
        mensagem.ReservarReenvio();
        try
        {
            var externoId = await resolvedorCanal.Obter(destino.Canal)
                .EnviarTextoAsync(destino.ContatoIdExterno, mensagem.Texto!, ct);
            mensagem.RegistrarReenviada(externoId);
        }
        catch (WhatsAppCloudException ex) when (ex.Codigo == WhatsAppCloudException.CodigoForaDaJanela)
        {
            await ForaDaJanelaAsync(empresaId, destino, mensagem, agora, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            mensagem.RegistrarFalhaEnvio(ex.Message, ClassificadorFalhaEnvio.Classificar(ex), agora);
        }
    }

    private async Task ForaDaJanelaAsync(Guid empresaId, Conversa conversa, Mensagem mensagem, DateTime agora, CancellationToken ct)
    {
        var modelo = (await configuracoes.GetOrDefaultAsync(empresaId)).ModeloRetomada;
        if (modelo is null || !conversa.Capacidades.AceitaModelo)
        {
            mensagem.RegistrarFalhaEnvio(MotivoForaDaJanela, TipoFalhaEnvio.Permanente, agora);
            return;
        }

        var jaEnviado = await conversaRepository.ExisteAguardandoClienteAsync(empresaId, conversa.Canal,
            conversa.ContatoIdExterno, conversa.ClienteId, agora - IntervaloEntreModelos, ct);
        if (!jaEnviado)
        {
            try
            {
                var externoId = await resolvedorCanal.Obter(conversa.Canal).EnviarModeloAsync(
                    conversa.ContatoIdExterno, modelo.Nome, modelo.Idioma, [PrimeiroNome(conversa.ContatoNome)], ct);
                var aviso = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, agora,
                    TipoConteudoMensagem.Texto, $"[modelo {modelo.Nome}]", externoId);
                await conversaRepository.AddMensagemAsync(aviso, ct);
                if (conversa.EstaAberta) conversa.RegistrarSaida(agora);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                mensagem.RegistrarFalhaEnvio($"Fora da janela de 24 h e o modelo de retomada falhou: {ex.Message}",
                    TipoFalhaEnvio.Permanente, agora);
                return;
            }
        }

        mensagem.AguardarCliente(agora, MotivoAguardandoCliente);
    }

    private static string PrimeiroNome(string? nome) =>
        string.IsNullOrWhiteSpace(nome) ? "cliente" : nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
}

/// <summary>Mensagem inexistente, de outra conversa ou de outra empresa: 404.</summary>
public sealed class MensagemNaoEncontradaException(Guid mensagemId)
    : Exception($"Mensagem {mensagemId} não encontrada.")
{
    public Guid MensagemId { get; } = mensagemId;
}
