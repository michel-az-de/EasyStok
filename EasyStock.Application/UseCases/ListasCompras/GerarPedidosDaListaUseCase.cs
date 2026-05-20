using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarSugestaoCompra;
using EasyStock.Application.UseCases.PreviewSugestaoCompra;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ListasCompras;

// ── DTOs ─────────────────────────────────────────────────────────────
public sealed record GerarPedidosDaListaCommand(
    Guid EmpresaId,
    Guid ListaComprasId,
    Guid? LojaId,
    string IdempotencyKey);

public sealed record PedidoGeradoItemResult(string Nome, decimal Quantidade, string Unidade);

public sealed record PedidoGeradoResult(
    Guid PedidoFornecedorId,
    Guid FornecedorId,
    string FornecedorNome,
    string? FornecedorTelefone,
    decimal? ValorEstimado,
    IReadOnlyList<PedidoGeradoItemResult> Itens);

public sealed record GerarPedidosDaListaResult(
    IReadOnlyList<PedidoGeradoResult> Pedidos,
    IReadOnlyList<string> ItensSemFornecedor,
    int ItensIgnorados);

/// <summary>
/// Transforma os itens vinculados a produto de uma lista de compras em pedidos de fornecedor,
/// agrupados pelo fornecedor preferido. Reusa <see cref="PreviewSugestaoCompraUseCase"/> (agrupamento)
/// e <see cref="CriarSugestaoCompraUseCase"/> (criação + notificação via Outbox). Itens sem ProdutoId,
/// sem quantidade ou sem fornecedor preferido não viram pedido — são reportados de volta.
/// </summary>
public class GerarPedidosDaListaUseCase(
    IListaComprasRepository listaRepository,
    IFornecedorRepository fornecedorRepository,
    PreviewSugestaoCompraUseCase previewUseCase,
    CriarSugestaoCompraUseCase criarUseCase,
    ILogger<GerarPedidosDaListaUseCase> logger)
{
    public async Task<GerarPedidosDaListaResult> ExecuteAsync(GerarPedidosDaListaCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ListaComprasId, "ListaComprasId");
        if (string.IsNullOrWhiteSpace(cmd.IdempotencyKey))
            throw new UseCaseValidationException("INVALID_IDEMPOTENCY_KEY", "Idempotency-Key obrigatório.");

        var lista = await listaRepository.GetByIdWithItemsAsync(cmd.EmpresaId, cmd.ListaComprasId)
            ?? throw new UseCaseValidationException("LISTA_NAO_ENCONTRADA", "Lista não encontrada.");

        // Só itens vinculados a produto e com quantidade positiva viram pedido
        // (CriarSugestaoCompra exige quantidade > 0).
        var itensComProduto = lista.Itens
            .Where(i => i.ProdutoId.HasValue && (i.Quantidade ?? 0m) > 0m)
            .ToList();
        var ignorados = lista.Itens.Count - itensComProduto.Count;

        if (itensComProduto.Count == 0)
            return new GerarPedidosDaListaResult([], [], ignorados);

        // Unidade da lista é texto livre; o pedido usa Un por padrão (só afeta o display do item).
        var insumos = itensComProduto
            .Select(i => new InsumoFaltanteInput(i.ProdutoId!.Value, i.Quantidade!.Value, UnidadeMedida.Un))
            .ToList();

        var preview = await previewUseCase.ExecuteAsync(
            new PreviewSugestaoCompraCommand(cmd.EmpresaId, cmd.LojaId, insumos), ct);

        var gruposComFornecedor = preview.PorFornecedor.Where(g => g.FornecedorId.HasValue).ToList();
        var itensSemFornecedor = preview.PorFornecedor
            .Where(g => !g.FornecedorId.HasValue)
            .SelectMany(g => g.Linhas.Select(l => l.InsumoNome))
            .ToList();

        if (gruposComFornecedor.Count == 0)
            return new GerarPedidosDaListaResult([], itensSemFornecedor, ignorados);

        var fornecedores = gruposComFornecedor.Select(g => new FornecedorGrupoInput(
            g.FornecedorId!.Value,
            g.Linhas.Select(l => new ItemFaltanteInput(
                l.InsumoId, l.InsumoNome, l.Quantidade, l.Unidade,
                l.CustoUnitarioReferencia ?? 0m, null)).ToList()
        )).ToList();

        var criado = await criarUseCase.ExecuteAsync(
            new CriarSugestaoCompraCommand(
                cmd.EmpresaId, cmd.LojaId, fornecedores,
                "lista-compras", $"Gerado da lista '{lista.Nome}'", cmd.IdempotencyKey),
            ct);

        // Junta o pedido criado com as linhas (preview) + telefone (fornecedor) para a UI/WhatsApp.
        var pedidos = new List<PedidoGeradoResult>(criado.PedidosCriados.Count);
        foreach (var pc in criado.PedidosCriados)
        {
            var grupo = gruposComFornecedor.First(g => g.FornecedorId == pc.FornecedorId);
            var fornecedor = await fornecedorRepository.GetByIdAsync(cmd.EmpresaId, pc.FornecedorId);
            pedidos.Add(new PedidoGeradoResult(
                pc.PedidoFornecedorId, pc.FornecedorId, pc.FornecedorNome, fornecedor?.Telefone,
                pc.ValorEstimado,
                grupo.Linhas.Select(l => new PedidoGeradoItemResult(
                    l.InsumoNome, l.Quantidade, l.Unidade.ToString())).ToList()));
        }

        logger.LogInformation(
            "Lista {ListaId}: gerados {Count} pedidos de fornecedor ({SemForn} itens sem fornecedor, {Ign} ignorados).",
            lista.Id, pedidos.Count, itensSemFornecedor.Count, ignorados);

        return new GerarPedidosDaListaResult(pedidos, itensSemFornecedor, ignorados);
    }
}
