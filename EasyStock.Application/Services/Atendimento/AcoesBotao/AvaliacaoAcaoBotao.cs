using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.Services.Atendimento.AcoesBotao;

/// <summary>
/// <c>acao:avaliacao:positiva|negativa:&lt;pedidoId&gt;</c> (S26): avaliação de um toque, sem LLM. Grava a
/// <c>PedidoAvaliacao</c>, agradece (positiva) ou avisa que a dona vai falar com o cliente (negativa).
/// TODO(S27): a negativa abre uma <c>Ocorrencia</c>; até a S27 entrar, a conversa passa para a dona.
/// Não faz commit: o webhook confirma depois do roteador.
/// </summary>
public sealed class AvaliacaoAcaoBotao(
    RegistrarAvaliacaoSimplesUseCase registrar,
    IEscaladorConversa escalador,
    ResolvedorCanal canais,
    IConversaRepository conversas,
    ILogger<AvaliacaoAcaoBotao> logger) : IAcaoBotaoHandler
{
    public const string NomeAcao = "avaliacao";
    public const string RespostaPositiva = "Que bom que você gostou! Obrigada pela avaliação.";
    public const string RespostaNegativa = "Sinto muito que não foi como esperado. A Tatiana vai falar com você.";
    public const string RespostaDuplicada = "Você já avaliou esse pedido. Obrigada!";
    public const string MotivoEscalada = "cliente avaliou o pedido como negativo";

    public string Nome => NomeAcao;

    public async Task ExecutarAsync(Guid empresaId, Conversa conversa, string payload, DateTime agora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conversa);

        var partes = (payload ?? string.Empty).Split(':', 2);
        if (partes.Length != 2
            || !RegistrarAvaliacaoSimplesUseCase.TryInterpretarResultado(partes[0], out var resultado)
            || !Guid.TryParse(partes[1], out var pedidoId)
            || conversa.ClienteId is not { } clienteId)
        {
            logger.LogWarning("Botão de avaliação inválido ou sem cliente na conversa {ConversaId}.", conversa.Id);
            await escalador.EscalarAsync(empresaId, conversa, "botão de avaliação sem pedido reconhecido", agora, ct);
            return;
        }

        string resposta;
        try
        {
            await registrar.ExecutarAsync(empresaId, clienteId, pedidoId, resultado, null, agora, ct);
            resposta = resultado == ResultadoAvaliacao.Positiva ? RespostaPositiva : RespostaNegativa;
            if (resultado == ResultadoAvaliacao.Negativa)
                await escalador.EscalarAsync(empresaId, conversa, MotivoEscalada, agora, ct);
        }
        catch (AvaliacaoDuplicadaException)
        {
            resposta = RespostaDuplicada;
        }
        catch (Exception ex) when (ex is StorefrontNaoEncontradoException or PedidoNaoElegivelParaAvaliacaoException)
        {
            logger.LogWarning(ex, "Avaliação por botão recusada na conversa {ConversaId}.", conversa.Id);
            await escalador.EscalarAsync(empresaId, conversa, "botão de avaliação para pedido não elegível", agora, ct);
            return;
        }

        await ResponderAsync(empresaId, conversa, resposta, ct);
    }

    private async Task ResponderAsync(Guid empresaId, Conversa conversa, string texto, CancellationToken ct)
    {
        Mensagem saida;
        try
        {
            var id = await canais.Obter(conversa.Canal).EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct);
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, DateTime.UtcNow, TipoConteudoMensagem.Texto, texto, id);
        }
        catch (Exception ex)
        {
            // A avaliação já foi gravada: a resposta é cortesia e não desfaz o registro.
            logger.LogWarning(ex, "Avaliação gravada, mas a resposta não saiu na conversa {ConversaId}.", conversa.Id);
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, DateTime.UtcNow, TipoConteudoMensagem.Texto, texto);
            saida.RegistrarFalhaEnvio(ex.Message, ClassificadorFalhaEnvio.Classificar(ex), saida.EnviadaEm);
        }

        conversa.RegistrarSaida(saida.EnviadaEm);
        await conversas.AddMensagemAsync(saida, ct);
    }
}
