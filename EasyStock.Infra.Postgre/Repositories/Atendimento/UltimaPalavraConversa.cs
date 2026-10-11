using System.Linq.Expressions;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <summary>
/// Critério único de "quem falou por último" numa conversa, usado pelo lembrete de cliente sem resposta
/// (#1550) e pelo "aguardando resposta" da inbox (#1562).
/// </summary>
internal static class UltimaPalavraConversa
{
    /// <summary>
    /// Falha de envio e nota interna do sistema (ex.: "escalado para a dona") não respondem ao cliente e ficam
    /// de fora. A exceção é a nota de reabertura: ela conta como a última palavra, senão a entrada de antes do
    /// encerramento voltaria como cliente esperando há dias. Entrada que chega depois da reabertura é mais nova
    /// que a nota e conta normalmente.
    /// </summary>
    public static readonly Expression<Func<Mensagem, bool>> Conta = m =>
        (m.Status != StatusMensagem.Falhou && m.Status != StatusMensagem.Pendente)
        || (m.Autor == AutorMensagem.Sistema && m.EnviadaPorUsuarioId != null
            && m.Texto!.StartsWith(ReabrirConversaUseCase.PrefixoNotaReabertura));
}
