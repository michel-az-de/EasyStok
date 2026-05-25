using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

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
    ILogger<CriarContaPagarUseCase> logger)
{
    public async Task<ContaPagarResult> ExecuteAsync(CriarContaPagarCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.CategoriaFinanceiraId, nameof(cmd.CategoriaFinanceiraId));
        if (cmd.Parcelas is null || cmd.Parcelas.Count == 0)
            throw new UseCaseValidationException("Pelo menos uma parcela e obrigatoria.");

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

        // Idempotencia: se DocumentoReferencia ja existe, retorna o existente
        if (!string.IsNullOrWhiteSpace(cmd.DocumentoReferencia))
        {
            var existente = await repo.GetByDocumentoReferenciaAsync(cmd.EmpresaId, cmd.DocumentoReferencia, ct);
            if (existente is not null) return ContaPagarResult.De(existente);
        }
        // Idempotencia: se Origem+OrigemRefId ja existe, retorna
        if (cmd.OrigemRefId.HasValue && cmd.Origem != OrigemContaFinanceira.Manual)
        {
            var existente = await repo.GetByOrigemAsync(cmd.EmpresaId, cmd.Origem, cmd.OrigemRefId.Value, ct);
            if (existente is not null) return ContaPagarResult.De(existente);
        }

        try
        {
            var conta = ContaPagar.Criar(
                cmd.EmpresaId, cmd.FornecedorId, cmd.CategoriaFinanceiraId,
                cmd.Descricao, DataUtc.ParaUtc(cmd.DataEmissao),
                cmd.CentroCustoId, cmd.LojaId,
                cmd.Origem, cmd.OrigemRefId, cmd.DocumentoReferencia,
                DataUtc.ParaUtcOpcional(cmd.DataCompetencia), cmd.Observacoes);

            foreach (var p in cmd.Parcelas.OrderBy(x => x.Numero))
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
