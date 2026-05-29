using System.Text.Json;

namespace EasyStock.Application.UseCases.GerenciarComposicao;

/// <summary>
/// CRUD da receita (composicao produto-final -> insumos). Replace-all transacional:
/// deleta linhas existentes do escopo (produtoFinal, lojaId) e insere as novas dentro
/// da mesma transacao. Valida ciclo, tenant do insumo, precisao decimal minima e
/// grava ProdutoComposicaoAlteracao com diff (added/removed/updated).
/// </summary>
public class GerenciarComposicaoUseCase(
    IProdutoRepository produtoRepository,
    IProdutoComposicaoRepository composicaoRepository,
    IProdutoComposicaoAlteracaoRepository alteracaoRepository,
    ILojaRepository lojaRepository,
    IUnitOfWork unitOfWork,
    ILogger<GerenciarComposicaoUseCase> logger)
{
    private const decimal PrecisaoMinima = 0.0001m;

    public async Task<ComposicaoResult?> ObterAsync(ObterComposicaoQuery query, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(query.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(query.ProdutoFinalId, "ProdutoFinalId");

        var produto = await produtoRepository.GetByIdAsync(query.EmpresaId, query.ProdutoFinalId)
            ?? throw new UseCaseValidationException("PRODUTO_NOT_FOUND", "Produto-final nao encontrado.");

        var linhas = await composicaoRepository.GetByProdutoFinalAsync(
            query.EmpresaId, query.ProdutoFinalId, query.LojaId, ct);

        return new ComposicaoResult(
            ProdutoFinalId: produto.Id,
            ProdutoFinalNome: produto.Nome,
            LojaId: query.LojaId,
            RendimentoBase: produto.RendimentoBase,
            RendimentoUnidade: produto.RendimentoUnidade,
            UnidadeMedidaBase: produto.UnidadeMedidaBase,
            Linhas: linhas.Select(c => new ComposicaoLinhaResult(
                InsumoId: c.InsumoId,
                InsumoNome: c.Insumo?.Nome ?? "",
                Quantidade: c.Quantidade,
                Unidade: c.Unidade,
                Observacao: c.Observacao,
                OrdemExibicao: c.OrdemExibicao)).ToList());
    }

    public async Task SubstituirAsync(SubstituirComposicaoCommand command, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(command.ProdutoFinalId, "ProdutoFinalId");
        UseCaseGuards.EnsureNotEmpty(command.UsuarioId, "UsuarioId");

        if (command.RendimentoBase <= 0)
            throw new UseCaseValidationException("INVALID_RENDIMENTO", "Rendimento deve ser maior que zero.");

        // Valida produto-final pertence a empresa
        var produto = await produtoRepository.GetByIdAsync(command.EmpresaId, command.ProdutoFinalId)
            ?? throw new UseCaseValidationException("PRODUTO_NOT_FOUND", "Produto-final nao encontrado.");

        if (produto.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("CROSS_TENANT", "Produto-final pertence a outra empresa.");

        // Loja se informada precisa pertencer a empresa
        if (command.LojaId.HasValue)
        {
            var loja = await lojaRepository.GetByIdAsync(command.EmpresaId, command.LojaId.Value);
            if (loja == null)
                throw new UseCaseValidationException("LOJA_NOT_FOUND", "Loja nao encontrada nesta empresa.");
        }

        // Valida linhas: sem duplicado, sem qty abaixo da precisao, insumo != produto-final
        var insumosVistos = new HashSet<Guid>();
        foreach (var linha in command.Linhas)
        {
            UseCaseGuards.EnsureNotEmpty(linha.InsumoId, "InsumoId");
            if (linha.Quantidade < PrecisaoMinima)
                throw new UseCaseValidationException("INVALID_QUANTITY", $"Quantidade minima e {PrecisaoMinima}.");

            if (linha.InsumoId == command.ProdutoFinalId)
                throw new UseCaseValidationException("CYCLE_DETECTED",
                    "Produto nao pode ser insumo de si mesmo.",
                    new { cicloDetectado = new[] { produto.Nome } });

            if (!insumosVistos.Add(linha.InsumoId))
                throw new UseCaseValidationException("DUPLICATE_INSUMO",
                    $"Insumo {linha.InsumoId} duplicado na receita.");

            var insumo = await produtoRepository.GetByIdAsync(command.EmpresaId, linha.InsumoId);
            if (insumo == null || insumo.EmpresaId != command.EmpresaId)
                throw new UseCaseValidationException("CROSS_TENANT_INSUMO",
                    $"Insumo {linha.InsumoId} nao pertence a esta empresa.");
        }

        // Carrega estado anterior pra diff
        var linhasAntes = await composicaoRepository.GetByProdutoFinalAsync(
            command.EmpresaId, command.ProdutoFinalId, command.LojaId, ct);

        // Replace-all transacional
        await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // Ciclo: pra cada nova linha, verifica se adicionar (final, insumo) criaria ciclo
            foreach (var linha in command.Linhas)
            {
                if (await composicaoRepository.ExisteCicloAsync(
                    command.EmpresaId, command.ProdutoFinalId, linha.InsumoId, innerCt))
                {
                    throw new UseCaseValidationException("CYCLE_DETECTED",
                        "Receita criaria ciclo: o insumo escolhido depende deste produto direta ou indiretamente.",
                        new { produtoFinalId = command.ProdutoFinalId, insumoId = linha.InsumoId });
                }
            }

            await composicaoRepository.DeleteByProdutoFinalAsync(
                command.EmpresaId, command.ProdutoFinalId, command.LojaId, innerCt);

            var agora = DateTime.UtcNow;
            foreach (var linha in command.Linhas)
            {
                await composicaoRepository.InsertAsync(new ProdutoComposicao
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = command.EmpresaId,
                    ProdutoFinalId = command.ProdutoFinalId,
                    InsumoId = linha.InsumoId,
                    LojaId = command.LojaId,
                    Quantidade = linha.Quantidade,
                    Unidade = linha.Unidade,
                    Observacao = linha.Observacao,
                    OrdemExibicao = linha.OrdemExibicao,
                    CriadoEm = agora,
                    AlteradoEm = agora,
                    CriadoPor = command.UsuarioId,
                    AlteradoPor = command.UsuarioId
                }, innerCt);
            }

            // Atualiza rendimento + unidade no Produto (carrega novamente pra ter xmin atual)
            produto.RendimentoBase = command.RendimentoBase;
            produto.RendimentoUnidade = command.RendimentoUnidade;
            produto.UnidadeMedidaBase = command.UnidadeMedidaBaseProdutoFinal;
            produto.AlteradoEm = agora;
            produto.AlteradoPor = command.UsuarioId;
            await produtoRepository.UpdateAsync(produto);

            // Audit diff
            var diff = BuildDiff(linhasAntes, command.Linhas);
            var acao = linhasAntes.Count == 0 ? "criada" : (command.Linhas.Count == 0 ? "removida" : "atualizada");

            await alteracaoRepository.AddAsync(new ProdutoComposicaoAlteracao
            {
                Id = Guid.NewGuid(),
                EmpresaId = command.EmpresaId,
                ProdutoFinalId = command.ProdutoFinalId,
                LojaId = command.LojaId,
                UsuarioId = command.UsuarioId,
                Acao = acao,
                AlteracoesJson = JsonSerializer.Serialize(diff),
                Observacao = command.Observacao,
                AlteradoEm = agora
            }, innerCt);

            await unitOfWork.CommitAsync();
        }, ct);

        logger.LogInformation(
            "Composicao substituida: produto {ProdutoId} empresa {EmpresaId} loja {LojaId} usuario {UsuarioId} {AntesCount}->{DepoisCount} linhas",
            command.ProdutoFinalId, command.EmpresaId, command.LojaId, command.UsuarioId, linhasAntes.Count, command.Linhas.Count);
    }

    private static object BuildDiff(
        IReadOnlyCollection<ProdutoComposicao> antes,
        IReadOnlyList<ComposicaoLinhaInput> depois)
    {
        var antesPorInsumo = antes.ToDictionary(c => c.InsumoId);
        var depoisPorInsumo = depois.ToDictionary(c => c.InsumoId);

        var added = depois
            .Where(d => !antesPorInsumo.ContainsKey(d.InsumoId))
            .Select(d => new { d.InsumoId, d.Quantidade, unidade = d.Unidade.ToString() })
            .ToList();

        var removed = antes
            .Where(a => !depoisPorInsumo.ContainsKey(a.InsumoId))
            .Select(a => new { a.InsumoId, a.Quantidade, unidade = a.Unidade.ToString() })
            .ToList();

        var updated = new List<object>();
        foreach (var d in depois)
        {
            if (antesPorInsumo.TryGetValue(d.InsumoId, out var a))
            {
                if (a.Quantidade != d.Quantidade)
                    updated.Add(new { insumoId = d.InsumoId, campo = "Quantidade", antes = a.Quantidade, depois = d.Quantidade });
                if (a.Unidade != d.Unidade)
                    updated.Add(new { insumoId = d.InsumoId, campo = "Unidade", antes = a.Unidade.ToString(), depois = d.Unidade.ToString() });
            }
        }

        return new { added, removed, updated };
    }
}
