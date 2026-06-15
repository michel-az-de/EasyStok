using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;

namespace EasyStock.Application.UseCases.AdicionarItemPedido;

public sealed record AdicionarItemPedidoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid PedidoId,
    [property: Required][property: MaxLength(150)] string Nome,
    decimal Quantidade,
    decimal PrecoUnitario,
    Guid? ProdutoId = null,
    [property: MaxLength(16)] string? Emoji = null,
    [property: MaxLength(32)] string? Unidade = null,
    string? Observacao = null,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    [property: MaxLength(20)] string? Origem = "web");

public class AdicionarItemPedidoUseCase(
    IPedidoRepository repo,
    IProdutoRepository produtoRepo,
    IUnitOfWork uow,
    ILogger<AdicionarItemPedidoUseCase> logger)
{
    public async Task<PedidoResult?> ExecuteAsync(AdicionarItemPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");
        if (cmd.Quantidade <= 0)
            throw new UseCaseValidationException("Quantidade deve ser maior que zero.");

        if (string.IsNullOrWhiteSpace(cmd.Nome))
            throw new UseCaseValidationException("Nome do item é obrigatório.");

        if (cmd.PrecoUnitario < 0)
            throw new UseCaseValidationException("PrecoUnitario não pode ser negativo.");

        // Tenant isolation: ProdutoId, se informado, precisa pertencer a esta empresa.
        // Sem isso, atacante autenticado pode anexar produto de outro tenant ao pedido.
        if (cmd.ProdutoId.HasValue && cmd.ProdutoId.Value != Guid.Empty)
        {
            var produto = await produtoRepo.GetByIdAsync(cmd.EmpresaId, cmd.ProdutoId.Value);
            if (produto is null)
                throw new UseCaseValidationException("Produto do item não pertence a esta empresa.");
        }

        var pedido = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.PedidoId);
        if (pedido == null) return null;
        if (pedido.EstaFinalizado)
            throw new UseCaseValidationException("Não é permitido alterar itens de pedido finalizado.");

        var item = new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            ProdutoId = cmd.ProdutoId,
            Nome = cmd.Nome.Trim(),
            Emoji = cmd.Emoji,
            Unidade = cmd.Unidade,
            Quantidade = cmd.Quantidade,
            PrecoUnitario = cmd.PrecoUnitario,
            Observacao = cmd.Observacao,
            CriadoEm = DateTime.UtcNow
        };
        item.RecalcularSubtotal();

        await repo.AddItemAsync(item);
        // O relationship fixup do EF Core ja insere o item em pedido.Itens, pois o
        // pedido vem rastreado com Itens carregado (GetByIdWithDetailsAsync). Sem a
        // guarda, pedido.Itens.Add duplicaria a MESMA instancia na colecao e
        // RecalcularTotal contaria o item 2x, inflando o Total (QA v1.10 BUG-001, #595).
        // A guarda mantem 1 ocorrencia mesmo se o fixup nao disparar (provider futuro
        // sem tracking) -> nunca sub/superconta.
        if (!pedido.Itens.Contains(item))
            pedido.Itens.Add(item);
        pedido.RecalcularTotal();

        await repo.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "item_added",
            UsuarioId = cmd.UsuarioId,
            UsuarioNome = cmd.UsuarioNome,
            Origem = cmd.Origem,
            OcorridoEm = DateTime.UtcNow,
            Detalhes = $"+{item.Quantidade} {item.Nome} ({item.Subtotal:C})"
        });

        await repo.UpdateAsync(pedido);
        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id}: item {Item} adicionado, novo total {Total}.",
            pedido.Id, item.Nome, pedido.Total);
        return CriarPedidoUseCase.Map(pedido);
    }
}
