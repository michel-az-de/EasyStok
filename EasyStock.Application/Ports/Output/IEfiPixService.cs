namespace EasyStock.Application.Ports.Output;

public record EfiCobrancaResult(
    string Txid,
    string PixCopiaCola,
    string QrCodeBase64,
    DateTime ExpiracaoEm);

public interface IEfiPixService
{
    Task<EfiCobrancaResult> CriarCobrancaAsync(
        string txid,
        decimal valor,
        string descricao,
        CancellationToken ct = default);
}
