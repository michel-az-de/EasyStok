using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Fiscal.InutilizarNumeracao;

/// <summary>
/// Inutiliza faixa de numeracao no SEFAZ via gateway. Operacao sincrona — sucesso
/// gera protocolo de inutilizacao; falha lanca <see cref="GatewayFiscalRejeitadaException"/>.
///
/// <para>
/// <b>F1 simplificado:</b> apenas chama o gateway. Persistencia de
/// <c>NfeInutilizacao</c> como entidade dedicada fica para iteracao futura
/// (criar tabela <c>nfe_inutilizacao</c> via migration F1.5 ou posterior).
/// Por enquanto, registro vive apenas no gateway/SEFAZ — recuperavel via
/// consulta de status.
/// </para>
/// </summary>
public class InutilizarNumeracaoUseCase(
    IGatewayFiscalFactory gatewayFactory,
    IConfigFiscalResolver configResolver,
    ILogger<InutilizarNumeracaoUseCase> logger) : IUseCase<InutilizarNumeracaoCommand, InutilizarNumeracaoResult>
{
    public async Task<InutilizarNumeracaoResult> ExecuteAsync(InutilizarNumeracaoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.Serie <= 0)
            throw new UseCaseValidationException("Serie invalida.");
        if (cmd.NumeroInicial <= 0 || cmd.NumeroFinal < cmd.NumeroInicial)
            throw new UseCaseValidationException("Faixa de numeracao invalida.");
        if (cmd.NumeroFinal - cmd.NumeroInicial > 9_999)
            throw new UseCaseValidationException("Faixa nao pode exceder 10.000 numeros por solicitacao.");
        if (string.IsNullOrWhiteSpace(cmd.Justificativa) || cmd.Justificativa.Trim().Length < 15)
            throw new UseCaseValidationException("Justificativa exige minimo 15 caracteres (SEFAZ).");

        var config = await configResolver.ResolveAsync(cmd.EmpresaId);
        var gateway = gatewayFactory.ObterPara(config.Provedor);

        var resultado = await gateway.InutilizarAsync(
            cmd.EmpresaId,
            cmd.Serie,
            cmd.NumeroInicial,
            cmd.NumeroFinal,
            cmd.Justificativa.Trim(),
            config);

        logger.LogInformation(
            "Inutilizacao tenant={Empresa} serie={Serie} faixa=[{Ini}..{Fim}] protocolo={Proto}.",
            cmd.EmpresaId, cmd.Serie, cmd.NumeroInicial, cmd.NumeroFinal, resultado.ProtocoloEvento);

        return new InutilizarNumeracaoResult(resultado.ProtocoloEvento, resultado.DataInutilizacao);
    }
}
