using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Financeiro.Integracao;

/// <summary>
/// Gera ContaReceber automaticamente a partir de Pedido entregue (ou status
/// configurado em <see cref="ConfiguracaoLoja.StatusPedidoQueGeraContaReceber"/>).
///
/// <para>Idempotente</para> via <c>(EmpresaId, Origem=Pedido, OrigemRefId=PedidoId)</c>
/// UNIQUE — segunda chamada com mesmo Pedido retorna a CR existente.
///
/// <para>Categoria default</para>: usa primeira categoria ativa de Receita
/// (ou Ambas) da empresa. Se nao houver, retorna null e loga warn — nao
/// bloqueia transicao do pedido.
///
/// <para>Flag opt-in</para>: <see cref="ConfiguracaoLoja.GerarContaReceberAutomaticaDePedido"/>
/// = true exigido. Default false.
/// </summary>
public sealed record GerarContaReceberDePedidoCommand(
    Guid EmpresaId,
    EasyStock.Domain.Entities.Pedido Pedido,
    Guid? UserId = null,
    string? UserNome = null);

public class GerarContaReceberDePedidoUseCase(
    IContaReceberRepository contaRepo,
    ICategoriaFinanceiraRepository categoriaRepo,
    IConfiguracaoLojaRepository configRepo,
    CriarContaReceberUseCase criarContaUseCase,
    ILogger<GerarContaReceberDePedidoUseCase> logger,
    EasyStock.Application.Services.Pedidos.SaldoFinanceiroPedido? saldos = null)
{
    public async Task<ContaReceberResult?> ExecuteAsync(GerarContaReceberDePedidoCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var pedido = cmd.Pedido ?? throw new ArgumentNullException(nameof(cmd.Pedido));

        // Idempotencia: ja existe CR pra esse pedido?
        var existente = await contaRepo.GetByOrigemAsync(cmd.EmpresaId, OrigemContaFinanceira.Pedido, pedido.Id, ct);
        if (existente is not null)
        {
            logger.LogDebug("Pedido {PedidoId} ja tem ContaReceber {ContaId} — no-op.", pedido.Id, existente.Id);
            return ContaReceberResult.De(existente);
        }

        // Flag opt-in por loja
        if (pedido.LojaId is null) return null;
        var config = await configRepo.GetByLojaIdAsync(pedido.LojaId.Value);
        if (config is null || !config.GerarContaReceberAutomaticaDePedido) return null;

        // Categoria default: primeira ativa Receita ou Ambas
        var categorias = await categoriaRepo.ListarAsync(cmd.EmpresaId, ativa: true, tipo: TipoCategoriaFinanceira.Receita, ct);
        var cat = categorias.FirstOrDefault();
        if (cat is null)
        {
            logger.LogWarning(
                "Empresa {EmpresaId} sem categoria de Receita ativa — pulando geracao automatica de CR pra pedido {PedidoId}.",
                cmd.EmpresaId, pedido.Id);
            return null;
        }

        // #2095 (F10): cobra o que falta receber (total menos o alocado: pagamentos, estornos e cupons), não o total.
        // Pedido pago no site não vira conta aberta e depois "vencida". Sem o serviço (teste antigo), só os pagamentos.
        var valor = saldos is not null
            ? await saldos.AReceberAsync(pedido, ct)
            : Math.Max(0m, pedido.Total.Valor - pedido.TotalPago);
        if (valor <= 0m)
        {
            logger.LogDebug("Pedido {PedidoId} sem saldo a receber — sem CR.", pedido.Id);
            return null;
        }

        var descricao = $"Pedido {CodigoCurto.De(pedido.Id)} — {pedido.ClienteNome ?? "Cliente"}";
        var hoje = DateTime.UtcNow;
        // Vencimento é data civil de Brasília (meia-noite UTC do dia), como as demais colunas de vencimento.
        var vencimento = HorarioBrasil.CivilComoInstanteUtc(HorarioBrasil.Hoje().AddDays(7));

        try
        {
            var result = await criarContaUseCase.ExecuteAsync(new CriarContaReceberCommand(
                EmpresaId: cmd.EmpresaId,
                ClienteId: pedido.ClienteId,
                CategoriaFinanceiraId: cat.Id,
                Descricao: descricao,
                DataEmissao: hoje,
                Parcelas: new[]
                {
                    new ParcelaSpec(1, valor, vencimento, MetodoPlanejado: null)
                },
                CentroCustoId: null,
                LojaId: pedido.LojaId,
                Origem: OrigemContaFinanceira.Pedido,
                OrigemRefId: pedido.Id,
                DocumentoReferencia: $"pedido:{pedido.Id}",
                EmitirAposCriar: true), ct);

            logger.LogInformation("ContaReceber automatica criada pra pedido {PedidoId}: CR={ContaId}",
                pedido.Id, result.Id);
            return result;
        }
        catch (UseCaseValidationException ex)
        {
            logger.LogWarning(ex,
                "Falha ao gerar ContaReceber automatica pra pedido {PedidoId} — pulando.",
                pedido.Id);
            return null;
        }
    }
}
