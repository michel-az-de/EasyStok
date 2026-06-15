using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;

namespace EasyStock.Application.UseCases.RegistrarPagamentoPedido;

public sealed record RegistrarPagamentoPedidoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid PedidoId,
    [property: Required][property: MaxLength(20)] string Metodo,
    decimal Valor,
    [property: MaxLength(120)] string? Referencia = null,
    string? Observacao = null,
    Guid? RegistradoPorUserId = null,
    [property: MaxLength(120)] string? RegistradoPorNome = null,
    [property: MaxLength(20)] string? Origem = "web");

public class RegistrarPagamentoPedidoUseCase(
    IPedidoRepository repo,
    IUnitOfWork uow,
    ILogger<RegistrarPagamentoPedidoUseCase> logger,
    ICaixaRepository? caixaRepo = null)
{
    private static readonly HashSet<string> MetodosValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "pix", "dinheiro", "credito", "debito", "transferencia", "outro"
    };

    public async Task<PedidoResult?> ExecuteAsync(RegistrarPagamentoPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");
        if (cmd.Valor <= 0)
            throw new UseCaseValidationException("Valor do pagamento deve ser maior que zero.");
        // Teto de sanidade anti-fat-finger (espelha LimitesProduto.ValorMaximo). NAO bloqueia
        // overpay (pagar acima do pendente e cenario real: gorjeta/arredondamento) -- o aviso
        // de overpay e so no frontend (Detail.cshtml). QA v1.10 r3 BUG-004 (issue 607).
        if (cmd.Valor > EasyStock.Application.Validators.LimitesProduto.ValorMaximo)
            throw new UseCaseValidationException(
                $"Valor do pagamento acima do teto permitido ({EasyStock.Application.Validators.LimitesProduto.ValorMaximo.ToString("C", Cultura.PtBr)}).");

        var metodo = (cmd.Metodo ?? "").Trim().ToLowerInvariant();
        if (!MetodosValidos.Contains(metodo))
            throw new UseCaseValidationException($"Método inválido: {cmd.Metodo}");

        var pedido = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.PedidoId);
        if (pedido == null) return null;

        var pag = new PedidoPagamento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Metodo = metodo,
            Valor = cmd.Valor,
            Referencia = cmd.Referencia,
            Observacao = cmd.Observacao,
            PagoEm = DateTime.UtcNow,
            RegistradoPorUserId = cmd.RegistradoPorUserId,
            RegistradoPorNome = cmd.RegistradoPorNome
        };

        await repo.AddPagamentoAsync(pag);
        pedido.Pagamentos.Add(pag);

        await repo.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "pagamento",
            UsuarioId = cmd.RegistradoPorUserId,
            UsuarioNome = cmd.RegistradoPorNome,
            Origem = cmd.Origem,
            OcorridoEm = DateTime.UtcNow,
            Detalhes = $"+{pag.Valor.ToString("C", Cultura.PtBr)} via {metodo}"
        });

        await TentarAbrirCaixaAsync(cmd, pedido, pag.PagoEm);

        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id}: pagamento {Valor} {Metodo} (TotalPago={TotalPago}/{Total}).",
            pedido.Id, pag.Valor, metodo, pedido.TotalPago, pedido.Total);
        return CriarPedidoUseCase.Map(pedido);
    }

    private async Task TentarAbrirCaixaAsync(RegistrarPagamentoPedidoCommand cmd, EasyStock.Domain.Entities.Pedido pedido, DateTime pagoEm)
    {
        // Caixa segue Pedido: ao receber o primeiro pagamento do dia, abrimos o caixa
        // automaticamente para evitar divergência "pedidos pagos mas caixa zerado".
        //
        // Best-effort: qualquer falha aqui não pode derrubar o pagamento. Race condition
        // entre TXs paralelas é mitigada pelo UNIQUE INDEX parcial em movimentos_caixa
        // (migration 20260516010000_UniqueAberturaCaixaPorDia) — quem perde a corrida
        // recebe unique_violation, capturado abaixo.
        if (caixaRepo is null) return;

        try
        {
            var data = DateOnly.FromDateTime(pagoEm);

            // Não reabre caixa fechado (replica regra de AbrirCaixaUseCase). Pagamentos
            // retroativos em dia fechado ficam apenas como PedidoPagamento; aparecem
            // na agregação do dia via GetTotalPagamentosPedidosDoDiaAsync.
            var fechamento = await caixaRepo.GetFechamentoDoDiaAsync(cmd.EmpresaId, data, pedido.LojaId);
            if (fechamento != null)
            {
                logger.LogInformation("Caixa de {Data} já fechado; pagamento {PedidoId} não dispara abertura automática.", data, pedido.Id);
                return;
            }

            var movimentos = await caixaRepo.GetMovimentosDoDiaAsync(cmd.EmpresaId, data, pedido.LojaId);
            if (movimentos.Any(m => m.Tipo == "abertura")) return;

            var abertura = MovimentoCaixa.Criar(cmd.EmpresaId, "abertura", 0m, pagoEm, pedido.LojaId);
            abertura.Descricao = "Abertura automática no primeiro pagamento de pedido do dia.";
            abertura.RegistradoPorUserId = cmd.RegistradoPorUserId;
            abertura.RegistradoPorNome = string.IsNullOrWhiteSpace(cmd.RegistradoPorNome) ? "Sistema" : cmd.RegistradoPorNome;
            abertura.Origem = "auto-pagamento";
            await caixaRepo.AddMovimentoAsync(abertura);
            logger.LogInformation("Caixa aberto automaticamente em {Data} por pagamento de pedido {PedidoId}.", data, pedido.Id);
        }
        catch (Exception ex)
        {
            // Não propagar: pagamento já foi adicionado ao UoW. Se a abertura falhar
            // (race, constraint, timeout), o pagamento deve persistir mesmo assim.
            logger.LogWarning(ex, "Falha ao abrir caixa automaticamente para pedido {PedidoId}. Pagamento prossegue.", pedido.Id);
        }
    }
}
