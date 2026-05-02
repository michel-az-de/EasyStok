namespace EasyStock.Application.Ports.Output;

public sealed record EfiBoletoResult(
    string Txid,
    string BoletoCodigo,
    string? BoletoUrl,
    DateTime ExpiracaoEm);

public interface IEfiBoletoService
{
    Task<EfiBoletoResult> CriarBoletoAsync(
        string txid,
        decimal valor,
        string descricao,
        string nomeDevedor,
        string cpfCnpjDevedor,
        CancellationToken ct = default);
}
