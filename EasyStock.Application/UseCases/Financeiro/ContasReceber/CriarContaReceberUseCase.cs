using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Financeiro.ContasReceber;

public sealed record CriarContaReceberCommand(
    Guid EmpresaId,
    Guid? ClienteId,
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

public class CriarContaReceberUseCase(
    IContaReceberRepository repo,
    ICategoriaFinanceiraRepository categoriaRepo,
    ICentroCustoRepository centroRepo,
    IUnitOfWork uow,
    ILogger<CriarContaReceberUseCase> logger)
{
    public async Task<ContaReceberResult> ExecuteAsync(CriarContaReceberCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.CategoriaFinanceiraId, nameof(cmd.CategoriaFinanceiraId));
        if (cmd.Parcelas is null || cmd.Parcelas.Count == 0)
            throw new UseCaseValidationException("Pelo menos uma parcela e obrigatoria.");

        var categoria = await categoriaRepo.GetByIdAsync(cmd.EmpresaId, cmd.CategoriaFinanceiraId, ct)
            ?? throw new UseCaseValidationException("Categoria financeira nao encontrada.");
        if (!categoria.Ativa)
            throw new UseCaseValidationException("Categoria financeira esta inativa.");
        if (categoria.Tipo == TipoCategoriaFinanceira.Despesa)
            throw new UseCaseValidationException("Categoria de despesa nao pode ser usada em conta a receber.");

        if (cmd.CentroCustoId.HasValue)
        {
            var centro = await centroRepo.GetByIdAsync(cmd.EmpresaId, cmd.CentroCustoId.Value, ct);
            if (centro is null || !centro.Ativo)
                throw new UseCaseValidationException("Centro de custo invalido ou inativo.");
        }

        if (!string.IsNullOrWhiteSpace(cmd.DocumentoReferencia))
        {
            var existente = await repo.GetByDocumentoReferenciaAsync(cmd.EmpresaId, cmd.DocumentoReferencia, ct);
            if (existente is not null) return ContaReceberResult.De(existente);
        }
        if (cmd.OrigemRefId.HasValue && cmd.Origem != OrigemContaFinanceira.Manual)
        {
            var existente = await repo.GetByOrigemAsync(cmd.EmpresaId, cmd.Origem, cmd.OrigemRefId.Value, ct);
            if (existente is not null) return ContaReceberResult.De(existente);
        }

        try
        {
            var conta = ContaReceber.Criar(
                cmd.EmpresaId, cmd.ClienteId, cmd.CategoriaFinanceiraId,
                cmd.Descricao, cmd.DataEmissao,
                cmd.CentroCustoId, cmd.LojaId,
                cmd.Origem, cmd.OrigemRefId, cmd.DocumentoReferencia,
                cmd.DataCompetencia, cmd.Observacoes);

            foreach (var p in cmd.Parcelas.OrderBy(x => x.Numero))
                conta.AdicionarParcela(p.Numero, p.Valor, p.DataVencimento, p.MetodoPlanejado);

            if (cmd.EmitirAposCriar) conta.Emitir();

            await repo.AddAsync(conta, ct);
            await repo.AddEventoAsync(ContaFinanceiraEvento.ParaContaReceber(
                conta.EmpresaId, conta.Id,
                cmd.EmitirAposCriar ? TipoEventoContaFinanceira.Emitida : TipoEventoContaFinanceira.Criada,
                descricao: $"Conta criada com {conta.Parcelas.Count} parcela(s) totalizando {conta.ValorTotal:F2}",
                origem: "api"), ct);
            await uow.CommitAsync();

            logger.LogInformation("ContaReceber {Id} criada para empresa {Empresa} ({Parcelas} parcelas, total={Total})",
                conta.Id, conta.EmpresaId, conta.Parcelas.Count, conta.ValorTotal);
            return ContaReceberResult.De(conta);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}
