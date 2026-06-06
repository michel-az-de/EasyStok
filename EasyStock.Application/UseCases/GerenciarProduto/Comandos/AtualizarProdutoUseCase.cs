using System.Text.Json;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.GerenciarProduto.Comandos;

/// <summary>
/// Comando: atualiza um produto existente. Orquestra mudancas em:
/// - Campos basicos (nome, marca, tipo, dimensoes, custo, preco, margem, status, observacao)
/// - Categoria e subcategoria (com validacao de hierarquia)
/// - SKU base e codigo de barras (com check de duplicidade)
/// - Caracteristicas, embalagens e variacoes (delete-and-recreate batch)
/// - Auditoria de alteracoes (ProdutoAlteracao com mudancas em JSON)
/// - Invalidacao de cache de relacionadas
///
/// Inclui gating: produto so pode ficar Ativo se tiver preco de venda > 0
/// (auditoria QA 2026-05-16 — produto Ativo sem preco virava vendido como R$0).
///
/// Extraido do god-UseCase <c>GerenciarProdutoUseCase</c> (F9b — o maior dos 4).
/// O facade continua expondo <c>AtualizarAsync</c> via delegacao, preservando
/// contrato publico (R8).
/// </summary>
public sealed class AtualizarProdutoUseCase(
    IProdutoRepository produtoRepository,
    ICategoriaRepository categoriaRepository,
    IProdutoVariacaoRepository produtoVariacaoRepository,
    IProdutoCaracteristicaRepository caracteristicaRepository,
    IProdutoEmbalagemRepository embalagemRepository,
    IUnitOfWork unitOfWork,
    ICacheService? cacheService = null,
    IProdutoAlteracaoRepository? alteracaoRepository = null)
{
    public async Task ExecuteAsync(AtualizarProdutoCommand command)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(command.ProdutoId, "ProdutoId");
        if (command.CategoriaId == Guid.Empty) throw new UseCaseValidationException("CategoriaId é obrigatório.");
        if (string.IsNullOrWhiteSpace(command.Nome)) throw new UseCaseValidationException("Nome do produto é obrigatório.");
        UseCaseGuards.EnsureSemTagsHtml(command.Nome, "Nome do produto");

        var produto = await produtoRepository.GetByIdAsync(command.EmpresaId, command.ProdutoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        // Captura estado anterior para auditoria
        var nomeAntes = produto.Nome;
        var marcaAntes = produto.Marca;
        var statusAntes = produto.Status;
        var precoAntes = produto.PrecoReferencia?.Valor;
        var custoAntes = produto.CustoReferencia?.Valor;
        var margemAntes = produto.MargemEstimada;
        var descricaoAntes = produto.DescricaoBase;
        var skuAntes = produto.SkuBase?.Value;
        var codigoBarrasAntes = produto.CodigoBarras;
        var observacaoAntes = produto.ObservacaoInterna;

        var categoria = await categoriaRepository.GetByIdAsync(command.EmpresaId, command.CategoriaId)
            ?? throw new UseCaseValidationException("Categoria nao encontrada.");

        if (command.SubcategoriaId.HasValue)
        {
            var subcategoria = await categoriaRepository.GetByIdAsync(command.EmpresaId, command.SubcategoriaId.Value)
                ?? throw new UseCaseValidationException("Subcategoria nao encontrada.");
            if (subcategoria.CategoriaPaiId != command.CategoriaId)
                throw new UseCaseValidationException("A subcategoria nao pertence a categoria informada.");
        }

        if (!string.IsNullOrWhiteSpace(command.SkuBase))
        {
            var skuBase = command.SkuBase.Trim();
            if (await produtoRepository.ExistsSkuBaseAsync(command.EmpresaId, skuBase, command.ProdutoId) ||
                await produtoVariacaoRepository.ExistsSkuAsync(command.EmpresaId, skuBase))
            {
                throw new UseCaseValidationException("SKU duplicado para esta empresa.");
            }

            produto.SkuBase = CodigoSku.From(skuBase);
        }
        else
        {
            produto.SkuBase = null;
        }

        if (!string.IsNullOrWhiteSpace(command.CodigoBarras))
        {
            var codigoBarras = command.CodigoBarras.Trim();
            try { _ = Gtin.Parse(codigoBarras); }
            catch (ArgumentException ex) { throw new UseCaseValidationException(ex.Message); }
            if (await produtoRepository.ExistsCodigoBarrasAsync(command.EmpresaId, codigoBarras, command.ProdutoId))
            {
                throw new UseCaseValidationException("Código de barras (EAN) duplicado para esta empresa.");
            }
        }

        if (!string.IsNullOrWhiteSpace(command.Nome))
        {
            var nome = command.Nome.Trim();
            if (await produtoRepository.ExistsNomeAsync(command.EmpresaId, nome, command.ProdutoId))
            {
                throw new UseCaseValidationException("Nome de produto duplicado para esta empresa.");
            }
        }

        produto.CategoriaId = command.CategoriaId;
        produto.SubcategoriaId = command.SubcategoriaId;
        produto.Nome = command.Nome.Trim();
        produto.DescricaoBase = Normalizar(command.DescricaoBase);
        produto.Marca = Normalizar(command.Marca);
        produto.Tipo = command.Tipo;
        produto.TipoEmbalagem = command.TipoEmbalagem; // C2 (RDC 727/2022)
        produto.CodigoBarras = Normalizar(command.CodigoBarras);
        produto.ControlaValidade = command.ControlaValidade;
        produto.Dimensoes = command.Dimensoes.ToValueObjectOrNull();
        produto.CustoReferencia = command.CustoReferencia.HasValue ? Dinheiro.FromDecimal(command.CustoReferencia.Value) : null;
        produto.PrecoReferencia = command.PrecoReferencia.HasValue ? Dinheiro.FromDecimal(command.PrecoReferencia.Value) : null;
        produto.MargemEstimada = command.MargemEstimada;
        // Preserva ficha tecnica quando o command omite AtributosJson. Form.cshtml de
        // produtos nao carrega/devolve esse campo — sem este guard, qualquer edicao via
        // Form zerava a ficha cadastrada via PUT /api/produtos/{id}/ficha-tecnica.
        if (command.AtributosJson != null)
            produto.AtributosJson = command.AtributosJson;
        // Gating: nao deixa Ativo sem preco de venda. Antes o usuario podia salvar
        // produto Ativo sem preco e ele aparecia na vitrine como "Definir preco →"
        // mas vendavel — gerava pedido com R$0 (auditoria QA 2026-05-16).
        var precoValido = command.PrecoReferencia.HasValue && command.PrecoReferencia.Value > 0;
        produto.Status = (command.Status == StatusProduto.Ativo && !precoValido)
            ? StatusProduto.Inativo
            : command.Status;
        produto.ObservacaoInterna = command.ObservacaoInterna;
        produto.AlteradoPor = command.UsuarioId != Guid.Empty ? command.UsuarioId : null;
        produto.AlteradoEm = DateTime.UtcNow;

        try
        {
            await produtoRepository.UpdateAsync(produto);

            // Replace caracteristicas — batch delete + re-insert
            if (command.Caracteristicas is not null)
            {
                await caracteristicaRepository.DeleteByProdutoAsync(command.EmpresaId, command.ProdutoId);

                var agora = DateTime.UtcNow;
                foreach (var input in command.Caracteristicas)
                {
                    await caracteristicaRepository.InsertAsync(new ProdutoCaracteristica
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = command.ProdutoId,
                        Nome = input.Nome.Trim(),
                        Descricao = input.Descricao?.Trim(),
                        QuantidadeReferencia = input.QuantidadeReferencia,
                        VariacaoPadrao = input.VariacaoPadrao?.Trim(),
                        VariacaoId = input.VariacaoId,
                        OrdemExibicao = input.OrdemExibicao,
                        CriadoEm = agora,
                        AlteradoEm = agora
                    });
                }
            }

            // Replace embalagens — batch delete + re-insert
            if (command.Embalagens is not null)
            {
                if (command.Embalagens.Count(e => e.Padrao) > 1)
                    throw new UseCaseValidationException("Somente uma embalagem pode ser marcada como padrao.");

                await embalagemRepository.DeleteByProdutoAsync(command.EmpresaId, command.ProdutoId);

                var agora = DateTime.UtcNow;
                foreach (var input in command.Embalagens)
                {
                    await embalagemRepository.InsertAsync(new ProdutoEmbalagem
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = command.ProdutoId,
                        Nome = input.Nome.Trim(),
                        Descricao = input.Descricao?.Trim(),
                        Dimensoes = input.Dimensoes.ToValueObjectOrNull(),
                        Padrao = input.Padrao,
                        CriadoEm = agora,
                        AlteradoEm = agora
                    });
                }
            }

            // Replace variacoes — batch delete + re-insert
            if (command.Variacoes is not null)
            {
                await produtoVariacaoRepository.DeleteByProdutoAsync(command.EmpresaId, command.ProdutoId);

                var agora = DateTime.UtcNow;
                foreach (var variacao in command.Variacoes)
                {
                    await produtoVariacaoRepository.InsertAsync(new ProdutoVariacao
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = command.ProdutoId,
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
                    });
                }
            }

            await unitOfWork.CommitAsync();

            if (alteracaoRepository is not null && command.UsuarioId != Guid.Empty)
            {
                var mudancas = new List<object>();
                if (produto.Nome != nomeAntes)
                    mudancas.Add(new { campo = "Nome", de = nomeAntes, para = produto.Nome });
                if (produto.Marca != marcaAntes)
                    mudancas.Add(new { campo = "Marca", de = marcaAntes, para = produto.Marca });
                if (produto.Status != statusAntes)
                    mudancas.Add(new { campo = "Status", de = statusAntes.ToString(), para = produto.Status.ToString() });
                if (produto.PrecoReferencia?.Valor != precoAntes)
                    mudancas.Add(new { campo = "Preço", de = precoAntes, para = produto.PrecoReferencia?.Valor });
                if (produto.CustoReferencia?.Valor != custoAntes)
                    mudancas.Add(new { campo = "Custo", de = custoAntes, para = produto.CustoReferencia?.Valor });
                if (produto.MargemEstimada != margemAntes)
                    mudancas.Add(new { campo = "Margem", de = margemAntes, para = produto.MargemEstimada });
                if (produto.DescricaoBase != descricaoAntes)
                    mudancas.Add(new { campo = "Descrição", de = descricaoAntes, para = produto.DescricaoBase });
                if (produto.SkuBase?.Value != skuAntes)
                    mudancas.Add(new { campo = "SKU", de = skuAntes, para = produto.SkuBase?.Value });
                if (produto.CodigoBarras != codigoBarrasAntes)
                    mudancas.Add(new { campo = "Código de barras", de = codigoBarrasAntes, para = produto.CodigoBarras });
                if (produto.ObservacaoInterna != observacaoAntes)
                    mudancas.Add(new { campo = "Observação interna", de = observacaoAntes, para = produto.ObservacaoInterna });

                if (mudancas.Count > 0)
                {
                    await alteracaoRepository.AddAsync(new ProdutoAlteracao
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = command.EmpresaId,
                        ProdutoId = command.ProdutoId,
                        UsuarioId = command.UsuarioId,
                        Acao = "atualizado",
                        AlteracoesJson = JsonSerializer.Serialize(mudancas),
                        Motivo = string.IsNullOrWhiteSpace(command.Motivo) ? null : command.Motivo.Trim(),
                        Observacao = string.IsNullOrWhiteSpace(command.Observacao) ? null : command.Observacao.Trim(),
                        AlteradoEm = DateTime.UtcNow
                    });
                    await unitOfWork.CommitAsync();
                }
            }
        }
        catch (UseCaseValidationException)
        {
            throw; // deixa subir sem logar como erro inesperado
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Erro ao atualizar produto {command.ProdutoId}: {ex.Message}", ex);
        }

        if (cacheService is not null)
            await cacheService.RemoveAsync(CacheKeys.ProdutoRelacionadas(command.EmpresaId, command.ProdutoId));
    }

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
