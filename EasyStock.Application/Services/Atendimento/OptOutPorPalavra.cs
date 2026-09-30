using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// "SAIR", "PARAR" ou "STOP" sozinhos na mensagem (S38): revoga o marketing do canal de onde veio,
/// confirma ao cliente e o turno do agente não roda. Avisos do pedido (transacional) continuam.
/// </summary>
public sealed class OptOutPorPalavra(
    IConsentimentoContatoRepository consentimentos,
    IConversaRepository conversas,
    ResolvedorCanal canais,
    IUnitOfWork unitOfWork,
    ILogger<OptOutPorPalavra> logger)
{
    public const string OrigemPalavra = "palavra_sair";
    public const string Confirmacao =
        "Pronto! Você não vai mais receber novidades por aqui. Avisos do seu pedido continuam chegando normalmente.";

    /// <returns><c>true</c> quando era opt-out e foi tratado (o chamador não aciona o agente).</returns>
    public async Task<bool> TentarAsync(Guid empresaId, Conversa conversa, string? texto, DateTime agoraUtc, CancellationToken ct = default)
    {
        if (!PoliticaConsentimento.EhPedidoDeOptOut(texto) || conversa.ClienteId is not { } clienteId)
            return false;

        var existentes = await consentimentos.ListarDoClienteAsync(empresaId, clienteId, ct);
        var marketing = existentes.FirstOrDefault(c => c.Canal == conversa.Canal && c.Finalidade == FinalidadeContato.Marketing);
        if (marketing is null)
            await consentimentos.AddAsync(ConsentimentoContato.Registrar(empresaId, clienteId, conversa.Canal,
                FinalidadeContato.Marketing, SituacaoConsentimento.Revogado, OrigemPalavra, agoraUtc), ct);
        else
            marketing.Alterar(SituacaoConsentimento.Revogado, OrigemPalavra, agoraUtc);

        await unitOfWork.CommitAsync();
        await ConfirmarAsync(empresaId, conversa, ct);
        return true;
    }

    private async Task ConfirmarAsync(Guid empresaId, Conversa conversa, CancellationToken ct)
    {
        Mensagem saida;
        try
        {
            var id = await canais.Obter(conversa.Canal).EnviarTextoAsync(conversa.ContatoIdExterno, Confirmacao, ct);
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, DateTime.UtcNow, TipoConteudoMensagem.Texto, Confirmacao, id);
        }
        catch (Exception ex)
        {
            // O consentimento já foi revogado: a confirmação é cortesia e não desfaz a revogação.
            logger.LogWarning(ex, "Opt-out gravado, mas a confirmação não saiu na conversa {ConversaId}.", conversa.Id);
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, DateTime.UtcNow, TipoConteudoMensagem.Texto, Confirmacao);
            saida.AtualizarStatusEntrega(StatusMensagem.Falhou, ex.Message);
        }

        conversa.RegistrarSaida(saida.EnviadaEm);
        await conversas.AddMensagemAsync(saida, ct);
        await unitOfWork.CommitAsync();
    }
}
