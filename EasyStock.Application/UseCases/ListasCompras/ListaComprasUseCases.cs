using ListaComprasEntity = EasyStock.Domain.Entities.ListaCompras;

namespace EasyStock.Application.UseCases.ListasCompras;

// ── DTOs ─────────────────────────────────────────────────────────────
public sealed record ListaComprasResult(
    Guid Id, Guid EmpresaId, Guid? LojaId,
    string Nome, string Status, string? Observacoes,
    Guid? CriadaPorUserId, string? CriadaPorNome, string? Origem,
    int TotalItens, int ItensFeitos, int ItensPendentes,
    DateTime CriadoEm, DateTime AlteradoEm, DateTime? ArquivadoEm
);

public sealed record ListaComprasDetalheResult(
    ListaComprasResult Lista,
    IReadOnlyList<ItemListaComprasResult> Itens
);

public sealed record ItemListaComprasResult(
    Guid Id, Guid ListaComprasId, Guid? ProdutoId,
    string Texto, decimal? Quantidade, string? Unidade,
    string? Observacao, string? Categoria,
    bool Done, DateTime? DoneEm, Guid? DonePorUserId, string? DonePorNome,
    DateTime CriadoEm, DateTime AlteradoEm
);

internal static class ListaComprasMapper
{
    public static ListaComprasResult Map(ListaComprasEntity l) => new(
        l.Id, l.EmpresaId, l.LojaId, l.Nome, l.Status, l.Observacoes,
        l.CriadaPorUserId, l.CriadaPorNome, l.Origem,
        l.TotalItens, l.ItensFeitos, l.ItensPendentes,
        l.CriadoEm, l.AlteradoEm, l.ArquivadoEm);

    public static ItemListaComprasResult Map(ItemListaCompras i) => new(
        i.Id, i.ListaComprasId, i.ProdutoId, i.Texto, i.Quantidade, i.Unidade,
        i.Observacao, i.Categoria,
        i.Done, i.DoneEm, i.DonePorUserId, i.DonePorNome,
        i.CriadoEm, i.AlteradoEm);
}

// ── Listar ───────────────────────────────────────────────────────────
public sealed record ListarListasComprasQuery(Guid EmpresaId, int Page = 1, int PageSize = 30, string? Status = null, string? Search = null);

public sealed record ListarListasComprasResult(IReadOnlyList<ListaComprasResult> Items, int Total, int Page, int PageSize);

public class ListarListasComprasUseCase(IListaComprasRepository repo)
{
    public async Task<ListarListasComprasResult> ExecuteAsync(ListarListasComprasQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);
        var (items, total) = await repo.ListAsync(q.EmpresaId, page, size, q.Status, q.Search);
        return new ListarListasComprasResult(
            items.Select(ListaComprasMapper.Map).ToList(), total, page, size);
    }
}

// ── ObterDetalhes ────────────────────────────────────────────────────
public sealed record ObterListaComprasQuery(Guid EmpresaId, Guid Id);

public class ObterListaComprasUseCase(IListaComprasRepository repo)
{
    public async Task<ListaComprasDetalheResult?> ExecuteAsync(ObterListaComprasQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(q.Id, "Id");
        var l = await repo.GetByIdWithItemsAsync(q.EmpresaId, q.Id);
        if (l == null) return null;
        return new ListaComprasDetalheResult(
            ListaComprasMapper.Map(l),
            l.Itens.OrderBy(i => i.Done).ThenBy(i => i.CriadoEm)
                   .Select(ListaComprasMapper.Map).ToList());
    }
}

// ── Criar ────────────────────────────────────────────────────────────
public sealed record CriarListaComprasCommand(
    [property: Required] Guid EmpresaId,
    [property: Required][property: MaxLength(120)] string Nome,
    Guid? LojaId = null,
    string? Observacoes = null,
    Guid? CriadaPorUserId = null,
    [property: MaxLength(120)] string? CriadaPorNome = null,
    [property: MaxLength(20)] string? Origem = "web");

public class CriarListaComprasUseCase(
    IListaComprasRepository repo, IUnitOfWork uow, ILogger<CriarListaComprasUseCase> logger)
{
    public async Task<ListaComprasResult> ExecuteAsync(CriarListaComprasCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (string.IsNullOrWhiteSpace(cmd.Nome))
            throw new UseCaseValidationException("Nome da lista é obrigatório.");

        var lista = ListaComprasEntity.Criar(cmd.EmpresaId, cmd.Nome, cmd.LojaId, cmd.Origem);
        lista.Observacoes = cmd.Observacoes;
        lista.CriadaPorUserId = cmd.CriadaPorUserId;
        lista.CriadaPorNome = cmd.CriadaPorNome;

        await repo.AddAsync(lista);
        await uow.CommitAsync();

        logger.LogInformation("Lista de compras {Id} '{Nome}' criada.", lista.Id, lista.Nome);
        return ListaComprasMapper.Map(lista);
    }
}

// ── ArquivarLista ────────────────────────────────────────────────────
public sealed record ArquivarListaComprasCommand(Guid EmpresaId, Guid Id);

public class ArquivarListaComprasUseCase(
    IListaComprasRepository repo, IUnitOfWork uow, ILogger<ArquivarListaComprasUseCase> logger)
{
    public async Task<ListaComprasResult?> ExecuteAsync(ArquivarListaComprasCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        var lista = await repo.GetByIdWithItemsAsync(cmd.EmpresaId, cmd.Id);
        if (lista == null) return null;
        if (lista.EstaArquivada) return ListaComprasMapper.Map(lista);

        lista.Arquivar();
        await repo.UpdateAsync(lista);
        await uow.CommitAsync();
        logger.LogInformation("Lista {Id} arquivada.", lista.Id);
        return ListaComprasMapper.Map(lista);
    }
}

// ── ReabrirLista ─────────────────────────────────────────────────────
public sealed record ReabrirListaComprasCommand(Guid EmpresaId, Guid Id);

public class ReabrirListaComprasUseCase(
    IListaComprasRepository repo, IUnitOfWork uow, ILogger<ReabrirListaComprasUseCase> logger)
{
    public async Task<ListaComprasResult?> ExecuteAsync(ReabrirListaComprasCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        var lista = await repo.GetByIdWithItemsAsync(cmd.EmpresaId, cmd.Id);
        if (lista == null) return null;
        if (!lista.EstaArquivada) return ListaComprasMapper.Map(lista);

        lista.Reabrir();
        await repo.UpdateAsync(lista);
        await uow.CommitAsync();
        logger.LogInformation("Lista {Id} reaberta.", lista.Id);
        return ListaComprasMapper.Map(lista);
    }
}

// ── AdicionarItem ────────────────────────────────────────────────────
public sealed record AdicionarItemListaComprasCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ListaComprasId,
    [property: Required][property: MaxLength(255)] string Texto,
    decimal? Quantidade = null,
    [property: MaxLength(32)] string? Unidade = null,
    string? Observacao = null,
    [property: MaxLength(60)] string? Categoria = null,
    Guid? ProdutoId = null);

public class AdicionarItemListaComprasUseCase(
    IListaComprasRepository repo, IUnitOfWork uow, ILogger<AdicionarItemListaComprasUseCase> logger)
{
    public async Task<ItemListaComprasResult?> ExecuteAsync(AdicionarItemListaComprasCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ListaComprasId, "ListaComprasId");
        if (string.IsNullOrWhiteSpace(cmd.Texto))
            throw new UseCaseValidationException("Texto do item é obrigatório.");

        var lista = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ListaComprasId);
        if (lista == null) return null;
        if (lista.EstaArquivada)
            throw new UseCaseValidationException("Lista arquivada — reabra antes de adicionar itens.");

        var agora = DateTime.UtcNow;
        var item = new ItemListaCompras
        {
            Id = Guid.NewGuid(),
            ListaComprasId = lista.Id,
            ProdutoId = cmd.ProdutoId,
            Texto = cmd.Texto.Trim(),
            Quantidade = cmd.Quantidade,
            Unidade = cmd.Unidade,
            Observacao = cmd.Observacao,
            Categoria = cmd.Categoria,
            Done = false,
            CriadoEm = agora,
            AlteradoEm = agora
        };

        await repo.AddItemAsync(item);
        lista.AlteradoEm = agora;
        await repo.UpdateAsync(lista);
        await uow.CommitAsync();

        logger.LogInformation("Lista {Id}: item '{Texto}' adicionado.", lista.Id, item.Texto);
        return ListaComprasMapper.Map(item);
    }
}

// ── ToggleItem ──────────────────────────────────────────────────────
public sealed record ToggleItemListaComprasCommand(
    Guid EmpresaId,
    Guid ListaComprasId,
    Guid ItemId,
    bool Done,
    Guid? UsuarioId = null,
    string? UsuarioNome = null);

public class ToggleItemListaComprasUseCase(
    IListaComprasRepository repo, IUnitOfWork uow, ILogger<ToggleItemListaComprasUseCase> logger)
{
    public async Task<ItemListaComprasResult?> ExecuteAsync(ToggleItemListaComprasCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ItemId, "ItemId");

        var lista = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ListaComprasId);
        if (lista == null) return null;

        var item = await repo.GetItemAsync(cmd.ItemId);
        if (item == null || item.ListaComprasId != cmd.ListaComprasId) return null;

        if (cmd.Done) item.MarcarDone(cmd.UsuarioId, cmd.UsuarioNome);
        else          item.Desmarcar();

        await repo.UpdateItemAsync(item);
        lista.AlteradoEm = DateTime.UtcNow;
        await repo.UpdateAsync(lista);
        await uow.CommitAsync();

        logger.LogInformation("Item {Id} -> done={Done}.", item.Id, cmd.Done);
        return ListaComprasMapper.Map(item);
    }
}

// ── RemoverItem ──────────────────────────────────────────────────────
public sealed record RemoverItemListaComprasCommand(Guid EmpresaId, Guid ListaComprasId, Guid ItemId);

public class RemoverItemListaComprasUseCase(
    IListaComprasRepository repo, IUnitOfWork uow, ILogger<RemoverItemListaComprasUseCase> logger)
{
    public async Task<bool> ExecuteAsync(RemoverItemListaComprasCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ItemId, "ItemId");

        var lista = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ListaComprasId);
        if (lista == null) return false;

        var item = await repo.GetItemAsync(cmd.ItemId);
        if (item == null || item.ListaComprasId != cmd.ListaComprasId) return false;

        await repo.RemoveItemAsync(item.Id);
        lista.AlteradoEm = DateTime.UtcNow;
        await repo.UpdateAsync(lista);
        await uow.CommitAsync();

        logger.LogInformation("Item {Id} removido da lista {ListaId}.", cmd.ItemId, cmd.ListaComprasId);
        return true;
    }
}

// ── GerarLista (criar lista já populada com itens) ───────────────────
public sealed record GerarItemListaComprasInput(
    [property: Required][property: MaxLength(255)] string Texto,
    Guid? ProdutoId = null,
    decimal? Quantidade = null,
    [property: MaxLength(32)] string? Unidade = null,
    string? Observacao = null,
    [property: MaxLength(60)] string? Categoria = null);

public sealed record GerarListaComprasCommand(
    [property: Required] Guid EmpresaId,
    [property: Required][property: MaxLength(120)] string Nome,
    IReadOnlyList<GerarItemListaComprasInput> Itens,
    Guid? LojaId = null,
    string? Observacoes = null,
    Guid? CriadaPorUserId = null,
    [property: MaxLength(120)] string? CriadaPorNome = null,
    [property: MaxLength(20)] string? Origem = "web");

public class GerarListaComprasUseCase(
    IListaComprasRepository repo, IUnitOfWork uow, ILogger<GerarListaComprasUseCase> logger)
{
    public async Task<ListaComprasResult> ExecuteAsync(GerarListaComprasCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (string.IsNullOrWhiteSpace(cmd.Nome))
            throw new UseCaseValidationException("Nome da lista é obrigatório.");

        var itensValidos = (cmd.Itens ?? Array.Empty<GerarItemListaComprasInput>())
            .Where(i => !string.IsNullOrWhiteSpace(i.Texto))
            .ToList();
        if (itensValidos.Count == 0)
            throw new UseCaseValidationException("Selecione ao menos um item para gerar a lista.");

        var agora = DateTime.UtcNow;
        var lista = ListaComprasEntity.Criar(cmd.EmpresaId, cmd.Nome, cmd.LojaId, cmd.Origem);
        lista.Observacoes = cmd.Observacoes;
        lista.CriadaPorUserId = cmd.CriadaPorUserId;
        lista.CriadaPorNome = cmd.CriadaPorNome;

        foreach (var input in itensValidos)
        {
            lista.Itens.Add(new ItemListaCompras
            {
                Id = Guid.NewGuid(),
                ListaComprasId = lista.Id,
                ProdutoId = input.ProdutoId,
                Texto = input.Texto.Trim(),
                Quantidade = input.Quantidade,
                Unidade = input.Unidade,
                Observacao = input.Observacao,
                Categoria = input.Categoria,
                Done = false,
                CriadoEm = agora,
                AlteradoEm = agora
            });
        }

        // Itens vão na coleção de navegação: EF insere lista + itens em cascata num único commit.
        await repo.AddAsync(lista);
        await uow.CommitAsync();

        logger.LogInformation("Lista de compras {Id} '{Nome}' gerada com {Count} itens.",
            lista.Id, lista.Nome, itensValidos.Count);
        return ListaComprasMapper.Map(lista);
    }
}
