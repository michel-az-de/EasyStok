using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Adapter que expoe <see cref="IEfiPixService"/> via a abstracao generica
/// <see cref="IPagamentoGateway"/>. Mantem o servico legado intacto — apenas
/// envolve as chamadas e converte tipos.
///
/// <para>
/// Limitacoes atuais (refletem a interface <c>IEfiPixService</c>):
/// </para>
/// <list type="bullet">
///   <item><c>ConsultarAsync</c> retorna <see cref="StatusGateway.Desconhecido"/> —
///   o servico legado nao expoe consulta. Pode ser estendido em F6 (reconciliacao)
///   adicionando endpoint <c>GET /v2/cob/{txid}</c> ao <see cref="IEfiPixService"/>.</item>
///   <item><c>EstornarAsync</c> retorna falha com mensagem — Pix permite estorno
///   ate 90 dias mas requer endpoint dedicado (<c>POST /v2/pix/{e2eId}/devolucao</c>).
///   Implementar quando a feature for solicitada.</item>
/// </list>
/// </summary>
public sealed class EfiPixGatewayAdapter(IEfiPixService pixService) : IPagamentoGateway
{
    public string Provedor => "EfiPix";

    public bool SuportaMetodo(string metodo) =>
        string.Equals(metodo, "pix", StringComparison.OrdinalIgnoreCase);

    public async Task<InstrucaoPagamento> CriarAsync(Fatura fatura, string metodo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fatura);
        if (!SuportaMetodo(metodo))
            throw new NotSupportedException($"EfiPix nao suporta metodo '{metodo}'.");

        // Txid Efi: aceita 26-35 chars alfanum. Usamos o Guid sem hifens, 32 chars.
        var txid = fatura.Id.ToString("N");
        var descricao = $"Fatura {fatura.Numero}";

        var efi = await pixService.CriarCobrancaAsync(txid, fatura.Total, descricao, ct);

        return new InstrucaoPagamento(
            Provedor: Provedor,
            TransactionId: efi.Txid,
            PixCopiaCola: efi.PixCopiaCola,
            QrCodeBase64: efi.QrCodeBase64,
            ExpiracaoEm: efi.ExpiracaoEm,
            DadosGatewayJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                txid = efi.Txid,
                pix_copia_cola = efi.PixCopiaCola,
                qr_code = efi.QrCodeBase64,
                expiracao_em = efi.ExpiracaoEm
            })
        );
    }

    public Task<StatusGateway> ConsultarAsync(string transactionId, CancellationToken ct = default)
    {
        // Servico legado nao expoe consulta — F6 deve estender IEfiPixService
        // com GetCobrancaAsync(txid) e atualizar este metodo.
        return Task.FromResult(StatusGateway.Desconhecido);
    }

    public Task<EstornoResult> EstornarAsync(string transactionId, decimal valor, CancellationToken ct = default)
    {
        return Task.FromResult(new EstornoResult(
            Sucesso: false,
            Mensagem: "Estorno via Pix Efi nao implementado nesta versao. Use o portal Efi diretamente."
        ));
    }
}
