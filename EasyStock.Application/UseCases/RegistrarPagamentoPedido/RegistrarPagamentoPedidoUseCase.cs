using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Sales;

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
    [property: MaxLength(20)] string? Origem = "web",
    // issue 962 (BUG-002 do QA): overpay continua permitido por decisao de produto (#607 --
    // gorjeta/arredondamento), mas so quando o CALLER sinaliza explicitamente que mostrou o
    // aviso. Hoje so o Detail.cshtml (onde o aviso de excedente ja existe) envia true; o
    // cockpit NAO envia -- mantem o bloqueio duro la, onde o risco de fat-finger e maior e o
    // valor costuma vir pre-preenchido de uma listagem que pode estar desatualizada.
    bool PermitirExcedente = false);

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

    public async Task<PedidoResult?> ExecuteAsync(RegistrarPagamentoPedidoCommand cmd, CancellationToken ct = default)
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

        // Pagamento manual so em pedido operacional: pre-operacional (montagem / pagamento
        // online pendente / aprovacao do cardapio guest) ainda nao e recebivel. Fecha o
        // pagamento-fantasma em pedido do cardapio nao aprovado, em TODA superficie que chame
        // este use case (cockpit, Detail, mobile, API). Espelha PRE_OPERACIONAL do cockpit (issue 862).
        if (StatusPedidoMapper.TryParse(pedido.Status, out var statusPedido)
            && !PedidoStateMachine.AceitaPagamento(statusPedido))
        {
            throw new UseCaseValidationException(
                "Não é possível registrar pagamento: o pedido ainda não foi confirmado/aprovado.");
        }

        // issue 962 (BUG-002 do QA): o guard client-side do cockpit e' sobre dado carregado —
        // uma aba stale (lista rec-carregada, pagamento feito em outra tela) repete o
        // superpagamento acidental, e o servidor aceitava por design (#607) sem distinguir
        // deliberado de acidental. Overpay so passa com PermitirExcedente=true, enviado
        // SOMENTE pelo Detail.cshtml (onde o aviso de gorjeta/arredondamento ja existe).
        var pendente = Math.Max(0m, pedido.Total.Valor - pedido.TotalPago);
        if (cmd.Valor > pendente && !cmd.PermitirExcedente)
        {
            throw new UseCaseValidationException(
                $"Valor do pagamento ({cmd.Valor.ToString("C", Cultura.PtBr)}) excede o pendente ({pendente.ToString("C", Cultura.PtBr)}).");
        }

        // issue 951: todo o restante roda numa transacao explicita (SemRetry — pag/evento tem
        // Guid.NewGuid() fresco; retry reexecutaria o lambda e duplicaria, mesma classe do #952).
        // A transacao explicita e o que permite o flush ISOLADO da abertura automatica logo
        // abaixo: se ela colidir com a unique parcial, so aquele SaveChanges e revertido (EF Core
        // cria savepoint automatico a cada SaveChanges dentro de uma tx ja em andamento) — o
        // pagamento, commitado no flush ANTERIOR, permanece de pe ate o commit final da tx.
        return await uow.ExecuteInTransactionSemRetryAsync(async token =>
        {
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

            // Flush do pagamento+evento ANTES da tentativa de abertura (ver comentario acima
            // sobre savepoint automatico) — e o que garante que a abertura nunca pode derrubar
            // o pagamento, mesmo que colida com a unique.
            await uow.CommitAsync();

            await TentarAbrirCaixaAsync(cmd, pedido, pag.PagoEm, token);

            logger.LogInformation("Pedido {Id}: pagamento {Valor} {Metodo} (TotalPago={TotalPago}/{Total}).",
                pedido.Id, pag.Valor, metodo, pedido.TotalPago, pedido.Total);
            return CriarPedidoUseCase.Map(pedido);
        }, ct);
    }

    private async Task TentarAbrirCaixaAsync(RegistrarPagamentoPedidoCommand cmd, EasyStock.Domain.Entities.Pedido pedido, DateTime pagoEm, CancellationToken ct)
    {
        // Caixa segue Pedido: ao receber o primeiro pagamento do dia, abrimos o caixa
        // automaticamente para evitar divergência "pedidos pagos mas caixa zerado".
        //
        // Best-effort de verdade: o pagamento já foi commitado no flush anterior (ver
        // ExecuteAsync) antes deste método rodar, então NENHUMA falha aqui — esperada
        // (corrida perdida) ou não — pode derrubá-lo. TryAddMovimentoAsync trata a corrida
        // como caso normal (retorna false); o catch abaixo é so para falhas inesperadas
        // (timeout, conexão, etc.) que não devem propagar como 500 pro operador.
        if (caixaRepo is null) return;

        try
        {
            // issue 951: dia OPERACIONAL de Brasília (não dia civil UTC) — a unique parcial
            // (ix_movimentos_caixa_abertura_unica) agrupa por caixa_abertura_utc_day (UTC-3).
            // DateOnly.FromDateTime(pagoEm) divergia do índice exatamente na janela 21h-24h
            // BRT: o pagamento via dia N+1 (UTC), a abertura existente do dia N (BRT) não
            // era vista, uma segunda abertura era tentada, e ela colidia com a primeira.
            var data = HorarioBrasil.DataOperacional(pagoEm);

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

            var criada = await caixaRepo.TryAddMovimentoAsync(abertura, ct);
            if (criada)
                logger.LogInformation("Caixa aberto automaticamente em {Data} por pagamento de pedido {PedidoId}.", data, pedido.Id);
            else
                logger.LogInformation(
                    "Abertura de caixa em {Data} já existe (corrida perdida contra outra transação); pagamento de pedido {PedidoId} já persistido.",
                    data, pedido.Id);
        }
        catch (Exception ex)
        {
            // Falha inesperada (nao a corrida esperada — essa TryAddMovimentoAsync ja
            // absorve retornando false). Nao propagar: o pagamento ja foi commitado antes
            // desta chamada.
            logger.LogWarning(ex, "Falha inesperada ao abrir caixa automaticamente para pedido {PedidoId}. Pagamento já persistido.", pedido.Id);
        }
    }
}
