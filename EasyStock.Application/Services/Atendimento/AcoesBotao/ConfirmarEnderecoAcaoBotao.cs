using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento.AcoesBotao;

/// <summary>
/// <c>acao:confirmar_endereco:&lt;enderecoId&gt;</c>. TODO(S14): gravar o <c>ClienteEndereco</c> padrão e
/// responder com taxa e tempo; até lá, o toque é registrado e a conversa vai para a dona.
/// </summary>
public sealed class ConfirmarEnderecoAcaoBotao(IEscaladorConversa escalador) : IAcaoBotaoHandler
{
    public string Nome => "confirmar_endereco";

    public Task ExecutarAsync(Guid empresaId, Conversa conversa, string payload, DateTime agora, CancellationToken ct = default) =>
        escalador.EscalarAsync(empresaId, conversa,
            $"cliente confirmou o endereço {payload} pelo botão; a confirmação automática chega com a S14",
            agora, ct);
}
