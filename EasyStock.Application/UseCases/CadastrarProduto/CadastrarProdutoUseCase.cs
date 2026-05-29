using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.CadastrarProduto
{
    public sealed record CadastrarProdutoCommand(
        [property: Required] Guid EmpresaId,
        [property: Required] Guid CategoriaId,
        Guid? SubcategoriaId,
        [property: Required][property: MinLength(1)][property: MaxLength(180)] string Nome,
        string? DescricaoBase,
        string? Marca,
        TipoProduto Tipo,
        string? SkuBase,
        string? CodigoBarras,
        bool ControlaValidade,
        DimensoesInput? Dimensoes,
        decimal? CustoReferencia,
        decimal? PrecoReferencia,
        decimal? MargemEstimada,
        string? AtributosJson,
        string? FotosJson,
        IReadOnlyCollection<ProdutoCaracteristicaInput>? Caracteristicas,
        IReadOnlyCollection<ProdutoEmbalagemInput>? Embalagens,
        IReadOnlyCollection<ProdutoVariacaoInput>? Variacoes,
        Guid UsuarioId = default,
        // C2 (RDC 727/2022): default Avulso para nao quebrar callers existentes.
        TipoEmbalagem TipoEmbalagem = TipoEmbalagem.Avulso);

    public sealed record CadastrarProdutoResult(
        Guid ProdutoId,
        IReadOnlyCollection<Guid> CaracteristicasIds,
        IReadOnlyCollection<Guid> EmbalagensIds,
        IReadOnlyCollection<Guid> VariacoesIds);

    public class CadastrarProdutoUseCase(
        IProdutoRepository produtoRepository,
        ICategoriaRepository categoriaRepository,
        IProdutoCaracteristicaRepository caracteristicaRepository,
        IProdutoEmbalagemRepository embalagemRepository,
        IProdutoVariacaoRepository variacaoRepository,
        IUnitOfWork unitOfWork,
        ILogger<CadastrarProdutoUseCase> logger,
        IProdutoAlteracaoRepository? alteracaoRepository = null,
        IAssinaturaEmpresaRepository? assinaturaRepository = null)
    {
        public async Task<CadastrarProdutoResult> ExecuteAsync(CadastrarProdutoCommand command)
        {
            logger.LogInformation("Iniciando cadastro de produto. EmpresaId: {EmpresaId}, Nome: {Nome}", command.EmpresaId, command.Nome);

            Validar(command);

            // Limite do plano (upgrade wall): conta produtos existentes contra o
            // teto do plano da assinatura ativa. assinaturaRepository é opcional
            // por compat — se ausente, segue sem cap (não regride v1).
            if (assinaturaRepository is not null)
            {
                var assinatura = await assinaturaRepository.GetAtivaAsync(command.EmpresaId);
                if (assinatura?.Plano is not null && !assinatura.Plano.ProdutosSaoIlimitados)
                {
                    var totalProdutos = await produtoRepository.CountByEmpresaAsync(command.EmpresaId);
                    if (totalProdutos >= assinatura.Plano.LimiteProdutos)
                        throw new EasyStock.Domain.Exceptions.PlanoLimiteAtingidoException("produtos");
                }
            }

            var categoria = await categoriaRepository.GetByIdAsync(command.EmpresaId, command.CategoriaId)
                ?? throw new UseCaseValidationException("Categoria nao encontrada.");

            if (command.SubcategoriaId.HasValue)
            {
                var subcategoria = await categoriaRepository.GetByIdAsync(command.EmpresaId, command.SubcategoriaId.Value)
                    ?? throw new UseCaseValidationException("Subcategoria nao encontrada.");
                if (subcategoria.CategoriaPaiId != command.CategoriaId)
                    throw new UseCaseValidationException("A subcategoria nao pertence a categoria informada.");
            }

            var nomeTrim = command.Nome.Trim();
            if (await produtoRepository.ExistsNomeAsync(command.EmpresaId, nomeTrim))
            {
                throw new UseCaseValidationException($"Já existe um produto cadastrado com o nome \"{nomeTrim}\" nesta empresa.");
            }

            if (!string.IsNullOrWhiteSpace(command.SkuBase))
            {
                var skuBase = command.SkuBase.Trim();
                if (await produtoRepository.ExistsSkuBaseAsync(command.EmpresaId, skuBase) ||
                    await variacaoRepository.ExistsSkuAsync(command.EmpresaId, skuBase))
                {
                    throw new UseCaseValidationException("SKU duplicado para esta empresa.");
                }
            }

            if (!string.IsNullOrWhiteSpace(command.CodigoBarras))
            {
                var codigoBarras = command.CodigoBarras.Trim();
                try { _ = Gtin.Parse(codigoBarras); }
                catch (ArgumentException ex) { throw new UseCaseValidationException(ex.Message); }
                if (await produtoRepository.ExistsCodigoBarrasAsync(command.EmpresaId, codigoBarras))
                {
                    throw new UseCaseValidationException("Código de barras (EAN) duplicado para esta empresa.");
                }
            }

            if (!string.IsNullOrWhiteSpace(command.Nome))
            {
                var nome = command.Nome.Trim();
                if (await produtoRepository.ExistsNomeAsync(command.EmpresaId, nome))
                {
                    throw new UseCaseValidationException("Nome de produto duplicado para esta empresa.");
                }
            }

            foreach (var skuVariacao in (command.Variacoes ?? [])
                         .Select(v => v.Sku?.Trim())
                         .Where(v => !string.IsNullOrWhiteSpace(v))
                         .Cast<string>())
            {
                if (await produtoRepository.ExistsSkuBaseAsync(command.EmpresaId, skuVariacao) ||
                    await variacaoRepository.ExistsSkuAsync(command.EmpresaId, skuVariacao))
                {
                    throw new UseCaseValidationException("SKU duplicado para esta empresa.");
                }
            }

            var agora = DateTime.UtcNow;
            // Gating de status: produto sem preco de venda nasce Inativo (rascunho).
            // Antes nascia sempre Ativo e podia ser exposto na vitrine sem preco, gerando
            // pedidos com R$0 e leitura gerencial errada (auditoria QA 2026-05-16).
            var statusInicial = (command.PrecoReferencia.HasValue && command.PrecoReferencia.Value > 0)
                ? StatusProduto.Ativo
                : StatusProduto.Inativo;
            if (statusInicial == StatusProduto.Inativo)
                logger.LogInformation("Produto {Nome} criado como Inativo (sem preco de venda definido).", command.Nome);

            var produto = new Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = command.EmpresaId,
                CategoriaId = command.CategoriaId,
                SubcategoriaId = command.SubcategoriaId,
                Nome = command.Nome.Trim(),
                DescricaoBase = command.DescricaoBase?.Trim(),
                Marca = command.Marca?.Trim(),
                Tipo = command.Tipo,
                TipoEmbalagem = command.TipoEmbalagem, // C2 (RDC 727/2022)
                SkuBase = string.IsNullOrWhiteSpace(command.SkuBase) ? null : CodigoSku.From(command.SkuBase),
                CodigoBarras = command.CodigoBarras?.Trim(),
                ControlaValidade = command.ControlaValidade,
                Dimensoes = command.Dimensoes.ToValueObjectOrNull(),
                CustoReferencia = command.CustoReferencia.HasValue ? Dinheiro.FromDecimal(command.CustoReferencia.Value) : null,
                PrecoReferencia = command.PrecoReferencia.HasValue ? Dinheiro.FromDecimal(command.PrecoReferencia.Value) : null,
                MargemEstimada = command.MargemEstimada,
                AtributosJson = command.AtributosJson,
                FotosJson = command.FotosJson,
                Status = statusInicial,
                CriadoEm = agora,
                AlteradoEm = agora,
                CriadoPor = command.UsuarioId != Guid.Empty ? command.UsuarioId : null,
                AlteradoPor = command.UsuarioId != Guid.Empty ? command.UsuarioId : null
            };

            var caracteristicasIds = new List<Guid>();
            var embalagensIds      = new List<Guid>();
            var variacoesIds       = new List<Guid>();

            try
            {
                await produtoRepository.InsertAsync(produto);

                foreach (var caracteristica in command.Caracteristicas ?? [])
                {
                    var entity = new ProdutoCaracteristica
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = produto.Id,
                        Nome = caracteristica.Nome.Trim(),
                        Descricao = caracteristica.Descricao?.Trim(),
                        QuantidadeReferencia = caracteristica.QuantidadeReferencia,
                        VariacaoPadrao = caracteristica.VariacaoPadrao?.Trim(),
                        VariacaoId = caracteristica.VariacaoId,
                        OrdemExibicao = caracteristica.OrdemExibicao,
                        CriadoEm = agora,
                        AlteradoEm = agora
                    };

                    await caracteristicaRepository.InsertAsync(entity);
                    caracteristicasIds.Add(entity.Id);
                }

                foreach (var embalagem in command.Embalagens ?? [])
                {
                    var entity = new ProdutoEmbalagem
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = produto.Id,
                        Nome = embalagem.Nome.Trim(),
                        Descricao = embalagem.Descricao?.Trim(),
                        Dimensoes = embalagem.Dimensoes.ToValueObjectOrNull(),
                        Padrao = embalagem.Padrao,
                        CriadoEm = agora,
                        AlteradoEm = agora
                    };

                    await embalagemRepository.InsertAsync(entity);
                    embalagensIds.Add(entity.Id);
                }

                foreach (var variacao in command.Variacoes ?? [])
                {
                    var entity = new ProdutoVariacao
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = produto.Id,
                        Nome = variacao.Nome.Trim(),
                        Cor = variacao.Cor?.Trim(),
                        Tamanho = variacao.Tamanho?.Trim(),
                        DescricaoComercial = variacao.DescricaoComercial?.Trim(),
                        Sku = string.IsNullOrWhiteSpace(variacao.Sku) ? null : CodigoSku.From(variacao.Sku),
                        CodigoBarras = variacao.CodigoBarras?.Trim(),
                        AtributosJson = variacao.AtributosJson,
                        DimensoesPadrao = variacao.DimensoesPadrao.ToValueObjectOrNull(),
                        Ativa = variacao.Ativa,
                        CriadoEm = agora,
                        AlteradoEm = agora
                    };

                    await variacaoRepository.InsertAsync(entity);
                    variacoesIds.Add(entity.Id);
                }

                if (alteracaoRepository is not null && command.UsuarioId != Guid.Empty)
                {
                    await alteracaoRepository.AddAsync(new ProdutoAlteracao
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = produto.Id,
                        UsuarioId = command.UsuarioId,
                        Acao = "cadastrado",
                        AlteradoEm = DateTime.UtcNow
                    });
                }

                await unitOfWork.CommitAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Erro ao cadastrar produto. EmpresaId: {EmpresaId}, Nome: {Nome}",
                    command.EmpresaId, command.Nome);
                throw;
            }

            logger.LogInformation("Produto cadastrado com sucesso. ProdutoId: {ProdutoId}, EmpresaId: {EmpresaId}", produto.Id, command.EmpresaId);

            return new CadastrarProdutoResult(produto.Id, caracteristicasIds, embalagensIds, variacoesIds);
        }

        private static void Validar(CadastrarProdutoCommand command)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
            UseCaseGuards.EnsureNotEmpty(command.CategoriaId, "CategoriaId");
            if (string.IsNullOrWhiteSpace(command.Nome)) throw new UseCaseValidationException("Nome do produto é obrigatório.");
            if ((command.Embalagens ?? []).Count(e => e.Padrao) > 1) throw new UseCaseValidationException("Somente uma embalagem pode ser marcada como padrao.");

            var skus = (command.Variacoes ?? [])
                .Select(v => v.Sku?.Trim())
                .Where(sku => !string.IsNullOrWhiteSpace(sku))
                .Select(sku => sku!.ToUpperInvariant())
                .ToArray();

            if (skus.Length != skus.Distinct().Count())
                throw new UseCaseValidationException("Nao e permitido cadastrar variacoes com o mesmo SKU.");

            if (!string.IsNullOrWhiteSpace(command.SkuBase) &&
                skus.Contains(command.SkuBase.Trim().ToUpperInvariant()))
            {
                throw new UseCaseValidationException("SKU base do produto nao pode ser igual ao SKU de uma variacao.");
            }
        }
    }
}
