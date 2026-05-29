using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Application.UseCases.Financeiro.ContasPagar;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.Integracao;

/// <summary>
/// Gera ContaPagar automaticamente a partir de PedidoFornecedor recebido.
/// Idempotente via (EmpresaId, Origem=PedidoFornecedor, OrigemRefId=PedidoId).
/// Flag opt-in: <see cref="ConfiguracaoLoja.GerarContaPagarAutomaticaDePedidoFornecedor"/>.
/// Categoria default: primeira ativa de Despesa.
/// </summary>
public sealed record GerarContaPagarDePedidoFornecedorCommand(
    Guid EmpresaId,
    PedidoFornecedor PedidoFornecedor,
    Guid? UserId = null,
    string? UserNome = null);

public class GerarContaPagarDePedidoFornecedorUseCase(
    IContaPagarRepository contaRepo,
    ICategoriaFinanceiraRepository categoriaRepo,
    IConfiguracaoLojaRepository configRepo,
    CriarContaPagarUseCase criarContaUseCase,
    ILogger<GerarContaPagarDePedidoFornecedorUseCase> logger)
{
    public async Task<ContaPagarResult?> ExecuteAsync(GerarContaPagarDePedidoFornecedorCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var pedido = cmd.PedidoFornecedor ?? throw new ArgumentNullException(nameof(cmd.PedidoFornecedor));

        var existente = await contaRepo.GetByOrigemAsync(cmd.EmpresaId, OrigemContaFinanceira.PedidoFornecedor, pedido.Id, ct);
        if (existente is not null)
        {
            logger.LogDebug("PedidoFornecedor {PedidoId} ja tem ContaPagar {ContaId} — no-op.", pedido.Id, existente.Id);
            return ContaPagarResult.De(existente);
        }

        // Flag por loja — pode nao ter LojaId no PedidoFornecedor; usar primeira loja se possivel?
        // Por seguranca, exigir LojaId presente pra ler config.
        Guid? lojaId = null;
        var lojaProp = pedido.GetType().GetProperty("LojaId");
        if (lojaProp is not null) lojaId = lojaProp.GetValue(pedido) as Guid?;

        if (lojaId is null) return null;
        var config = await configRepo.GetByLojaIdAsync(lojaId.Value);
        if (config is null || !config.GerarContaPagarAutomaticaDePedidoFornecedor) return null;

        var categorias = await categoriaRepo.ListarAsync(cmd.EmpresaId, ativa: true, tipo: TipoCategoriaFinanceira.Despesa, ct);
        var cat = categorias.FirstOrDefault();
        if (cat is null)
        {
            logger.LogWarning(
                "Empresa {EmpresaId} sem categoria de Despesa ativa — pulando geracao automatica de CP pra PedidoFornecedor {PedidoId}.",
                cmd.EmpresaId, pedido.Id);
            return null;
        }

        var valor = pedido.ValorEstimado ?? 0m;
        if (valor <= 0m)
        {
            logger.LogDebug("PedidoFornecedor {PedidoId} sem ValorEstimado — sem CP.", pedido.Id);
            return null;
        }

        var descricao = $"Compra {pedido.Id.ToString("N")[..8]}";
        var hoje = DateTime.UtcNow;

        try
        {
            var result = await criarContaUseCase.ExecuteAsync(new CriarContaPagarCommand(
                EmpresaId: cmd.EmpresaId,
                FornecedorId: pedido.FornecedorId,
                CategoriaFinanceiraId: cat.Id,
                Descricao: descricao,
                DataEmissao: hoje,
                Parcelas: new[]
                {
                    new ParcelaSpec(1, valor, hoje.AddDays(30), MetodoPlanejado: null)
                },
                CentroCustoId: null,
                LojaId: lojaId,
                Origem: OrigemContaFinanceira.PedidoFornecedor,
                OrigemRefId: pedido.Id,
                DocumentoReferencia: $"compra:{pedido.Id}",
                EmitirAposCriar: true), ct);

            logger.LogInformation("ContaPagar automatica criada pra PedidoFornecedor {PedidoId}: CP={ContaId}",
                pedido.Id, result.Id);
            return result;
        }
        catch (UseCaseValidationException ex)
        {
            logger.LogWarning(ex,
                "Falha ao gerar ContaPagar automatica pra PedidoFornecedor {PedidoId} — pulando.",
                pedido.Id);
            return null;
        }
    }
}
