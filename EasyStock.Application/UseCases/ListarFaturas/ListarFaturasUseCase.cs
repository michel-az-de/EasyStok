using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;

namespace EasyStock.Application.UseCases.ListarFaturas;

public sealed record FaturaResult(
    Guid Id,
    string Txid,
    decimal Valor,
    string Status,
    string? MetodoPagamento,
    string? PixCopiaCola,
    string? QrCodeBase64,
    string? BoletoCodigo,
    string? BoletoUrl,
    DateTime CriadoEm,
    DateTime ExpiracaoEm,
    DateTime? PagoEm);

public class ListarFaturasUseCase(ICobrancaAssinaturaRepository cobrancaRepo)
{
    public async Task<IEnumerable<FaturaResult>> ExecuteAsync(Guid empresaId, int limit = 24)
    {
        var cobracas = await cobrancaRepo.GetByEmpresaAsync(empresaId, limit);
        return cobracas.Select(c => new FaturaResult(
            c.Id, c.Txid, c.Valor, c.Status.ToString(),
            c.MetodoPagamento,
            c.PixCopiaCola, c.QrCodeBase64,
            c.BoletoCodigo, c.BoletoUrl,
            c.CriadoEm, c.ExpiracaoEm, c.PagoEm));
    }
}
