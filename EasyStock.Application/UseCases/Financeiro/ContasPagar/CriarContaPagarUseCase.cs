using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.ContasPagar;

public sealed record CriarContaPagarCommand(
    Guid EmpresaId,
    Guid? FornecedorId,
    Guid CategoriaFinanceiraId,
    string Descricao,
    DateTime DataEmissao,
    IReadOnlyList<ParcelaSpec> Parcelas,
    Guid? CentroCustoId = null,
    Guid? LojaId = null,
    DateTime? DataCompetencia = null,
    string? Observacoes = null,
    OrigemContaFinanceira Origem = OrigemContaFinanceira.Manual,
    Guid? OrigemRefId = null,
    string? DocumentoReferencia = null,
    bool EmitirAposCriar = false);

public class CriarContaPagarUseCase(
    IContaPagarRepository repo,
    ICategoriaFinanceiraRepository categoriaRepo,
    ICentroCustoRepository centroRepo,
    IUnitOfWork uow,
    ILogger<CriarContaPagarUseCase> logger,
    IFornecedorRepository? fornecedores = null,
    ILojaRepository? lojas = null)
{
    public async Task<ContaPagarResult> ExecuteAsync(CriarContaPagarCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.CategoriaFinanceiraId, nameof(cmd.CategoriaFinanceiraId));
        if (cmd.Parcelas is null || cmd.Parcelas.Count == 0)
            throw new UseCaseValidationException("Pelo menos uma parcela é obrigatória.");
        // #2095 (F7): tamanho e faixa antes do banco (estourar a coluna respondia 500).
        ValidacaoFinanceira.TextosDaConta(cmd.Descricao, cmd.Observacoes, cmd.DocumentoReferencia);
        var parcelas = cmd.Parcelas
            .Select(p => p with { Valor = ValidacaoFinanceira.Parcela(p.Valor, p.MetodoPlanejado) })
            .ToList();

        // Valida categoria pertence ao tenant + ativa + tipo Despesa/Ambas
        var categoria = await categoriaRepo.GetByIdAsync(cmd.EmpresaId, cmd.CategoriaFinanceiraId, ct)
            ?? throw new UseCaseValidationException("Categoria financeira nao encontrada.");
        if (!categoria.Ativa)
            throw new UseCaseValidationException("Categoria financeira esta inativa.");
        if (categoria.Tipo == TipoCategoriaFinanceira.Receita)
            throw new UseCaseValidationException("Categoria de receita nao pode ser usada em conta a pagar.");

        if (cmd.CentroCustoId.HasValue)
        {
            var centro = await centroRepo.GetByIdAsync(cmd.EmpresaId, cmd.CentroCustoId.Value, ct);
            if (centro is null || !centro.Ativo)
                throw new UseCaseValidationException("Centro de custo invalido ou inativo.");
        }

        await ValidacaoFinanceira.GarantirReferenciasAsync(cmd.EmpresaId,
            fornecedorId: cmd.FornecedorId, fornecedores: fornecedores, lojaId: cmd.LojaId, lojas: lojas);

        // Idempotencia das integracoes: Origem+OrigemRefId ja existe, devolve a conta.
        if (cmd.OrigemRefId.HasValue && cmd.Origem != OrigemContaFinanceira.Manual)
        {
            var existente = await repo.GetByOrigemAsync(cmd.EmpresaId, cmd.Origem, cmd.OrigemRefId.Value, ct);
            if (existente is not null) return ContaPagarResult.De(existente);
        }
        // #2095 (F6): documento repetido nao e idempotencia, e outra divida (ou erro de digitacao): 409, nunca a
        // conta antiga em silencio. O indice unico (EmpresaId, DocumentoReferencia) continua; por fornecedor e migracao.
        if (!string.IsNullOrWhiteSpace(cmd.DocumentoReferencia))
        {
            var documento = cmd.DocumentoReferencia.Trim();
            var existente = await repo.GetByDocumentoReferenciaAsync(cmd.EmpresaId, documento, ct);
            if (existente is not null) throw new ContaFinanceiraDuplicadaException(documento, existente.Id);
        }

        try
        {
            var conta = ContaPagar.Criar(
                cmd.EmpresaId, cmd.FornecedorId, cmd.CategoriaFinanceiraId,
                cmd.Descricao, DataUtc.ParaUtc(cmd.DataEmissao),
                cmd.CentroCustoId, cmd.LojaId,
                cmd.Origem, cmd.OrigemRefId, cmd.DocumentoReferencia,
                DataUtc.ParaUtcOpcional(cmd.DataCompetencia), cmd.Observacoes);

            foreach (var p in parcelas.OrderBy(x => x.Numero))
                conta.AdicionarParcela(p.Numero, p.Valor, DataUtc.ParaUtc(p.DataVencimento), p.MetodoPlanejado);

            if (cmd.EmitirAposCriar) conta.Emitir();

            await repo.AddAsync(conta, ct);
            await repo.AddEventoAsync(ContaFinanceiraEvento.ParaContaPagar(
                conta.EmpresaId, conta.Id,
                cmd.EmitirAposCriar ? TipoEventoContaFinanceira.Emitida : TipoEventoContaFinanceira.Criada,
                descricao: $"Conta criada com {conta.Parcelas.Count} parcela(s) totalizando {conta.ValorTotal:F2}",
                origem: "api"), ct);
            await uow.CommitAsync();

            logger.LogInformation("ContaPagar {Id} criada para empresa {Empresa} ({Parcelas} parcelas, total={Total})",
                conta.Id, conta.EmpresaId, conta.Parcelas.Count, conta.ValorTotal);
            return ContaPagarResult.De(conta);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}
