using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Fiscal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Implementacao de <see cref="IGatewayFiscal"/> para Focus NFe. Composicao
/// de <see cref="FocusNFeHttpClient"/> + <see cref="FocusNFePayloadMapper"/> +
/// <see cref="FocusNFeResponseMapper"/>.
///
/// <para>
/// <b>Referencia (idempotency):</b> Focus usa o parametro <c>ref</c> como
/// chave de idempotencia. Usamos <c>nfe.Id.ToString("N")</c> (32 chars hex,
/// sem hifens) — re-emissao com mesmo ID retorna a chave ja autorizada
/// sem duplicar.
/// </para>
/// </summary>
public sealed class FocusNFeAdapter(
    FocusNFeHttpClient httpClient,
    IOptions<FocusNFeOptions> options,
    ILogger<FocusNFeAdapter> logger) : IGatewayFiscal
{
    public string Provedor => "focus";

    public async Task<ResultadoEmissaoNfce> EmitirAsync(NfeDocumento nfe, ConfigFiscalDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nfe);
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.CredencialToken))
            throw new GatewayFiscalCredencialException("Token do Focus NFe ausente. Cadastre a credencial fiscal do tenant antes de emitir.");

        var referencia = nfe.Id.ToString("N");
        var payload = FocusNFePayloadMapper.Map(nfe, config, options.Value);

        logger.LogDebug("Focus emitir ref={Ref} cnpj={Cnpj} total={Total}",
            referencia, payload.CnpjEmitente, payload.ValorTotal);

        var resp = await httpClient.EmitirAsync(referencia, config.CredencialToken, payload, ct);
        return FocusNFeResponseMapper.MapEmissao(resp);
    }

    public async Task<ResultadoCancelamentoNfce> CancelarAsync(NfeDocumento nfe, string motivo, ConfigFiscalDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nfe);
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 15)
            throw new ArgumentException("Motivo deve ter no minimo 15 caracteres (SEFAZ).", nameof(motivo));
        if (string.IsNullOrWhiteSpace(config.CredencialToken))
            throw new GatewayFiscalCredencialException("Token Focus ausente.");

        var referencia = nfe.Id.ToString("N");
        var resp = await httpClient.CancelarAsync(referencia, config.CredencialToken, motivo.Trim(), ct);
        return FocusNFeResponseMapper.MapCancelamento(resp);
    }

    public async Task<ResultadoInutilizacaoNfce> InutilizarAsync(
        Guid empresaId,
        short serie,
        long numeroInicial,
        long numeroFinal,
        string justificativa,
        ConfigFiscalDto config,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.CredencialToken))
            throw new GatewayFiscalCredencialException("Token Focus ausente.");
        if (string.IsNullOrWhiteSpace(justificativa) || justificativa.Trim().Length < 15)
            throw new ArgumentException("Justificativa minima 15 chars.", nameof(justificativa));

        var resp = await httpClient.InutilizarAsync(
            SomenteDigitos(config.Cnpj),
            serie,
            numeroInicial,
            numeroFinal,
            justificativa.Trim(),
            config.CredencialToken,
            ct);

        return FocusNFeResponseMapper.MapInutilizacao(resp);
    }

    public async Task<ResultadoConsultaNfce> ConsultarStatusAsync(string chaveAcesso, ConfigFiscalDto config, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.CredencialToken))
            throw new GatewayFiscalCredencialException("Token Focus ausente.");
        if (string.IsNullOrWhiteSpace(chaveAcesso) || chaveAcesso.Length != 44)
            throw new ArgumentException("Chave de acesso deve ter 44 digitos.", nameof(chaveAcesso));

        // Focus aceita consulta tanto por chave quanto por ref. Usamos chave aqui pra
        // suportar consultas externas (reconciliacao manual, suporte) sem precisar conhecer nfe.Id.
        var resp = await httpClient.ConsultarAsync(chaveAcesso, config.CredencialToken, ct);
        return FocusNFeResponseMapper.MapConsulta(resp);
    }

    private static string SomenteDigitos(string input) =>
        new(input.Where(char.IsDigit).ToArray());
}
