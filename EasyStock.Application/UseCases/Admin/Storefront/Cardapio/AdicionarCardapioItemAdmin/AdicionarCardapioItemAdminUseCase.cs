using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;

/// <summary>
/// Adiciona item ao cardápio do storefront. Suporta dois modos (ADR-0031):
///
/// <list type="bullet">
/// <item><term>Vinculado</term>
/// <description>ProdutoId presente. Produto deve existir e pertencer à mesma Empresa.</description></item>
/// <item><term>Avulso</term>
/// <description>ProdutoId null. NomePublico e PrecoStorefront obrigatórios.
/// Não requer produto no ERP.</description></item>
/// </list>
///
/// Garante tenant isolation e idempotência (produto vinculado não pode duplicar por storefront).
/// </summary>
public sealed record AdicionarCardapioItemAdminCommand(
    Guid StorefrontId,
    Guid? ProdutoId,       // null = item avulso
    string? NomePublico,   // obrigatório para avulso; override opcional para vinculado
    string? CategoriaTexto,
    double OrdemExibicao,
    bool Visivel,
    string? DescricaoPublica,
    string? Ingredientes,
    string? Alergenos,
    string? SugestaoMolho,
    string? TempoPreparo,
    string? FotoUrl,
    decimal? PrecoStorefront,
    string? Tag,
    string? PesoExibicao,
    string? FiltrosJson) : ICommand;

public sealed record AdicionarCardapioItemAdminResult(Guid ItemId, Guid StorefrontId, Guid? ProdutoId);

public class AdicionarCardapioItemAdminUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository,
    IProdutoRepository produtoRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<AdicionarCardapioItemAdminCommand, AdicionarCardapioItemAdminResult>
{
    public async Task<AdicionarCardapioItemAdminResult> ExecuteAsync(AdicionarCardapioItemAdminCommand command)
    {
        var storefront = await storefrontRepository.GetByIdAsync(command.StorefrontId)
            ?? throw new StorefrontNaoEncontradoException();

        CardapioItem item;

        if (command.ProdutoId.HasValue)
        {
            // ── Modo VINCULADO: item ligado a um Produto do ERP ──────────────
            // Tenant isolation: produto deve pertencer à mesma empresa do storefront.
            var produto = await produtoRepository.GetByIdAsync(storefront.EmpresaId, command.ProdutoId.Value)
                ?? throw new UseCaseValidationException(
                    "PRODUTO_INEXISTENTE",
                    $"Produto {command.ProdutoId.Value} não encontrado para a empresa {storefront.EmpresaId}.");

            var jaUsados = await cardapioRepository.GetProdutoIdsDoStorefrontAsync(command.StorefrontId);
            if (jaUsados.Contains(produto.Id))
                throw new UseCaseValidationException(
                    "PRODUTO_JA_NO_CARDAPIO",
                    $"Produto '{produto.Nome}' já está no cardápio deste storefront.");

            item = CardapioItem.CriarAPartirDeProduto(command.StorefrontId, produto);
        }
        else
        {
            // ── Modo AVULSO: item sem vínculo com ERP (ADR-0031) ─────────────
            // NomePublico e PrecoStorefront obrigatórios (validados pela entity).
            if (string.IsNullOrWhiteSpace(command.NomePublico))
                throw new UseCaseValidationException(
                    "NOME_OBRIGATORIO",
                    "Nome é obrigatório para item avulso.");

            if (!command.PrecoStorefront.HasValue || command.PrecoStorefront.Value <= 0m)
                throw new UseCaseValidationException(
                    "PRECO_OBRIGATORIO",
                    "Preço é obrigatório e deve ser positivo para item avulso.");

            item = CardapioItem.CriarAvulso(
                command.StorefrontId,
                command.NomePublico,
                command.PrecoStorefront.Value,
                command.CategoriaTexto);
        }

        // Aplica ordem e visibilidade fora dos defaults (factory cria com Visivel=false, ordem=0).
        if (command.OrdemExibicao > 0)
            item.DefinirOrdem(command.OrdemExibicao);

        if (command.Visivel)
            item.TornarVisivel();

        // Aplica metadata opcional (NomePublico/CategoriaTexto inclusas para override em vinculado).
        var temMetadata = command.NomePublico is not null
            || command.CategoriaTexto is not null
            || command.DescricaoPublica is not null
            || command.Ingredientes is not null
            || command.Alergenos is not null
            || command.SugestaoMolho is not null
            || command.TempoPreparo is not null
            || command.FotoUrl is not null
            || command.PrecoStorefront.HasValue
            || command.Tag is not null
            || command.PesoExibicao is not null
            || command.FiltrosJson is not null;

        // Para avulso: já criou com NomePublico/PrecoStorefront na factory — evitar re-setar.
        if (temMetadata && command.ProdutoId.HasValue)
        {
            item.AtualizarMetadata(
                nomePublico: command.NomePublico,
                categoriaTexto: command.CategoriaTexto,
                descricaoPublica: command.DescricaoPublica,
                ingredientes: command.Ingredientes,
                alergenos: command.Alergenos,
                sugestaoMolho: command.SugestaoMolho,
                tempoPreparo: command.TempoPreparo,
                fotoUrl: command.FotoUrl,
                precoStorefront: command.PrecoStorefront,
                tag: command.Tag,
                filtrosJson: command.FiltrosJson,
                pesoExibicao: command.PesoExibicao);
        }
        else if (temMetadata)
        {
            // Avulso: NomePublico/Categoria/Preco já na factory; só aplica metadata extra.
            item.AtualizarMetadata(
                descricaoPublica: command.DescricaoPublica,
                ingredientes: command.Ingredientes,
                alergenos: command.Alergenos,
                sugestaoMolho: command.SugestaoMolho,
                tempoPreparo: command.TempoPreparo,
                fotoUrl: command.FotoUrl,
                tag: command.Tag,
                filtrosJson: command.FiltrosJson,
                pesoExibicao: command.PesoExibicao);
        }

        await cardapioRepository.AddAsync(item);
        await unitOfWork.CommitAsync();

        return new AdicionarCardapioItemAdminResult(item.Id, command.StorefrontId, command.ProdutoId);
    }
}
