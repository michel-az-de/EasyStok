using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Gateway "manual": admin marca o pagamento como recebido sem chamar
/// nenhum provedor externo. Cobre dinheiro, transferencia bancaria,
/// cheque, etc.
///
/// <para>
/// <see cref="CriarAsync"/> retorna instrucao vazia — o admin/usuario ja
/// sabe como pagar (combinacao fora-do-app). <see cref="ConsultarAsync"/>
/// sempre retorna <see cref="StatusGateway.Pendente"/> ate o admin marcar
/// manualmente. Sem webhook nem estorno automatico.
/// </para>
/// </summary>
public sealed class ManualGatewayAdapter : IPagamentoGateway
{
    private static readonly string[] MetodosManual = { "manual", "dinheiro", "transferencia", "cheque", "outro" };

    public string Provedor => "Manual";

    public bool SuportaMetodo(string metodo) =>
        MetodosManual.Contains(metodo?.ToLowerInvariant() ?? "", StringComparer.OrdinalIgnoreCase);

    public Task<InstrucaoPagamento> CriarAsync(Fatura fatura, string metodo, CancellationToken ct = default)
    {
        var txid = $"manual-{fatura.Id:N}";
        return Task.FromResult(new InstrucaoPagamento(
            Provedor: Provedor,
            TransactionId: txid,
            DadosGatewayJson: System.Text.Json.JsonSerializer.Serialize(new { metodo })
        ));
    }

    public Task<StatusGateway> ConsultarAsync(string transactionId, CancellationToken ct = default) =>
        Task.FromResult(StatusGateway.Pendente);

    public Task<EstornoResult> EstornarAsync(string transactionId, decimal valor, CancellationToken ct = default) =>
        Task.FromResult(new EstornoResult(
            Sucesso: true,
            ProtocoloEstorno: $"manual-rev-{Guid.NewGuid():N}",
            Mensagem: "Estorno manual — admin deve atualizar o pagamento na fatura."));
}
