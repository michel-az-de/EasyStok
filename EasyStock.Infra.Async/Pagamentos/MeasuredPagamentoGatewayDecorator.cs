using System.Diagnostics;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums.Pagamentos;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Decorator que envolve cada <see cref="IPagamentoGateway"/> real para medir
/// latencia, classificar excecoes e alimentar <see cref="IGatewayHealthStore"/>.
///
/// <para>
/// <b>Onda P0</b>: registra logs estruturados (provedor, metodo, latencia,
/// outcome) e chama <see cref="IGatewayHealthStore"/> (no-op em P0).
/// Em P1, o store passa a ser o InMemory store + flush DB e impacta o
/// <c>PlanejarRotaAsync</c> para suspender gateways degradados.
/// </para>
///
/// <para>
/// <b>Transparente</b> para os adapters reais — apenas implementa o mesmo
/// contrato e delega. <see cref="IPagamentoGateway.Provedor"/> e
/// <see cref="IPagamentoGateway.SuportaMetodo"/> sao forwards puros.
/// </para>
/// </summary>
public sealed class MeasuredPagamentoGatewayDecorator(
    IPagamentoGateway inner,
    IGatewayHealthStore health,
    IGatewayErrorClassifier classifier,
    ILogger<MeasuredPagamentoGatewayDecorator> logger) : IPagamentoGateway
{
    public string Provedor => inner.Provedor;

    public bool SuportaMetodo(string metodo) => inner.SuportaMetodo(metodo);

    public async Task<InstrucaoPagamento> CriarAsync(
        Fatura fatura,
        string metodo,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await inner.CriarAsync(fatura, metodo, idempotencyKey, ct);
            sw.Stop();
            health.RegistrarSucesso(Provedor, (int)sw.ElapsedMilliseconds);
            logger.LogInformation(
                "Gateway {Provedor} CriarAsync OK em {LatenciaMs}ms (FaturaId={FaturaId}, Metodo={Metodo}, IdempotencyKey={IdemKey})",
                Provedor, sw.ElapsedMilliseconds, fatura.Id, metodo, AbreviarKey(idempotencyKey));
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var cat = classifier.Classify(Provedor, ex);
            health.RegistrarFalha(Provedor, cat, (int)sw.ElapsedMilliseconds);
            logger.LogWarning(ex,
                "Gateway {Provedor} CriarAsync FALHOU em {LatenciaMs}ms cat={Categoria} (FaturaId={FaturaId}, Metodo={Metodo})",
                Provedor, sw.ElapsedMilliseconds, cat, fatura.Id, metodo);
            throw;
        }
    }

    public async Task<StatusGateway> ConsultarAsync(string transactionId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await inner.ConsultarAsync(transactionId, ct);
            sw.Stop();
            health.RegistrarSucesso(Provedor, (int)sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var cat = classifier.Classify(Provedor, ex);
            health.RegistrarFalha(Provedor, cat, (int)sw.ElapsedMilliseconds);
            logger.LogWarning(ex,
                "Gateway {Provedor} ConsultarAsync FALHOU em {LatenciaMs}ms cat={Categoria} (txId={TxId})",
                Provedor, sw.ElapsedMilliseconds, cat, transactionId);
            throw;
        }
    }

    public async Task<EstornoResult> EstornarAsync(string transactionId, decimal valor, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await inner.EstornarAsync(transactionId, valor, ct);
            sw.Stop();
            health.RegistrarSucesso(Provedor, (int)sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var cat = classifier.Classify(Provedor, ex);
            health.RegistrarFalha(Provedor, cat, (int)sw.ElapsedMilliseconds);
            logger.LogWarning(ex,
                "Gateway {Provedor} EstornarAsync FALHOU em {LatenciaMs}ms cat={Categoria} (txId={TxId}, Valor={Valor})",
                Provedor, sw.ElapsedMilliseconds, cat, transactionId, valor);
            throw;
        }
    }

    private static string? AbreviarKey(string? key)
        => string.IsNullOrEmpty(key) ? null : (key.Length > 12 ? key[..12] + "…" : key);
}
