using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.Producao;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <summary>Um prato produzido, como ela anota: porções, peso de cada uma, peso real e validade.</summary>
/// <param name="VariacaoId">M1.4c (#1537): a porção produzida (300 g × 800 g). Obrigatória em prato com porções.</param>
public sealed record PratoProduzidoInput(
    Guid CardapioItemId, int Porcoes, int? PesoPorPorcaoG, int? PesoRealG, int ValidadeDias, Guid? VariacaoId = null);

public sealed record ProduzirPratosCommand(
    Guid EmpresaId, Guid UsuarioId, string? OperadorNome, IReadOnlyList<PratoProduzidoInput>? Pratos, string? Observacao = null);

public sealed record PratoProduzidoResult(Guid CardapioItemId, string Nome, int Porcoes, int? SobraG, DateTime ValidadeEm);

/// <param name="Avisos">D-M2-01 (#1499): falta de insumo na baixa automática. Avisa, não trava.</param>
public sealed record ProduzirPratosResult(
    Guid LoteId, string CodigoLote, int TotalEtiquetas, IReadOnlyList<PratoProduzidoResult> Pratos, IReadOnlyList<string>? Avisos = null);

/// <summary>
/// Produção do dia pelo console (M2.2, #1491). Ela escolhe o prato do cardápio; o produto do estoque
/// é resolvido aqui. Prato avulso (incluído pelo console, sem produto) ganha um produto de estoque
/// na primeira produção, na categoria de estoque "Cardápio", e fica ligado a ele (D-M1-01: o que a
/// casa produz é vinculado). Depois delega ao <see cref="RegistrarProducaoUseCase"/> (S23): lote,
/// etiquetas e entrada em porções numa transação. D-M2-04: o destino vem da linha do prato.
/// D-M2-01 (#1499): a S23 baixa os insumos do prato marcado e devolve os avisos de falta.
/// </summary>
public sealed class ProduzirPratosUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository,
    ICategoriaRepository categoriaRepository,
    CadastrarProdutoUseCase cadastrarProduto,
    RegistrarProducaoUseCase registrarProducao,
    IUnitOfWork unitOfWork,
    EasyStock.Application.UseCases.Atendimento.Comanda.VincularPorcoesAoEstoqueUseCase vincularPorcoes)
{
    public const string CategoriaPratos = "Cardápio";

    public async Task<ProduzirPratosResult> ExecuteAsync(ProduzirPratosCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.Pratos is null || cmd.Pratos.Count == 0)
            throw new UseCaseValidationException("Informe ao menos um prato produzido.");
        // M1.4c: o mesmo prato pode vir em duas linhas, desde que de porções diferentes.
        if (cmd.Pratos.GroupBy(p => (p.CardapioItemId, p.VariacaoId)).Any(g => g.Count() > 1))
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
            // M1.4c (#1537, D-M1-03): prato com porções produz uma porção; o saldo é dela.
            if (item.TemVariacoes() && item.Variacoes.All(v => v.Id != p.VariacaoId))
                throw new UseCaseValidationException($"Escolha a porção de \"{item.NomeEfetivo()}\" que você produziu.");
            if (!item.TemVariacoes() && p.VariacaoId is not null)
                throw new UseCaseValidationException($"\"{item.NomeEfetivo()}\" não tem essa porção.");
            pratos.Add(item);
        }

        await VincularAvulsosAsync(cmd, pratos, ct);
        await VincularPorcoesAsync(cmd.EmpresaId, pratos);
        var porcoes = cmd.Pratos.Select((p, i) => pratos[i].Variacoes.FirstOrDefault(v => v.Id == p.VariacaoId)).ToList();

        var producao = await registrarProducao.ExecuteAsync(new RegistrarProducaoCommand(
            cmd.EmpresaId, null, null,
            cmd.Pratos.Select((p, i) => new RegistrarProducaoItemInput(
                pratos[i].ProdutoId!.Value, p.Porcoes, p.PesoPorPorcaoG, p.ValidadeDias, null, p.PesoRealG,
                porcoes[i]?.ProdutoVariacaoId, porcoes[i]?.Rotulo)).ToList(),
            cmd.Observacao,
            cmd.UsuarioId == Guid.Empty ? null : cmd.UsuarioId,
            cmd.OperadorNome), ct);

        return new ProduzirPratosResult(
            producao.LoteId, producao.CodigoLote, producao.TotalEtiquetas,
            producao.Itens.Select((r, i) => new PratoProduzidoResult(
                pratos[i].Id, Exibicao(pratos[i].NomeEfetivo()) + (porcoes[i] is { } v ? $" {v.Rotulo}" : ""),
                r.Porcoes, r.SobraG, r.ValidadeEm)).ToList(),
            producao.Avisos ?? []);
    }

    private async Task VincularAvulsosAsync(ProduzirPratosCommand cmd, IReadOnlyList<CardapioItem> pratos, CancellationToken ct)
    {
        var avulsos = pratos.Where(p => !p.ProdutoId.HasValue).Distinct().ToList();
        if (avulsos.Count == 0) return;

        var categoriaId = await CategoriaDeEstoque.ObterOuCriarAsync(categoriaRepository, unitOfWork, cmd.EmpresaId,
            CategoriaPratos, "Pratos produzidos pela casa (criada pela produção do console)");
        foreach (var prato in avulsos)
        {
            var produto = await cadastrarProduto.ExecuteAsync(new CadastrarProdutoCommand(
                cmd.EmpresaId, categoriaId, null, Exibicao(prato.NomeEfetivo()), null, null, TipoProduto.Alimento,
                null, null, true, null, null, prato.PrecoEfetivo(), null, null, null, null, null, null, cmd.UsuarioId));
            prato.VincularProduto(produto.ProdutoId);
        }
        await unitOfWork.CommitAsync();
    }

    // M1.4c (#1537): porção sem variação do estoque (prato avulso recém-ligado, porção antiga) ganha a dela
    // antes da entrada, para o saldo nascer na porção certa.
    private async Task VincularPorcoesAsync(Guid empresaId, IReadOnlyList<CardapioItem> pratos)
    {
        var pendentes = pratos.Distinct().Where(p => p.Variacoes.Any(v => v.ProdutoVariacaoId is null)).ToList();
        if (pendentes.Count == 0) return;
        foreach (var prato in pendentes) await vincularPorcoes.ExecuteAsync(empresaId, prato);
        await unitOfWork.CommitAsync();
    }

    // O nome do avulso é gravado em minúsculo (factory do item): o produto nasce com inicial maiúscula.
    private static string Exibicao(string? nome)
    {
        var limpo = (nome ?? "Prato").Trim();
        return limpo.Length == 0 ? "Prato" : char.ToUpperInvariant(limpo[0]) + limpo[1..];
    }
}
