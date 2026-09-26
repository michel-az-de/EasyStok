using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento.AcoesBotao;

/// <summary>Handler de uma ação de botão <c>acao:&lt;Nome&gt;:&lt;payload&gt;</c>. Não faz commit.</summary>
public interface IAcaoBotaoHandler
{
    string Nome { get; }

    Task ExecutarAsync(Guid empresaId, Conversa conversa, string payload, DateTime agora, CancellationToken ct = default);
}
