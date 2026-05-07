using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Financeiro.Pagamentos;

public sealed record GerarPixQrParcelaReceberCommand(
    Guid EmpresaId,
    Guid ParcelaId);

public sealed record PixQrParcelaResult(
    string Txid,
    string PixCopiaCola,
    string QrCodeBase64,
    DateTime ExpiraEm,
    decimal Valor);

public class GerarPixQrParcelaReceberUseCase(
    IContaReceberRepository contaRepo,
    IEfiPixService pix,
    IUnitOfWork uow,
    ILogger<GerarPixQrParcelaReceberUseCase> logger)
{
    public async Task<PixQrParcelaResult?> ExecuteAsync(GerarPixQrParcelaReceberCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var parcela = await contaRepo.GetParcelaWithContaAsync(cmd.EmpresaId, cmd.ParcelaId, ct);
        if (parcela is null) return null;
        var conta = parcela.ContaReceber
                    ?? throw new UseCaseValidationException("Parcela orfa.");

        if (parcela.Status == StatusParcela.Paga || parcela.Status == StatusParcela.Cancelada)
            throw new UseCaseValidationException("Parcela ja paga ou cancelada — Pix nao pode ser gerado.");

        // Idempotencia: se ja tem txid e ainda nao expirou, retorna existente
        if (!string.IsNullOrWhiteSpace(parcela.EfiTxid) &&
            parcela.PixExpiraEm.HasValue &&
            parcela.PixExpiraEm.Value > DateTime.UtcNow.AddMinutes(5))
        {
            return new PixQrParcelaResult(
                parcela.EfiTxid!,
                parcela.PixCopiaCola ?? "",
                parcela.QrCodeBase64 ?? "",
                parcela.PixExpiraEm.Value,
                parcela.Saldo);
        }

        try
        {
            // Prefixo "cr" pra distinguir de Fatura no webhook (max 35 chars total)
            var txid = $"cr{conta.EmpresaId.ToString("N")[..6]}{parcela.Id.ToString("N")[..18]}";
            if (txid.Length > 35) txid = txid[..35];

            var descricao = $"Conta {conta.Descricao} parcela {parcela.Numero}/{conta.Parcelas.Count}";
            if (descricao.Length > 100) descricao = descricao[..100];

            var resultado = await pix.CriarCobrancaAsync(txid, parcela.Saldo, descricao, ct);

            parcela.AssociarPix(resultado.Txid, resultado.PixCopiaCola, resultado.QrCodeBase64, resultado.ExpiracaoEm);

            await contaRepo.AddEventoAsync(ContaFinanceiraEvento.ParaContaReceber(
                conta.EmpresaId, conta.Id, TipoEventoContaFinanceira.PixGerado,
                descricao: $"Pix QR gerado pra parcela {parcela.Numero} (R$ {parcela.Saldo:F2}).",
                origem: "api"), ct);

            await contaRepo.UpdateAsync(conta, ct);
            await uow.CommitAsync();

            logger.LogInformation("Pix QR gerado: parcela={ParcId} txid={Txid}", parcela.Id, resultado.Txid);
            return new PixQrParcelaResult(
                resultado.Txid, resultado.PixCopiaCola, resultado.QrCodeBase64,
                resultado.ExpiracaoEm, parcela.Saldo);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}

public sealed record LimparPixParcelaReceberCommand(Guid EmpresaId, Guid ParcelaId);

public class LimparPixParcelaReceberUseCase(
    IContaReceberRepository contaRepo,
    IUnitOfWork uow)
{
    public async Task<bool> ExecuteAsync(LimparPixParcelaReceberCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var parcela = await contaRepo.GetParcelaWithContaAsync(cmd.EmpresaId, cmd.ParcelaId, ct);
        if (parcela is null) return false;
        if (string.IsNullOrWhiteSpace(parcela.EfiTxid)) return true;

        parcela.LimparPix();

        await contaRepo.AddEventoAsync(ContaFinanceiraEvento.ParaContaReceber(
            parcela.EmpresaId, parcela.ContaReceberId, TipoEventoContaFinanceira.PixLimpado,
            descricao: $"Pix da parcela {parcela.Numero} limpo.",
            origem: "api"), ct);

        if (parcela.ContaReceber is not null)
            await contaRepo.UpdateAsync(parcela.ContaReceber, ct);
        await uow.CommitAsync();
        return true;
    }
}
