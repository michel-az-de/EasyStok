using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Fiscal;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Fiscal.ReprocessarContingencia;

/// <summary>
/// Job que processa NfeDocumentos em <see cref="StatusNfe.FalhaTransiente"/>.
/// Aplica fix B-053: usa <see cref="IRowLevelSecurityBypass.Begin"/> para iterar
/// cross-tenant; aplica fix B-052 implicitamente (HTTP fora de transacao).
///
/// <para>
/// <b>Limite:</b> processa no maximo <see cref="ReprocessarContingenciaCommand.BatchSize"/>
/// notas por execucao. Job recorrente (F4) chama este use case a cada N minutos.
/// </para>
///
/// <para>
/// <b>SLA contingencia:</b> SEFAZ permite ate 24h sem autorizacao. Apos esse
/// prazo, nota deve virar inutilizada manualmente (alerta operacional separado — F4).
/// </para>
/// </summary>
public class ReprocessarContingenciaUseCase(
    INfeRepository nfeRepo,
    IGatewayFiscalFactory gatewayFactory,
    IConfigFiscalResolver configResolver,
    IRowLevelSecurityBypass rlsBypass,
    IUnitOfWork uow,
    ILogger<ReprocessarContingenciaUseCase> logger) : IUseCase<ReprocessarContingenciaCommand, ReprocessarContingenciaResult>
{
    public async Task<ReprocessarContingenciaResult> ExecuteAsync(ReprocessarContingenciaCommand cmd)
    {
        if (cmd.BatchSize <= 0 || cmd.BatchSize > 500)
            throw new UseCaseValidationException("BatchSize deve estar em 1..500.");

        using var _ = rlsBypass.Begin();

        var pendentes = (await nfeRepo.ListarPendentesContingenciaAsync(cmd.BatchSize)).ToList();
        if (pendentes.Count == 0)
        {
            return new ReprocessarContingenciaResult(0, 0, 0, 0);
        }

        logger.LogInformation("Contingencia: {Count} notas em FalhaTransiente para reprocessar.", pendentes.Count);

        int autorizadas = 0, rejeitadas = 0, transientes = 0;

        foreach (var nfe in pendentes)
        {
            try
            {
                var config = await configResolver.ResolveAsync(nfe.EmpresaId);
                var gateway = gatewayFactory.ObterPara(config.Provedor);

                // Recarrega com itens (gateway precisa)
                var nfeComItens = await nfeRepo.GetByIdWithDetailsAsync(nfe.EmpresaId, nfe.Id);
                if (nfeComItens is null)
                {
                    logger.LogWarning("Nfe {Id} sumiu entre listagem e reprocessamento.", nfe.Id);
                    continue;
                }

                // Re-marca como EnviadaAguardandoRetorno antes de chamar SEFAZ
                await uow.ExecuteInTransactionAsync(async _ =>
                {
                    nfeComItens.MarcarEnviada(origem: "job-contingencia");
                    await nfeRepo.UpdateAsync(nfeComItens);
                });

                // HTTP fora de transacao (mesmo pattern que EmitirNfceUseCase)
                try
                {
                    var resultado = await gateway.EmitirAsync(nfeComItens, config);

                    await uow.ExecuteInTransactionAsync(async _ =>
                    {
                        var nfeReload = await nfeRepo.GetByIdAsync(nfeComItens.EmpresaId, nfeComItens.Id);
                        nfeReload!.MarcarAutorizada(
                            chaveAcesso: resultado.ChaveAcesso,
                            protocoloAutorizacao: resultado.ProtocoloAutorizacao,
                            xmlAssinadoStorageKey: resultado.XmlAssinadoUrl,
                            danfeUrl: resultado.DanfeUrl,
                            origem: "job-contingencia");
                        await nfeRepo.UpdateAsync(nfeReload);
                    });
                    autorizadas++;
                }
                catch (GatewayFiscalRejeitadaException rej)
                {
                    await uow.ExecuteInTransactionAsync(async _ =>
                    {
                        var nfeReload = await nfeRepo.GetByIdAsync(nfeComItens.EmpresaId, nfeComItens.Id);
                        nfeReload!.MarcarRejeitada(rej.Motivo, origem: "job-contingencia");
                        await nfeRepo.UpdateAsync(nfeReload);
                    });
                    rejeitadas++;
                }
                catch (GatewayFiscalTransienteException)
                {
                    await uow.ExecuteInTransactionAsync(async _ =>
                    {
                        var nfeReload = await nfeRepo.GetByIdAsync(nfeComItens.EmpresaId, nfeComItens.Id);
                        nfeReload!.MarcarFalhaTransiente(
                            detalhe: "Ainda em falha apos reprocessamento.",
                            origem: "job-contingencia");
                        await nfeRepo.UpdateAsync(nfeReload);
                    });
                    transientes++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Erro inesperado reprocessando Nfe {Id} tenant={Empresa}. Ignorando esta e seguindo lote.",
                    nfe.Id, nfe.EmpresaId);
                transientes++;
            }
        }

        var total = autorizadas + rejeitadas + transientes;
        logger.LogInformation(
            "Contingencia concluida: total={Total} autorizadas={Auth} rejeitadas={Rej} ainda_transientes={Tra}.",
            total, autorizadas, rejeitadas, transientes);

        return new ReprocessarContingenciaResult(total, autorizadas, rejeitadas, transientes);
    }
}
