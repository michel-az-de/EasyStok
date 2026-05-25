using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.PagarAgora;

public sealed record PagarAgoraCommand(Guid EmpresaId);

public sealed record PagarAgoraResult(
    string Txid,
    decimal Valor,
    string PixCopiaCola,
    string QrCodeBase64,
    DateTime ExpiracaoEm,
    bool ReusouPendente);

/// <summary>
/// Cobranca Pix instantanea pra cliente que precisa pagar agora (reativacao
/// de conta suspensa, upgrade trial-paid acelerado). Substitui a espera de ate
/// 24h pelo CobrancaAssinaturaJob.
///
/// Idempotencia: se ja existe cobranca pendente nao expirada, retorna a mesma.
/// Cliente nao vai gerar 2 cobrancas duplicadas se clicar duas vezes.
/// </summary>
public class PagarAgoraUseCase(
    IAssinaturaEmpresaRepository assinaturaRepo,
    IPlanoRepository planoRepo,
    ICobrancaAssinaturaRepository cobrancaRepo,
    IEfiPixService efiService,
    IUnitOfWork uow,
    ILogger<PagarAgoraUseCase> logger)
{
    public async Task<PagarAgoraResult> ExecuteAsync(PagarAgoraCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var assinatura = await assinaturaRepo.GetAtivaAsync(cmd.EmpresaId)
            ?? throw new UseCaseValidationException("Nenhuma assinatura encontrada.");

        var plano = await planoRepo.GetByIdAsync(assinatura.PlanoId)
            ?? throw new UseCaseValidationException("Plano da assinatura nao encontrado.");

        if (plano.PrecoMensal <= 0)
            throw new UseCaseValidationException("Plano gratuito nao requer pagamento.");

        // Idempotencia: pendente nao expirada = reusa. Evita 2 QR codes pra mesmo cliente.
        if (await cobrancaRepo.ExistePendenteAsync(cmd.EmpresaId))
        {
            var pendentes = await cobrancaRepo.GetByEmpresaAsync(cmd.EmpresaId, limit: 5);
            var ativa = pendentes.FirstOrDefault(c => c.Status == Domain.Enums.StatusCobranca.Pendente && c.ExpiracaoEm > DateTime.UtcNow);
            if (ativa is not null)
            {
                logger.LogInformation("PagarAgora: reusando cobranca pendente Txid={Txid} EmpresaId={EmpresaId}", ativa.Txid, cmd.EmpresaId);
                return new PagarAgoraResult(ativa.Txid, ativa.Valor, ativa.PixCopiaCola, ativa.QrCodeBase64, ativa.ExpiracaoEm, ReusouPendente: true);
            }
        }

        var txid = GerarTxid();
        var efi = await efiService.CriarCobrancaAsync(txid, plano.PrecoMensal, $"EasyStok - {plano.Nome}", ct);

        var cobranca = CobrancaAssinatura.Criar(
            empresaId: cmd.EmpresaId,
            assinaturaId: assinatura.Id,
            txid: efi.Txid,
            valor: plano.PrecoMensal,
            pixCopiaCola: efi.PixCopiaCola,
            qrCodeBase64: efi.QrCodeBase64,
            expiracaoEm: efi.ExpiracaoEm);
        cobranca.MetodoPagamento = "Pix";

        await cobrancaRepo.AddAsync(cobranca);
        await uow.CommitAsync();

        logger.LogInformation("PagarAgora: cobranca nova Txid={Txid} Valor={Valor} EmpresaId={EmpresaId}", efi.Txid, plano.PrecoMensal, cmd.EmpresaId);
        return new PagarAgoraResult(efi.Txid, plano.PrecoMensal, efi.PixCopiaCola, efi.QrCodeBase64, efi.ExpiracaoEm, ReusouPendente: false);
    }

    // Efi exige Txid alfanumerico de 26-35 chars. Guid hex tem 32 — perfeito.
    private static string GerarTxid() => Guid.NewGuid().ToString("N");
}
