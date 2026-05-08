using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Logging;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Implementação do <see cref="IGatewayFiscal"/> via Focus NFe.
/// Composição: HttpClient + Mappers + Credencial Resolver + Polly pipeline
/// (categoria "fiscal"). Não acopla logica fiscal a Application — apenas
/// traduz domain → JSON Focus → domain.
/// </summary>
internal sealed class FocusNFeAdapter(
    FocusNFeHttpClient client,
    FocusNFePayloadMapper payloadMapper,
    FocusNFeResponseMapper responseMapper,
    IIntegrationCredentialResolver credResolver,
    ResiliencePipelineProvider<string> pipelineProvider,
    ILogger<FocusNFeAdapter> log) : IGatewayFiscal
{
    private const string ProviderKey = "focusnfe";

    public async Task<ResultadoEmissaoNFCe> EmitirNFCeAsync(
        NotaFiscal nota, ConfigFiscalDto config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(nota);
        ArgumentNullException.ThrowIfNull(config);

        var token = await ResolverTokenAsync(nota.EmpresaId, config.Ambiente, ct);
        var payload = payloadMapper.Mapear(nota, config);
        var pipeline = pipelineProvider.GetPipeline("fiscal");

        var resp = await pipeline.ExecuteAsync(async token2 =>
            await client.EmitirAsync(payload, nota.Id, token, config.Ambiente, token2), ct);

        var result = responseMapper.Mapear(resp);
        log.LogInformation("Focus emitir {NotaId} → {Resultado} ({Codigo})",
            nota.Id, result.Resultado, result.Codigo);
        return result;
    }

    public async Task<ResultadoCancelamentoNFCe> CancelarNFCeAsync(
        NotaFiscal nota, string justificativa, ConfigFiscalDto config, CancellationToken ct)
    {
        var token = await ResolverTokenAsync(nota.EmpresaId, config.Ambiente, ct);
        var pipeline = pipelineProvider.GetPipeline("fiscal");

        var resp = await pipeline.ExecuteAsync(async token2 =>
            await client.CancelarAsync(nota.Id, justificativa, token, config.Ambiente, token2), ct);

        return responseMapper.MapearCancelamento(resp);
    }

    public async Task<ResultadoInutilizacaoNFCe> InutilizarNumeracaoAsync(
        NotaFiscalInutilizacao inutilizacao, ConfigFiscalDto config, CancellationToken ct)
    {
        var token = await ResolverTokenAsync(inutilizacao.EmpresaId, config.Ambiente, ct);
        var pipeline = pipelineProvider.GetPipeline("fiscal");

        var resp = await pipeline.ExecuteAsync(async token2 =>
            await client.InutilizarAsync(
                inutilizacao.EmpresaId,
                inutilizacao.Ano,
                config.CnpjEmitente,
                inutilizacao.Serie.ToString(),
                inutilizacao.NumeroInicial,
                inutilizacao.NumeroFinal,
                inutilizacao.Justificativa,
                token,
                config.Ambiente,
                token2), ct);

        return responseMapper.MapearInutilizacao(resp);
    }

    public async Task<ResultadoEmissaoNFCe> RetransmitirContingenciaAsync(
        NotaFiscal nota, ConfigFiscalDto config, CancellationToken ct)
    {
        var token = await ResolverTokenAsync(nota.EmpresaId, config.Ambiente, ct);
        var payload = payloadMapper.MapearContingencia(nota, config);
        var pipeline = pipelineProvider.GetPipeline("fiscal");

        var resp = await pipeline.ExecuteAsync(async token2 =>
            await client.EmitirAsync(payload, nota.Id, token, config.Ambiente, token2), ct);

        return responseMapper.Mapear(resp);
    }

    public string GerarXmlAssinadoLocal(NotaFiscal nota, ConfigFiscalDto config)
    {
        return payloadMapper.MontarXmlLocalSemAssinatura(nota, config);
    }

    private async Task<string> ResolverTokenAsync(Guid empresaId, Domain.Enums.Fiscal.AmbienteSefaz ambiente, CancellationToken ct)
    {
        var ambIntegracao = ambiente == Domain.Enums.Fiscal.AmbienteSefaz.Producao
            ? AmbienteIntegracao.Production
            : AmbienteIntegracao.Sandbox;

        var cred = await credResolver.ObterAsync<FocusNFeCredencial>(empresaId, ProviderKey, ambIntegracao, ct);
        if (cred is null || string.IsNullOrWhiteSpace(cred.TokenFocus))
            throw new CredencialFiscalAusenteException(empresaId);

        return cred.TokenFocus;
    }
}
