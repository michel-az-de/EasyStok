using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Fiscal;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Fiscal.CancelarNfe;

/// <summary>
/// Cancela uma NFC-e autorizada. Padrao "SEFAZ antes do commit" (fix B-055):
/// <list type="number">
///   <item>Validar status = <see cref="StatusNfe.Autorizada"/>.</item>
///   <item>Chamar <see cref="IGatewayFiscal.CancelarAsync"/> — se SEFAZ rejeitar (prazo, etc), excecao propaga e nada e commitado.</item>
///   <item>So apos SEFAZ confirmar, abrir Tx e chamar <c>nfe.Cancelar()</c> + persistir.</item>
/// </list>
///
/// <para>
/// <b>Risco residual:</b> se crash entre HTTP de sucesso e commit local, a SEFAZ tem
/// cancelamento mas o nosso banco ainda mostra Autorizada. Mitigacao: job de
/// reconciliacao consulta SEFAZ periodicamente (F4) — encontra discrepancia e finaliza.
/// </para>
/// </summary>
public class CancelarNfeUseCase(
    INfeRepository nfeRepo,
    IGatewayFiscalFactory gatewayFactory,
    IConfigFiscalResolver configResolver,
    IUnitOfWork uow,
    ILogger<CancelarNfeUseCase> logger) : IUseCase<CancelarNfeCommand, CancelarNfeResult>
{
    public async Task<CancelarNfeResult> ExecuteAsync(CancelarNfeCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.NfeId, "NfeId");
        if (string.IsNullOrWhiteSpace(cmd.Motivo) || cmd.Motivo.Trim().Length < 15)
            throw new UseCaseValidationException("Motivo do cancelamento exige no minimo 15 caracteres (SEFAZ).");

        var nfe = await nfeRepo.GetByIdAsync(cmd.EmpresaId, cmd.NfeId)
            ?? throw new RegraDeDominioVioladaException($"NfeDocumento {cmd.NfeId} nao encontrado.");

        if (nfe.Status == StatusNfe.Cancelada)
        {
            logger.LogInformation("Nfe {Id} ja estava cancelada — operacao idempotente.", nfe.Id);
            return new CancelarNfeResult(nfe.Id, nfe.Status, ProtocoloEvento: null);
        }

        if (nfe.Status != StatusNfe.Autorizada)
            throw new RegraDeDominioVioladaException(
                $"So NFC-e Autorizada pode ser cancelada. Status atual: {nfe.Status}.");

        var config = await configResolver.ResolveAsync(cmd.EmpresaId);
        var gateway = gatewayFactory.ObterPara(config.Provedor);

        // === SEFAZ primeiro (anti-padrao "commit antes de SEFAZ" evitado — B-055) ===
        var resultado = await gateway.CancelarAsync(nfe, cmd.Motivo.Trim(), config);

        // === Commit local apos SEFAZ confirmar ===
        await uow.ExecuteInTransactionAsync(async _ =>
        {
            var nfeReload = await nfeRepo.GetByIdAsync(cmd.EmpresaId, cmd.NfeId)
                ?? throw new InvalidOperationException($"Nfe {cmd.NfeId} sumiu apos cancel SEFAZ.");

            nfeReload.Cancelar(cmd.Motivo.Trim(), cmd.UsuarioId, cmd.UsuarioNome, cmd.Origem);
            await nfeRepo.UpdateAsync(nfeReload);
        });

        logger.LogInformation(
            "Nfe {Id} cancelada. ProtocoloEvento={Protocolo}.", nfe.Id, resultado.ProtocoloEvento);

        return new CancelarNfeResult(nfe.Id, StatusNfe.Cancelada, resultado.ProtocoloEvento);
    }
}

public sealed record CancelarNfeResult(Guid NfeId, StatusNfe Status, string? ProtocoloEvento);
