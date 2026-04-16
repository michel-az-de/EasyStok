using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

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
        Guid UsuarioId = default);

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
        IProdutoAlteracaoRepository? alteracaoRepository = null)
    {
        public async Task<CadastrarProdutoResult> ExecuteAsync(CadastrarProdutoCommand command)
        {
            logger.LogInformation("Iniciando cadastro de produto. EmpresaId: {EmpresaId}, Nome: {Nome}", command.EmpresaId, command.Nome);

            Validar(command);

            var categoria = await categoriaRepository.GetByIdAsync(command.CategoriaId)
                ?? throw new UseCaseValidationException("Categoria nao encontrada.");

            if (categoria.EmpresaId != command.EmpresaId)
                throw new UseCaseValidationException("A categoria informada nao pertence a empresa.");

            if (command.SubcategoriaId.HasValue)
            {
                var subcategoria = await categoriaRepository.GetByIdAsync(command.SubcategoriaId.Value)
                    ?? throw new UseCaseValidationException("Subcategoria nao encontrada.");
                if (subcategoria.EmpresaId != command.EmpresaId)
                    throw new UseCaseValidationException("A subcategoria informada nao pertence a empresa.");
                if (subcategoria.CategoriaPaiId != command.CategoriaId)
                    throw new UseCaseValidationException("A subcategoria nao pertence a categoria informada.");
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
                SkuBase = string.IsNullOrWhiteSpace(command.SkuBase) ? null : CodigoSku.From(command.SkuBase),
                CodigoBarras = command.CodigoBarras?.Trim(),
                ControlaValidade = command.ControlaValidade,
                Dimensoes = command.Dimensoes.ToValueObjectOrNull(),
                CustoReferencia = command.CustoReferencia.HasValue ? Dinheiro.FromDecimal(command.CustoReferencia.Value) : null,
                PrecoReferencia = command.PrecoReferencia.HasValue ? Dinheiro.FromDecimal(command.PrecoReferencia.Value) : null,
                MargemEstimada = command.MargemEstimada,
                AtributosJson = command.AtributosJson,
                FotosJson = command.FotosJson,
                Status = StatusProduto.Ativo,
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
            if (command.EmpresaId == Guid.Empty) throw new UseCaseValidationException("EmpresaId é obrigatório.");
            if (command.CategoriaId == Guid.Empty) throw new UseCaseValidationException("CategoriaId é obrigatório.");
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
