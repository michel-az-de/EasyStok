using EasyStock.Application.UseCases.GerenciarProduto.Helpers;

namespace EasyStock.Application.UseCases.GerenciarProduto.Queries;

/// <summary>
/// Query: detalhe completo do produto. Carrega produto + variacoes + caracteristicas
/// + embalagens + fotos (via ProdutoFotosSerializer) + itens de estoque + nomes de
/// usuario (criado/alterado por). Cache em memoria 120s por (empresa, produto), invalidado
/// por mutacao de saldo via EstoqueSaldoCacheInvalidationInterceptor (BUG-009/#517).
///
/// Extraido do god-UseCase <c>GerenciarProdutoUseCase</c> (F9b). O facade continua
/// expondo <c>ObterDetalheAsync</c> via delegacao, preservando contrato publico (R8).
/// </summary>
public sealed class ObterDetalheProdutoUseCase(
    IProdutoRepository produtoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    IProdutoVariacaoRepository produtoVariacaoRepository,
    ICacheService? cacheService = null,
    IUsuarioRepository? usuarioRepository = null)
{
    public async Task<ProdutoDetalheResult> ExecuteAsync(Guid empresaId, Guid produtoId)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(produtoId, "ProdutoId");

        // Cache de 120s — detalhe de produto inclui fotos, variações e itens de estoque (queries pesadas).
        // TTL deliberado (BUG-009/#517): o EstoqueSaldoCacheInvalidationInterceptor invalida
        // esta chave em toda mutação de saldo, então o TTL deixou de ser a única defesa e virou
        // rede de segurança das races residuais (read-repopulate + pré-commit) — 120s as estreita.
        if (cacheService is not null)
        {
            var cached = await cacheService.GetAsync<ProdutoDetalheResult>(CacheKeys.Produto(empresaId, produtoId));
            if (cached is not null) return cached;
        }

        var produto = await produtoRepository.GetDetalheAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        // Queries sequenciais — repositórios compartilham o mesmo DbContext (Scoped),
        // Task.WhenAll em paralelo causaria "A second operation was started on this context instance".
        var itens = await itemEstoqueRepository.GetByProdutoAsync(empresaId, produtoId);
        var variacoes = await produtoVariacaoRepository.GetByProdutoAsync(empresaId, produtoId);
        var fotos = ProdutoFotosSerializer.Deserializar(produto.FotosJson);

        var variacoesResult = variacoes
            .Select(variacao =>
            {
                var itensDaVariacao = itens.Where(i => i.ProdutoVariacaoId == variacao.Id).ToArray();
                return new ProdutoVariacaoDetalheResult(
                    variacao.Id,
                    variacao.Nome,
                    variacao.Cor,
                    variacao.Tamanho,
                    variacao.DescricaoComercial,
                    variacao.Sku?.Value,
                    variacao.CodigoBarras,
                    variacao.Ativa,
                    itensDaVariacao.Sum(i => i.QuantidadeAtual.Value),
                    itensDaVariacao.MaxBy(i => i.EntradaEm)?.EntradaEm);
            })
            .ToArray();

        var caracteristicasResult = (produto.Caracteristicas ?? [])
            .OrderBy(c => c.OrdemExibicao)
            .Select(c => new ProdutoCaracteristicaDetalheResult(
                c.Id, c.Nome, c.Descricao, c.QuantidadeReferencia, c.VariacaoPadrao, c.VariacaoId, c.OrdemExibicao))
            .ToArray();

        var embalagensResult = (produto.Embalagens ?? [])
            .Select(e => new ProdutoEmbalagemDetalheResult(
                e.Id, e.Nome, e.Descricao,
                e.Dimensoes is null ? null : new DimensoesDetalheResult(e.Dimensoes.Peso, e.Dimensoes.Largura, e.Dimensoes.Altura, e.Dimensoes.Comprimento),
                e.Padrao))
            .ToArray();

        // Resolver nomes de usuário para auditoria
        string? criadoPorNome = null, alteradoPorNome = null;
        if (usuarioRepository is not null)
        {
            if (produto.CriadoPor.HasValue && produto.CriadoPor != Guid.Empty)
                criadoPorNome = (await usuarioRepository.GetByIdAsync(produto.CriadoPor.Value))?.Nome;
            if (produto.AlteradoPor.HasValue && produto.AlteradoPor != Guid.Empty)
            {
                if (produto.AlteradoPor == produto.CriadoPor)
                    alteradoPorNome = criadoPorNome;
                else
                    alteradoPorNome = (await usuarioRepository.GetByIdAsync(produto.AlteradoPor.Value))?.Nome;
            }
        }

        var result = new ProdutoDetalheResult(
            produto.Id,
            produto.EmpresaId,
            produto.CategoriaId,
            produto.SubcategoriaId,
            produto.Nome,
            produto.DescricaoBase,
            produto.Marca,
            produto.Tipo,
            produto.SkuBase?.Value,
            produto.CodigoBarras,
            produto.ControlaValidade,
            produto.Status,
            produto.CustoReferencia?.Valor,
            produto.PrecoReferencia?.Valor,
            produto.MargemEstimada,
            produto.Dimensoes is null ? null : new DimensoesDetalheResult(produto.Dimensoes.Peso, produto.Dimensoes.Largura, produto.Dimensoes.Altura, produto.Dimensoes.Comprimento),
            itens.Sum(i => i.QuantidadeAtual.Value),
            itens.MaxBy(i => i.EntradaEm)?.EntradaEm,
            fotos.Select(f => new ProdutoFotoResult(f.FotoId, f.Url, f.CriadoEm)).ToArray(),
            variacoesResult,
            caracteristicasResult,
            embalagensResult,
            CriadoPor: produto.CriadoPor,
            AlteradoPor: produto.AlteradoPor,
            CriadoPorNome: criadoPorNome,
            AlteradoPorNome: alteradoPorNome,
            ObservacaoInterna: produto.ObservacaoInterna,
            CriadoEm: produto.CriadoEm,
            AlteradoEm: produto.AlteradoEm,
            QuantidadeMinima: produto.QuantidadeMinima,
            QuantidadeCritica: produto.QuantidadeCritica,
            TipoEmbalagem: produto.TipoEmbalagem, // C2 (RDC 727/2022)
            AtributosJson: produto.AtributosJson);

        if (cacheService is not null)
            await cacheService.SetAsync(CacheKeys.Produto(empresaId, produtoId), result, TimeSpan.FromSeconds(120));

        return result;
    }
}
