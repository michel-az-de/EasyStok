using System.Collections.Concurrent;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Fiscal;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Fiscal.Mock;

/// <summary>
/// Adapter "mock" do <see cref="IGatewayFiscal"/>. Simula respostas SEFAZ sem
/// chamar provedor real nem exigir certificado A1 valido. Selecao acontece via
/// <see cref="IGatewayFiscalFactory"/> quando
/// <c>EmpresaConfiguracaoFiscal.ProvedorPreferido == "mock"</c>.
///
/// <para>
/// <b>Uso:</b> exclusivo de Sandbox/dev. O dominio bloqueia <c>Habilitar()</c>
/// com Provedor=mock em <see cref="Domain.Integration.AmbienteIntegracao.Production"/>.
/// </para>
///
/// <para>
/// <b>Persistencia in-memory:</b> mantem estado de emissoes por processo
/// (<see cref="StatusPorChave"/>) para suportar <see cref="ConsultarStatusAsync"/>
/// determinista em reconciliacao. Resetado a cada restart — apropriado para mock.
/// </para>
/// </summary>
public sealed class MockGatewayFiscal(
    IGeradorChaveAcesso geradorChave,
    ILogger<MockGatewayFiscal> logger) : IGatewayFiscal
{
    private static readonly ConcurrentDictionary<string, ResultadoConsultaNfce> StatusPorChave = new();

    /// <summary>Identificador estavel do provedor. Casa com whitelist em <c>EmpresaConfiguracaoFiscal.EscolherProvedor</c>.</summary>
    public string Provedor => "mock";

    public async Task<ResultadoEmissaoNfce> EmitirAsync(NfeDocumento nfe, ConfigFiscalDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nfe);
        ArgumentNullException.ThrowIfNull(config);

        // Simula latencia SEFAZ (200-400ms) — ajuda a expor race conditions em UI
        // sem virar bottleneck de testes integration.
        await Task.Delay(Random.Shared.Next(200, 400), ct);

        var uf = config.Endereco?.Uf
            ?? throw new GatewayFiscalRejeitadaException("UF do emitente ausente na configuracao fiscal mock.");

        // Reaproveita o gerador real para emitir chave estruturalmente valida
        // (cUF + AAMM + CNPJ + modelo + serie + numero + tpEmis + cNF + DV).
        var chave = !string.IsNullOrWhiteSpace(nfe.ChaveAcesso)
            ? nfe.ChaveAcesso
            : geradorChave.Gerar(
                uf: uf,
                cnpjEmitente: config.Cnpj,
                serie: nfe.Serie,
                numero: nfe.Numero,
                dataEmissao: DateTime.UtcNow,
                modeloFiscal: nfe.Modelo,
                tipoEmissao: 1);

        var protocolo = GerarProtocolo();
        var agora = DateTime.UtcNow;
        var danfeUrl = $"/api/mock/danfe/{chave}.pdf";

        var resultado = new ResultadoEmissaoNfce(
            ChaveAcesso: chave,
            ProtocoloAutorizacao: protocolo,
            DataAutorizacao: agora,
            XmlAssinadoUrl: null,
            DanfeUrl: danfeUrl);

        StatusPorChave[chave] = new ResultadoConsultaNfce(
            StatusGateway: "autorizado",
            ProtocoloAutorizacao: protocolo,
            MotivoRejeicao: null,
            DataAutorizacao: agora,
            XmlAssinadoUrl: null,
            DanfeUrl: danfeUrl);

        logger.LogInformation(
            "MockGatewayFiscal: NFC-e simulada autorizada. Chave={Chave} Protocolo={Protocolo} Cnpj={Cnpj} Total={Total}",
            chave, protocolo, config.Cnpj, nfe.TotalNota.Valor);

        return resultado;
    }

    public async Task<ResultadoCancelamentoNfce> CancelarAsync(NfeDocumento nfe, string motivo, ConfigFiscalDto config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nfe);
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 15)
            throw new ArgumentException("Motivo deve ter no minimo 15 caracteres (SEFAZ).", nameof(motivo));

        await Task.Delay(Random.Shared.Next(150, 300), ct);

        // SEFAZ rejeita cancelamento apos 24h da autorizacao. Mock reproduz essa janela
        // para que telas de admin exercam o caminho de erro fora-de-prazo.
        if (nfe.DataAutorizacao is { } emitidaEm
            && DateTime.UtcNow - emitidaEm > TimeSpan.FromHours(24))
        {
            throw new GatewayFiscalRejeitadaException(
                motivo: "Prazo de cancelamento de 24h expirado (mock).",
                codigo: "501");
        }

        var protocolo = GerarProtocolo();
        var agora = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(nfe.ChaveAcesso))
        {
            StatusPorChave[nfe.ChaveAcesso] = new ResultadoConsultaNfce(
                StatusGateway: "cancelado",
                ProtocoloAutorizacao: nfe.ProtocoloAutorizacao,
                MotivoRejeicao: motivo,
                DataAutorizacao: nfe.DataAutorizacao,
                XmlAssinadoUrl: null,
                DanfeUrl: nfe.DanfeUrl);
        }

        logger.LogInformation(
            "MockGatewayFiscal: cancelamento simulado. NfeId={Id} Chave={Chave} Protocolo={Protocolo}",
            nfe.Id, nfe.ChaveAcesso, protocolo);

        return new ResultadoCancelamentoNfce(
            ProtocoloEvento: protocolo,
            DataCancelamento: agora);
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
        if (string.IsNullOrWhiteSpace(justificativa) || justificativa.Trim().Length < 15)
            throw new ArgumentException("Justificativa minima 15 caracteres (SEFAZ).", nameof(justificativa));

        await Task.Delay(Random.Shared.Next(150, 300), ct);

        var protocolo = GerarProtocolo();
        logger.LogInformation(
            "MockGatewayFiscal: inutilizacao simulada. Tenant={Empresa} Serie={Serie} Faixa=[{Ini}..{Fim}] Protocolo={Protocolo}",
            empresaId, serie, numeroInicial, numeroFinal, protocolo);

        return new ResultadoInutilizacaoNfce(
            ProtocoloEvento: protocolo,
            DataInutilizacao: DateTime.UtcNow);
    }

    public async Task<ResultadoConsultaNfce> ConsultarStatusAsync(string chaveAcesso, ConfigFiscalDto config, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chaveAcesso) || chaveAcesso.Length != 44)
            throw new ArgumentException("Chave de acesso deve ter 44 digitos.", nameof(chaveAcesso));

        await Task.Delay(50, ct);

        // Devolve estado cache (autorizado/cancelado) se conhecido; senao "nao encontrado".
        if (StatusPorChave.TryGetValue(chaveAcesso, out var conhecido))
            return conhecido;

        return new ResultadoConsultaNfce(
            StatusGateway: "nao-encontrado",
            ProtocoloAutorizacao: null,
            MotivoRejeicao: "Chave nao localizada na cache do mock (provavelmente emitida em outra instancia).",
            DataAutorizacao: null,
            XmlAssinadoUrl: null,
            DanfeUrl: null);
    }

    private static string GerarProtocolo()
    {
        // SEFAZ usa 15 digitos numericos (cUF + AAMMdd + sequencial). Mock gera valor
        // estatisticamente unico baseado em timestamp + sufixo aleatorio.
        var prefixo = DateTime.UtcNow.ToString("yyMMddHHmmss");
        var sufixo = Random.Shared.Next(100, 999).ToString();
        return prefixo + sufixo;
    }
}
