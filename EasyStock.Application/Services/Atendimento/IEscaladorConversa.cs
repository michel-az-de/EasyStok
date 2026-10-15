using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Passa a conversa para a dona (ferramenta <c>escalar_para_dona</c>, limite de iterações do
/// agente e ações de botão ainda sem handler automático). Não faz commit: quem chama confirma.
/// Implementação: <see cref="UseCases.Atendimento.EscalarConversaUseCase"/> (S07).
/// </summary>
public interface IEscaladorConversa
{
    Task EscalarAsync(Guid empresaId, Conversa conversa, string motivo, DateTime agora, CancellationToken ct = default);
}
