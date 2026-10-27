using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Caixa;
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
    bool PermitirExcedente = false,
    // S13: pagamento já confirmado na fonte pelo provedor (ConfirmarPagamentoPedidoUseCase). Dinheiro
    // recebido é registrado mesmo com o pedido aguardando a aprovação da dona (Pedido.RequerAprovacao,
    // S12); a guarda de pré-operacional continua valendo para todo registro manual.
    bool ConfirmadoPeloProvedor = false,
    // #1724-12: data da aprovação no provedor (date_approved). Só vale com ConfirmadoPeloProvedor; a baixa manual
    // grava sempre o instante do registro.
    DateTime? AprovadoNoProvedorEm = null);

public class RegistrarPagamentoPedidoUseCase(
    IPedidoRepository repo,
    IUnitOfWork uow,
    ILogger<RegistrarPagamentoPedidoUseCase> logger,
    CalculadoraInicioPrevistoPedido inicioPrevisto,
    QuitacaoPedido quitacao,
    ICaixaRepository? caixaRepo = null,
    SaldoFinanceiroPedido? saldos = null,
    Lazy<EasyStock.Application.Services.Atendimento.AvisoStatusPedidoCliente>? avisoCliente = null,
    EasyStock.Application.UseCases.ListarFechamentosCaixa.ListarFechamentosCaixaUseCase? fechamentos = null)
{
    private static readonly HashSet<string> MetodosValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "pix", "dinheiro", "credito", "debito", "transferencia", "outro"
    };

    public async Task<PedidoResult?> ExecuteAsync(RegistrarPagamentoPedidoCommand cmd, CancellationToken ct = default,
        Func<PedidoResult, CancellationToken, Task>? antesDoCommit = null,
        Func<CancellationToken, Task<PedidoResult?>>? recuperarRecibo = null)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");
        if (cmd.Valor <= 0)
            throw new UseCaseValidationException("Valor do pagamento deve ser maior que zero.");
        if (cmd.Valor != decimal.Round(cmd.Valor, 2))
            throw new UseCaseValidationException("Valor do pagamento deve ter até duas casas decimais.");
        // Teto de sanidade anti-fat-finger (espelha LimitesProduto.ValorMaximo). NAO bloqueia
        // overpay (pagar acima do pendente e cenario real: gorjeta/arredondamento) -- o aviso
        // de overpay e so no frontend (Detail.cshtml). QA v1.10 r3 BUG-004 (issue 607).
        if (cmd.Valor > EasyStock.Application.Validators.LimitesProduto.ValorMaximo)
            throw new UseCaseValidationException(
                $"Valor do pagamento acima do teto permitido ({EasyStock.Application.Validators.LimitesProduto.ValorMaximo.ToString("C", Cultura.PtBr)}).");

        var metodo = (cmd.Metodo ?? "").Trim().ToLowerInvariant();
        if (!MetodosValidos.Contains(metodo))
            throw new UseCaseValidationException($"Método inválido: {cmd.Metodo}");

        EfeitosQuitacao? efeito = null;
        PedidoMudouStatusOperacao? mudanca = null;
        (EasyStock.Domain.Entities.Pedido Pedido, PedidoPagamento Pagamento)? sinalManual = null;
        var resultado = await uow.ExecuteInTransactionSemRetryAsync(async token =>
        {
            if (!cmd.ConfirmadoPeloProvedor) await repo.TravarAsync(cmd.EmpresaId, cmd.PedidoId, token);
            var pedido = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.PedidoId);
            if (pedido == null || pedido.EmpresaId != cmd.EmpresaId) return null;

            // Um takeover pode ter ocorrido enquanto o primeiro recebimento segurava este lock.
            // O recibo e o pagamento foram gravados juntos; releia antes de aplicar outro parcial.
            if (recuperarRecibo is not null && await recuperarRecibo(token) is { } recibo) return recibo;

            // Pagamento manual so em pedido operacional: pre-operacional (montagem / pagamento
            // online pendente / aprovacao do cardapio guest) ainda nao e recebivel. Fecha o
            // pagamento-fantasma em pedido do cardapio nao aprovado, em TODA superficie que chame
            // este use case (cockpit, Detail, mobile, API). Espelha PRE_OPERACIONAL do cockpit (issue 862).
            if (!cmd.ConfirmadoPeloProvedor
                && StatusPedidoMapper.TryParse(pedido.Status, out var statusPedido)
                && !PedidoStateMachine.AceitaPagamento(statusPedido)
                && !await quitacao.PodeReceberSinalManualAsync(pedido, token))
            {
                throw new UseCaseValidationException(statusPedido == StatusPedido.Cancelado
                    ? "Não é possível registrar pagamento: o pedido está cancelado."
                    : "Não é possível registrar pagamento: o pedido ainda não foi confirmado/aprovado.");
            }

            // issue 962 (BUG-002 do QA): o guard client-side do cockpit e' sobre dado carregado —
            // uma aba stale (lista rec-carregada, pagamento feito em outra tela) repete o
            // superpagamento acidental, e o servidor aceitava por design (#607) sem distinguir
            // deliberado de acidental. Overpay so passa com PermitirExcedente=true, enviado
            // SOMENTE pelo Detail.cshtml (onde o aviso de gorjeta/arredondamento ja existe).
            var alocado = saldos is null ? pedido.TotalPago : (await saldos.ConsultarAsync(pedido, token)).Alocado;
            var pendente = Math.Max(0m, pedido.Total.Valor - alocado);
            if (cmd.Valor > pendente && !cmd.PermitirExcedente)
            {
                throw new UseCaseValidationException(
                    $"Valor do pagamento ({cmd.Valor.ToString("C", Cultura.PtBr)}) excede o pendente ({pendente.ToString("C", Cultura.PtBr)}).");
            }

            var pag = new PedidoPagamento
            {
                Id = Guid.NewGuid(),
                PedidoId = pedido.Id,
                Metodo = metodo,
                Valor = cmd.Valor,
                Referencia = cmd.Referencia,
                Observacao = cmd.Observacao,
                PagoEm = await PagoEmAsync(cmd, pedido.LojaId),
                RegistradoPorUserId = cmd.RegistradoPorUserId,
                RegistradoPorNome = cmd.RegistradoPorNome
            };

            // issue 1029: NAO adicionar pag a pedido.Pagamentos aqui. O agregado e rastreado
            // (GetByIdWithDetailsAsync nao usa AsNoTracking) e o relationship fixup do EF ja
            // popula a colecao quando AddPagamentoAsync faz db.Set<PedidoPagamento>().Add(pag).
            // Um Add explicito entraria DE NOVO na List<> (que nao deduplica por referencia) e
            // dobraria pedido.TotalPago, que e derivado de Pagamentos -- medido: a resposta
            // devolvia 160 para um pagamento de 80. Foi por isso que o 4abb7bb5 removeu a linha.
            await repo.AddPagamentoAsync(pag);

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

            // #1230: pagamento manual (Pix, dinheiro, cartão) em pedido da fila vira compromisso como no
            // Mercado Pago: grava o início previsto (agregado rastreado, sai no flush abaixo). O pagamento do
            // provedor chega do ConfirmarPagamentoPedidoUseCase, que já gravou antes de chamar este use case.
            if (!cmd.ConfirmadoPeloProvedor)
            {
                mudanca = await quitacao.ConfirmarSinalManualAsync(pedido, pag, token);
                if (mudanca is null) await inicioPrevisto.AplicarNaFilaAsync(pedido, token);
                else sinalManual = (pedido, pag);
            }

            if (!cmd.ConfirmadoPeloProvedor && cmd.Origem != "balcao")
                efeito = await quitacao.ConcluirManualAsync(pedido, pag, token);

            // Flush do pagamento+evento ANTES da tentativa de abertura (ver comentario acima
            // sobre savepoint automatico) — e o que garante que a abertura nunca pode derrubar
            // o pagamento, mesmo que colida com a unique.
            await uow.CommitAsync();

            await TentarAbrirCaixaAsync(cmd, pedido, pag.PagoEm, token);

            logger.LogInformation("Pedido {Id}: pagamento {Valor} {Metodo} (TotalPago={TotalPago}/{Total}).",
                pedido.Id, pag.Valor, metodo, pedido.TotalPago, pedido.Total);
            var resposta = CriarPedidoUseCase.Map(pedido);
            if (antesDoCommit is not null) await antesDoCommit(resposta, token);
            return resposta;
        }, ct);
        await quitacao.PublicarAsync(cmd.EmpresaId, efeito, ct, mudanca);
        if (sinalManual is { } sinal) await AvisarSinalManualAsync(sinal.Pedido, sinal.Pagamento, ct);
        return resultado;
    }

    /// <summary>
    /// #2007 (item 6): o sinal da encomenda registrado à mão avisa o cliente como o pago pelo Mercado Pago, pelo mesmo
    /// aviso do pedido (WhatsApp ou chat do site). Sai uma vez: só na passagem que completa o sinal, e a chave leva o
    /// id do pagamento. Pagamento que já quita fica com o aviso de pedido pago. Mensagem não é dinheiro: sai depois do
    /// commit do pagamento, numa gravação própria, e a falha só loga. Os valores seguem o do Mercado Pago: o dinheiro
    /// recebido e o que falta pelo saldo financeiro (cupom e devolução contam).
    /// </summary>
    private async Task AvisarSinalManualAsync(EasyStock.Domain.Entities.Pedido pedido, PedidoPagamento pag, CancellationToken ct)
    {
        if (avisoCliente is null) return;
        try
        {
            var restante = saldos is null
                ? pedido.Total.Valor - pedido.TotalPago
                : (await saldos.ConsultarAsync(pedido, ct)).AReceber(pedido.Total.Valor);
            if (restante <= 0) return;
            await avisoCliente.Value.EnfileirarAsync(Domain.Enums.Notifications.TipoEventoNotificacao.ComandaAtualizada,
                pedido.EmpresaId, pedido.Id, $"sinal:manual-{pag.Id:N}", ct,
                mensagem: EasyStock.Application.UseCases.Pedidos.Cobranca.ConfirmarPagamentoPedidoUseCase.TextoSinalRecebido(pedido.TotalPago, restante));
            await uow.CommitAsync();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Aviso de sinal manual nao enfileirado pedidoId={PedidoId} pagamento={Pagamento}", pedido.Id, pag.Id);
        }
    }

    /// <summary>
    /// #1724-12: o recebimento do provedor vale na data da aprovação. Não volta para um dia de Caixa já fechado: o
    /// fechamento não o retratou (CaixaFechadoGuard), e ele entra no dia em que foi registrado.
    /// </summary>
    private async Task<DateTime> PagoEmAsync(RegistrarPagamentoPedidoCommand cmd, Guid? lojaId)
    {
        var agora = DateTime.UtcNow;
        if (!cmd.ConfirmadoPeloProvedor || DataUtc.ParaUtcOpcional(cmd.AprovadoNoProvedorEm) is not { } aprovado || aprovado >= agora)
            return agora;
        if (caixaRepo is not null && await CaixaFechadoGuard.DiaFechadoAsync(caixaRepo, cmd.EmpresaId, aprovado, lojaId))
            return agora;
        return aprovado;
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
        // #1889: pagamento do provedor não entra na gaveta e chega a qualquer hora. Abrir o caixa com R$ 0 de
        // madrugada fazia o console pular a conferência do troco inicial; o pagamento entra no dia do mesmo jeito.
        if (cmd.ConfirmadoPeloProvedor) return;

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
            // #1598: vale o fechamento da loja do pedido ou o da empresa (CaixaFechadoGuard).
            if (await CaixaFechadoGuard.DiaFechadoAsync(caixaRepo, cmd.EmpresaId, pagoEm, pedido.LojaId))
            {
                logger.LogInformation("Caixa de {Data} já fechado; pagamento {PedidoId} não dispara abertura automática.", data, pedido.Id);
                return;
            }

            var movimentos = await caixaRepo.GetMovimentosDoDiaAsync(cmd.EmpresaId, data, pedido.LojaId);
            if (movimentos.Any(m => m.Tipo == "abertura")) return;

            // #1506: sessao de dia anterior ainda aberta: nao abre o dia por cima (a antiga ficaria
            // orfa, sem fechamento). O pagamento ja esta gravado e entra no caixa do dia dele.
            var pendente = await caixaRepo.GetAberturaPendenteAsync(cmd.EmpresaId, pedido.LojaId);
            if (pendente != null && HorarioBrasil.DataOperacional(pendente.DataMovimento) < data)
            {
                logger.LogInformation("Caixa de {Pendente} ainda aberto; pagamento {PedidoId} não abre o caixa de {Data}.",
                    HorarioBrasil.DataOperacional(pendente.DataMovimento), pedido.Id, data);
                return;
            }

            // #2084 (B1): a gaveta não começa vazia. Herda o dinheiro deixado no último fechamento, a mesma sugestão da
            // abertura manual, e fica marcada para a gerente conferir depois (ConfirmacaoPendente). Não bloqueia o
            // pagamento: sem fechamento anterior, abre com zero, também a confirmar.
            var herdada = fechamentos is null ? null : await fechamentos.GavetaDoUltimoFechamentoAsync(cmd.EmpresaId);
            var abertura = MovimentoCaixa.Criar(cmd.EmpresaId, "abertura", Math.Max(0m, herdada?.Dinheiro ?? 0m), pagoEm, pedido.LojaId);
            abertura.Descricao = herdada is { } h
                ? $"Abertura automática no primeiro pagamento de pedido do dia, com {h.Dinheiro.ToString("C", Cultura.PtBr)} deixados no fechamento de {h.Data:dd/MM}. Falta a gerente conferir."
                : "Abertura automática no primeiro pagamento de pedido do dia. Falta a gerente conferir.";
            abertura.ConfirmacaoPendente = true;
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
