using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.Producao;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <summary>Um prato produzido, como ela anota: porções, peso de cada uma, peso real e validade.</summary>
public sealed record PratoProduzidoInput(Guid CardapioItemId, int Porcoes, int? PesoPorPorcaoG, int? PesoRealG, int ValidadeDias);

public sealed record ProduzirPratosCommand(
    Guid EmpresaId, Guid UsuarioId, string? OperadorNome, IReadOnlyList<PratoProduzidoInput>? Pratos, string? Observacao = null);

public sealed record PratoProduzidoResult(Guid CardapioItemId, string Nome, int Porcoes, int? SobraG, DateTime ValidadeEm);

public sealed record ProduzirPratosResult(Guid LoteId, string CodigoLote, int TotalEtiquetas, IReadOnlyList<PratoProduzidoResult> Pratos);

/// <summary>
/// Produção do dia pelo console (M2.2, #1491). Ela escolhe o prato do cardápio; o produto do estoque
/// é resolvido aqui. Prato avulso (incluído pelo console, sem produto) ganha um produto de estoque
/// na primeira produção, na categoria de estoque "Cardápio", e fica ligado a ele (D-M1-01: o que a
/// casa produz é vinculado). Depois delega ao <see cref="RegistrarProducaoUseCase"/> (S23): lote,
/// etiquetas e entrada em porções numa transação. D-M2-04: o destino vem da linha do prato.
/// D-M2-01: a baixa de insumo pela receita entra com a M2.4.
/// </summary>
public sealed class ProduzirPratosUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository,
    ICategoriaRepository categoriaRepository,
    CadastrarProdutoUseCase cadastrarProduto,
    RegistrarProducaoUseCase registrarProducao,
    IUnitOfWork unitOfWork)
{
    public const string CategoriaDeEstoque = "Cardápio";

    public async Task<ProduzirPratosResult> ExecuteAsync(ProduzirPratosCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.Pratos is null || cmd.Pratos.Count == 0)
            throw new UseCaseValidationException("Informe ao menos um prato produzido.");
        if (cmd.Pratos.GroupBy(p => p.CardapioItemId).Any(g => g.Count() > 1))
            throw new UseCaseValidationException("O mesmo prato apareceu duas vezes: some as porções numa linha só.");

        var storefront = await storefrontRepository.GetByEmpresaAsync(cmd.EmpresaId, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException();

        var pratos = new List<CardapioItem>(cmd.Pratos.Count);
        foreach (var p in cmd.Pratos)
        {
            var item = await cardapioRepository.GetByIdAndScopeAsync(storefront.Id, p.CardapioItemId, cmd.EmpresaId, ct)
                ?? throw new CardapioItemNaoEncontradoException(storefront.Id, p.CardapioItemId);
            if (item.EstaArquivado)
                throw new UseCaseValidationException($"\"{item.NomeEfetivo()}\" está fora do cardápio: reponha antes de produzir.");
            pratos.Add(item);
        }

        await VincularAvulsosAsync(cmd, pratos, ct);

        var producao = await registrarProducao.ExecuteAsync(new RegistrarProducaoCommand(
            cmd.EmpresaId, null, null,
            cmd.Pratos.Select((p, i) => new RegistrarProducaoItemInput(
                pratos[i].ProdutoId!.Value, p.Porcoes, p.PesoPorPorcaoG, p.ValidadeDias, null, p.PesoRealG)).ToList(),
            cmd.Observacao,
            cmd.UsuarioId == Guid.Empty ? null : cmd.UsuarioId,
            cmd.OperadorNome), ct);

        return new ProduzirPratosResult(
            producao.LoteId, producao.CodigoLote, producao.TotalEtiquetas,
            producao.Itens.Select((r, i) => new PratoProduzidoResult(
                pratos[i].Id, Exibicao(pratos[i].NomeEfetivo()), r.Porcoes, r.SobraG, r.ValidadeEm)).ToList());
    }

    private async Task VincularAvulsosAsync(ProduzirPratosCommand cmd, IReadOnlyList<CardapioItem> pratos, CancellationToken ct)
    {
        var avulsos = pratos.Where(p => !p.ProdutoId.HasValue).ToList();
        if (avulsos.Count == 0) return;

        var categoriaId = await CategoriaDeEstoqueAsync(cmd.EmpresaId);
        foreach (var prato in avulsos)
        {
            var produto = await cadastrarProduto.ExecuteAsync(new CadastrarProdutoCommand(
                cmd.EmpresaId, categoriaId, null, Exibicao(prato.NomeEfetivo()), null, null, TipoProduto.Alimento,
                null, null, true, null, null, prato.PrecoEfetivo(), null, null, null, null, null, null, cmd.UsuarioId));
            prato.VincularProduto(produto.ProdutoId);
        }
        await unitOfWork.CommitAsync();
    }

    private async Task<Guid> CategoriaDeEstoqueAsync(Guid empresaId)
    {
        var existente = (await categoriaRepository.GetByEmpresaAsync(empresaId))
            .FirstOrDefault(c => string.Equals(c.Nome.Trim(), CategoriaDeEstoque, StringComparison.OrdinalIgnoreCase));
        if (existente is not null) return existente.Id;

        var agora = DateTime.UtcNow;
        var categoria = new Categoria
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = CategoriaDeEstoque,
            Descricao = "Pratos produzidos pela casa (criada pela produção do console)",
            CriadoEm = agora, AlteradoEm = agora,
        };
        await categoriaRepository.AddAsync(categoria);
        await unitOfWork.CommitAsync();
        return categoria.Id;
    }

    // O nome do avulso é gravado em minúsculo (factory do item): o produto nasce com inicial maiúscula.
    private static string Exibicao(string? nome)
    {
        var limpo = (nome ?? "Prato").Trim();
        return limpo.Length == 0 ? "Prato" : char.ToUpperInvariant(limpo[0]) + limpo[1..];
    }
}
