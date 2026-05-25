using EasyStok.Mobile.Models;
using EasyStok.Mobile.Storage;
using Microsoft.Extensions.Logging;
using System.IO;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Fachada otimista para incrementar/decrementar estoque na producao.
/// Atualiza o cache local imediatamente (UX instantaneo) e enfileira
/// no outbox a chamada REST correspondente. Flush dispara em background.
/// </summary>
public sealed class EstoqueMutationService : IEstoqueMutationService
{
    private readonly IOutboxRepository _outbox;
    private readonly IOutboxFlushService _flush;
    private readonly AppDatabase _db;
    private readonly ISecureStore _store;
    private readonly ILogger<EstoqueMutationService> _logger;

    public EstoqueMutationService(
        IOutboxRepository outbox,
        IOutboxFlushService flush,
        AppDatabase db,
        ISecureStore store,
        ILogger<EstoqueMutationService> logger)
    {
        _outbox = outbox;
        _flush = flush;
        _db = db;
        _store = store;
        _logger = logger;
    }

    public Task IncrementAsync(CachedItemEstoque item, int quantidade = 1) =>
        IncrementAsync(item, new CapturaProducaoResult(quantidade, null, null, null));

    public async Task IncrementAsync(CachedItemEstoque item, CapturaProducaoResult capture)
    {
        var empresaId = await _store.GetEmpresaIdAsync()
            ?? throw new InvalidOperationException("Empresa nao definida.");
        var lojaId = await _store.GetLojaIdAsync();

        // DimensoesInput exige todos os 4 valores (Peso, Largura, Altura, Comprimento).
        // Quando peso e informado, mandamos com largura/altura/comprimento=0 para que
        // o backend grave so o peso. Quando nao informado, deixamos null (default).
        object? dims = capture.PesoG.HasValue
            ? new { peso = capture.PesoG.Value, largura = 0m, altura = 0m, comprimento = 0m }
            : null;

        var observacoes = "Entrada via mobile";
        if (!string.IsNullOrEmpty(capture.FotoPath))
            observacoes += $" | foto: {Path.GetFileName(capture.FotoPath)}";

        var produtoId = Guid.Parse(item.ProdutoId);
        var cmd = new RegistrarEntradaCommand(
            EmpresaId: empresaId,
            ProdutoId: produtoId,
            ProdutoVariacaoId: null,
            Quantidade: capture.Quantidade,
            CustoUnitario: item.CustoUnitario,
            PrecoVendaSugerido: item.PrecoVendaSugerido,
            DataEntrada: DateTime.UtcNow,
            Natureza: "Compra",
            CodigoInterno: null,
            CodigoLote: item.Lote,
            CodigoMarketplace: null,
            VariacaoDescricao: null,
            Cor: null,
            Tamanho: null,
            FornecedorNome: null,
            Validade: capture.Validade ?? item.ValidadeUtc,
            Observacoes: observacoes,
            DescricaoAnuncio: null,
            DocumentoReferencia: null,
            DimensoesReais: dims,
            InstrucoesGeracaoDescricao: null,
            LojaId: lojaId);

        await _outbox.EnqueueAsync(OutboxTypes.EstoqueEntrada, cmd);
        await OptimisticUpdateAsync(item.Id, +capture.Quantidade);
        _ = Task.Run(async () => await _flush.FlushAsync());
    }

    public async Task DecrementAsync(CachedItemEstoque item, int quantidade = 1)
    {
        var empresaId = await _store.GetEmpresaIdAsync()
            ?? throw new InvalidOperationException("Empresa nao definida.");

        var itens = new[]
        {
            new RegistrarSaidaItem(
                ItemEstoqueId: Guid.Parse(item.Id),
                ProdutoId: Guid.Parse(item.ProdutoId),
                ProdutoVariacaoId: null,
                Quantidade: quantidade,
                ValorVendaUnitario: 0m,
                Descricao: "Saida via mobile (ajuste)")
        };

        var cmd = new RegistrarSaidaCommand(
            EmpresaId: empresaId,
            Itens: itens,
            DataVenda: DateTime.UtcNow,
            DataSaida: DateTime.UtcNow,
            DataEnvio: null,
            NotaFiscal: null,
            Natureza: "Ajuste",
            Canal: "LojaPropria",
            Observacoes: "Ajuste via mobile");

        await _outbox.EnqueueAsync(OutboxTypes.EstoqueSaida, cmd);
        await OptimisticUpdateAsync(item.Id, -quantidade);
        _ = Task.Run(async () => await _flush.FlushAsync());
    }

    private async Task OptimisticUpdateAsync(string itemId, int delta)
    {
        var conn = await _db.GetConnectionAsync();
        var row = await conn.FindAsync<CachedItemEstoque>(itemId);
        if (row is null) return;
        row.Qty = Math.Max(0, row.Qty + delta);
        row.LastMovUtc = DateTime.UtcNow;
        await conn.UpdateAsync(row);
    }
}

public interface IEstoqueMutationService
{
    Task IncrementAsync(CachedItemEstoque item, int quantidade = 1);
    Task IncrementAsync(CachedItemEstoque item, CapturaProducaoResult capture);
    Task DecrementAsync(CachedItemEstoque item, int quantidade = 1);
}
