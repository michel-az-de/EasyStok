using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects.Pix;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <summary>Reemissão ou cobrança de um pedido já gravado (job, troca de forma, operadora).</summary>
/// <param name="NaEntregaSemProvedor">Pedido confirmado pela atendente (#1726): sem link possível, a dívida fica a receber na entrega em vez de falhar.</param>
public sealed record GerarCobrancaPedidoInput(Guid EmpresaId, Guid PedidoId, Guid? ConversaId = null, int Tentativa = 1, bool CobrarSaldo = false,
    bool NaEntregaSemProvedor = false);

public sealed record CobrancaPedidoResult(
    Guid CobrancaId,
    Guid PedidoId,
    string Provedor,
    string Status,
    string? LinkPagamento,
    decimal Valor,
    DateTime? ExpiraEm,
    int Tentativa,
    bool Reutilizada)
{
    /// <summary>
    /// #1717-8: preferência do link pendente que esta cobrança substituiu. Quem fecha a transação a expira depois do
    /// commit (<see cref="Services.Pedidos.ExpiracaoPreferenciaMercadoPago"/>). Não sai na resposta da API.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PreferenciaSubstituida { get; init; }

    /// <summary>
    /// #2233: copia e cola do Pix direto. Carrega a chave da loja, por isso só sai para quem cuida do pedido (console)
    /// ou para o próprio cliente; nunca para o cardápio público.
    /// </summary>
    public string? PixCopiaCola { get; init; }

    /// <summary>
    /// #2684: mensagem pronta ao cliente quando a cobrança é Pix direto (valor, recebedor e copia e cola), para quem
    /// avisa na conversa. Leva a chave da loja: nunca sai na resposta da API.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? TextoPix { get; init; }

    /// <summary>#2684: o Pix direto pendente caiu porque a loja ficou sem chave; a cobrança voltou para a entrega.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PixDiretoSemChave { get; init; }

    public static CobrancaPedidoResult De(CobrancaPedido c, bool reutilizada = false) =>
        new(c.Id, c.PedidoId, c.Provedor, c.Status.ToString(), c.LinkPagamento, c.Valor, c.ExpiraEm, c.Tentativa, reutilizada)
        {
            PixCopiaCola = c.PixCopiaCola,
        };
}

/// <summary>
/// Cobra o pedido pelo Mercado Pago (S11): preferência do Checkout Pro com itens e frete,
/// <c>external_reference = PedidoId</c>, expiração de 30 minutos e <c>X-Idempotency-Key</c>; grava a
/// <see cref="CobrancaPedido"/> pendente e devolve o link. Um caminho só para o site
/// (<c>IniciarCheckoutUseCase</c>, fase 3) e para a conversa (ferramenta <c>criar_pedido</c>).
///
/// <para>
/// Idempotente: com uma cobrança online pendente e ainda válida, devolve a mesma sem chamar o Mercado
/// Pago. Pendente vencida é marcada <c>Expirada</c> e nasce outra. A chave de idempotência é
/// <c>{PedidoId}-{n}</c>, com <c>n</c> = número da cobrança no pedido: o retry da mesma chamada repete a
/// chave (o Mercado Pago devolve a mesma preferência) e uma cobrança nova nunca reaproveita a anterior.
/// </para>
///
/// <para>
/// Falha ou timeout (5 s) do Mercado Pago: <see cref="MercadoPagoIndisponivelException"/>, nenhuma cobrança
/// gravada. No pedido recém-reservado, a reserva é desfeita antes de relançar (#1301): pedido cancelado com
/// motivo <c>mercado_pago_indisponivel</c> e vaga liberada, porque sem cobrança nenhum job o alcança.
/// </para>
/// </summary>
public sealed class GerarCobrancaPedidoUseCase(
    IPedidoRepository pedidoRepository,
    IStorefrontRepository storefrontRepository,
    ICobrancaPedidoRepository cobrancaRepository,
    IMercadoPagoClient mercadoPagoClient,
    CheckoutCoreService checkoutCore,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<GerarCobrancaPedidoUseCase> logger,
    SaldoFinanceiroPedido? saldoFinanceiro = null,
    IConversaRepository? conversaRepository = null,
    ILojaRepository? lojas = null)
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);

    /// <summary>Motivo gravado na pendente cancelada porque o total do pedido mudou (#1291).</summary>
    public const string MotivoValorAlterado = "valor_do_pedido_alterado";
    private static readonly TimeSpan MpTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Pedido recém-criado pelo núcleo do checkout (site ou conversa). Se o Mercado Pago não responde, desfaz a
    /// reserva e relança <see cref="MercadoPagoIndisponivelException"/> (#1301).
    /// </summary>
    public async Task<CobrancaPedidoResult> ExecuteAsync(PedidoReservado reservado, Guid? conversaId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservado);
        var itens = reservado.Itens.Append(reservado.ItemFrete).ToList();
        var dados = new DadosCobranca(
            reservado.Pedido.EmpresaId, reservado.Pedido.Id, reservado.Storefront.Id,
            reservado.Storefront.TituloPublico, reservado.Total, itens, reservado.Pedido.AgendadoParaEm, reservado.Pedido.CriadoEm,
            RetornoWhatsApp: reservado.Storefront.LinkWhatsAppPedidos());
        try
        {
            return await CobrarAsync(dados, conversaId, tentativa: 1, ct);
        }
        catch (MercadoPagoIndisponivelException)
        {
            await DesfazerReservaAsync(reservado);
            throw;
        }
    }

    /// <summary>
    /// Sem <see cref="CancellationToken"/> da requisição: a compensação roda mesmo se o cliente desistiu. Se ela
    /// falhar, a resposta segue sendo a indisponibilidade do Mercado Pago e a varredura de abandonados cancela
    /// o pedido depois de 30 minutos.
    /// </summary>
    private async Task DesfazerReservaAsync(PedidoReservado reservado)
    {
        try
        {
            await checkoutCore.DesfazerReservaAsync(
                reservado, CheckoutCoreService.MotivoMercadoPagoIndisponivel, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cobranca reserva-nao-desfeita pedidoId={PedidoId}", reservado.Pedido.Id);
        }
    }

    /// <summary>
    /// Reemissão pedida por uma pessoa (console). Trava o pedido antes de olhar as cobranças (#1724): sem a
    /// trava, cliques simultâneos liam "sem pendente" e cada um criava um link pagável para o mesmo pedido.
    /// #1717-8: o link substituído por valor expira no provedor depois que a transação fechou.
    /// </summary>
    public async Task<CobrancaPedidoResult> ExecuteComTravaAsync(GerarCobrancaPedidoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var resultado = await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
        {
            await pedidoRepository.TravarAsync(input.EmpresaId, input.PedidoId, token);
            return await ExecuteAsync(input, token);
        }, ct);
        if (resultado.PreferenciaSubstituida is { } preferencia)
            await ExpiracaoPreferenciaMercadoPago.ExpirarAsync(() => mercadoPagoClient, preferencia,
                relogio.GetUtcNow().UtcDateTime, logger, ct);
        return resultado;
    }

    /// <summary>Pedido já gravado: carrega itens e frete do banco.</summary>
    public async Task<CobrancaPedidoResult> ExecuteAsync(GerarCobrancaPedidoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");

        var pedido = await pedidoRepository.GetByIdWithDetailsAsync(input.EmpresaId, input.PedidoId)
            ?? throw new CobrancaPedidoNaoEncontradoException(input.PedidoId);
        // #2813: o entregue do histórico já foi pago na origem; link ou cobrança nova seria cobrar de novo.
        if (pedido.QuitadoHistorico)
            throw new UseCaseValidationException("Não é possível gerar cobrança: o pedido do histórico já está quitado.");
        if (PedidoStateMachine.EstaFinalizado(pedido.StatusEnum))
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoFinalizado,
                "Pedido cancelado ou entregue não recebe cobrança.");
        // Mesma conta da confirmação (#1289): com os pagamentos cobrindo o total, o webhook trataria
        // o pagamento deste link como duplicado e o devolveria sozinho.
        var alocado = saldoFinanceiro is not null
            ? (await saldoFinanceiro.ConsultarAsync(pedido, ct)).Alocado : pedido.TotalPago;
        if (pedido.Total.Valor > 0m && alocado >= pedido.Total.Valor)
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoJaPago, "Pedido já pago.");

        var storefront = await storefrontRepository.GetByEmpresaAsync(input.EmpresaId, ct);
        // #1865: com parte já recebida (o sinal da encomenda), reemitir cobra o restante em vez de recusar.
        var cobrarSaldo = input.CobrarSaldo || alocado > 0m;
        var dados = new DadosCobranca(
            pedido.EmpresaId, pedido.Id, storefront?.Id ?? Guid.Empty, storefront?.TituloPublico ?? string.Empty,
            pedido.Total.Valor, pedido.Itens.ToList(), pedido.AgendadoParaEm, pedido.CriadoEm, alocado, cobrarSaldo,
            input.NaEntregaSemProvedor, storefront?.LinkWhatsAppPedidos(), pedido.LojaId);
        return await CobrarAsync(dados, input.ConversaId, input.Tentativa, ct);
    }

    private async Task<CobrancaPedidoResult> CobrarAsync(DadosCobranca dados, Guid? conversaId, int tentativa, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var valorCobrado = dados.CobrarSaldo ? dados.Total - dados.Alocado : PlanoPagamentoEncomenda.TemSinal(dados.AgendadoParaEm, dados.CriadoEm)
            ? PlanoPagamentoEncomenda.Sinal(dados.Total)
            : dados.Total;
        var existentes = await cobrancaRepository.ListarDoPedidoAsync(dados.EmpresaId, dados.PedidoId, ct);
        // #1924: reemissão sem conversa informada (POST /cobranca) herda a do pedido; sem ela, o job tratava o
        // link vencido como pedido do site e cancelava o pedido da conversa sem aviso.
        conversaId ??= existentes.Where(c => c.ConversaId is not null).OrderBy(c => c.CriadaEm).Select(c => c.ConversaId).LastOrDefault();

        if (!dados.CobrarSaldo && existentes.Any(c => c.Status == Domain.Enums.Pagamentos.StatusCobrancaPedido.Paga))
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoJaPago, "Pedido já pago.");

        var pendente = existentes.FirstOrDefault(c => c.EstaPendente);
        if (pendente is { EhPixDireto: true })
        {
            var recebedor = await RecebedorPixAsync(dados);
            var temSinal = PlanoPagamentoEncomenda.TemSinal(dados.AgendadoParaEm, dados.CriadoEm);
            if (recebedor is not null && (dados.NaEntregaSemProvedor || temSinal))
                return await PixDiretoAsync(pendente, dados, recebedor, conversaId, agora, ct);
            // A alteração da comanda sem chave na loja: o código velho cobra o valor antigo; a diferença fica na entrega.
            if (recebedor is null && dados.NaEntregaSemProvedor)
            {
                pendente.Cancelar(MotivoValorAlterado, agora);
                var naEntrega = CobrancaPedido.CriarNaEntrega(dados.EmpresaId, dados.PedidoId, dados.Total - dados.Alocado,
                    agora, conversaId ?? pendente.ConversaId);
                await cobrancaRepository.AddAsync(naEntrega, ct);
                await unitOfWork.CommitAsync();
                return CobrancaPedidoResult.De(naEntrega) with { PixDiretoSemChave = true };
            }
            // Sem chave na loja, a encomenda segue a cobrança manual de sempre (abaixo); o resto é pedido de link.
            if (recebedor is not null || !temSinal)
                throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.FormaPixDireto,
                    "O pedido está combinado para Pix na chave da loja; troque a forma para gerar link.");
        }
        if (pendente is not null && !pendente.EhOnline)
        {
            if (PlanoPagamentoEncomenda.TemSinal(dados.AgendadoParaEm, dados.CriadoEm))
            {
                var saldoManual = dados.Total - dados.Alocado;
                if (pendente.Valor == saldoManual) return CobrancaPedidoResult.De(pendente, reutilizada: true);
                pendente.Cancelar(MotivoValorAlterado, agora);
                var manual = CobrancaPedido.CriarNaEntrega(dados.EmpresaId, dados.PedidoId,
                    saldoManual, agora, conversaId ?? pendente.ConversaId);
                await cobrancaRepository.AddAsync(manual, ct);
                await unitOfWork.CommitAsync();
                return CobrancaPedidoResult.De(manual);
            }
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.FormaNaEntrega,
                "O pedido está combinado para pagar na entrega; troque a forma para gerar link.");
        }
        string? preferenciaSubstituida = null;
        if (pendente is not null && pendente.Venceu(agora))
            pendente.Expirar(agora);
        else if (pendente is not null && pendente.Valor != valorCobrado)
        {
            // #1291: item editado em AguardandoPagamento; o link antigo cobraria o valor velho.
            pendente.Cancelar(MotivoValorAlterado, agora);
            // #1717-8: quem fecha a transação expira a preferência no provedor depois do commit.
            preferenciaSubstituida = pendente.ReferenciaExterna;
        }
        else if (pendente is not null)
            return CobrancaPedidoResult.De(pendente, reutilizada: true);

        var expiraEm = agora.Add(Validade);
        var command = new CriarPreferenceCommand(
            PedidoId: dados.PedidoId,
            StorefrontId: dados.StorefrontId,
            StorefrontNome: dados.StorefrontNome,
            ValorTotal: valorCobrado,
            Items: valorCobrado == dados.Total
                ? dados.Itens.Where(i => i.PrecoUnitario > 0m).Select(ItemDaPreferencia).ToList()
                : [new PreferenceItemCommand(await TituloDaParcelaAsync(dados, conversaId, ct), 1, valorCobrado)],
            ExpiraEm: expiraEm,
            IdempotencyKey: $"{dados.PedidoId:N}-{existentes.Count + 1}",
            // #2228: da conversa, o checkout devolve o cliente ao WhatsApp da loja; do site, as back_urls do site.
            RetornoUrl: conversaId is null ? null : dados.RetornoWhatsApp);

        PreferenceCriadaResult preferencia;
        try
        {
            preferencia = await CriarPreferenciaAsync(command, ct);
        }
        catch (MercadoPagoIndisponivelException ex) when (dados.NaEntregaSemProvedor
            || (ex.InnerException is MercadoPagoNaoConfiguradoException
                && PlanoPagamentoEncomenda.TemSinal(dados.AgendadoParaEm, dados.CriadoEm)))
        {
            // Sem provedor configurado, a encomenda conserva a dívida e aguarda sinal externo, sem prazo de link.
            // O pedido confirmado pela atendente faz o mesmo com o provedor fora: nunca nasce sem cobrança (#1726).
            var manual = CobrancaPedido.CriarNaEntrega(dados.EmpresaId, dados.PedidoId,
                dados.Total - dados.Alocado, agora, conversaId);
            await cobrancaRepository.AddAsync(manual, ct);
            await unitOfWork.CommitAsync();
            return CobrancaPedidoResult.De(manual);
        }

        var cobranca = CobrancaPedido.CriarOnline(
            dados.EmpresaId, dados.PedidoId, valorCobrado, preferencia.PreferenceId, preferencia.InitPointUrl,
            expiraEm, tentativa, agora, conversaId, preferencia.ContaRecebedoraId);
        if (dados.CobrarSaldo) cobranca.MarcarComplementar();
        await cobrancaRepository.AddAsync(cobranca, ct);
        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Cobranca pedido criada pedidoId={PedidoId} cobrancaId={CobrancaId} tentativa={Tentativa}",
            dados.PedidoId, cobranca.Id, tentativa);
        return CobrancaPedidoResult.De(cobranca) with { PreferenciaSubstituida = preferenciaSubstituida };
    }

    /// <summary>
    /// Item da preferência. O Mercado Pago só aceita quantidade inteira: item fracionário (1,5 kg) vai como
    /// 1 unidade com o subtotal arredondado a 2 casas, e a soma da preferência fecha com o total (#1291).
    /// </summary>
    private static PreferenceItemCommand ItemDaPreferencia(PedidoItem item)
    {
        // #1991-5: pedido antigo guardou o nome do prato em minúsculas; o cliente lê como no cardápio.
        var titulo = NomeCardapio.Exibicao(item.Nome) ?? item.Nome;
        if (item.Quantidade == decimal.Truncate(item.Quantidade))
            return new PreferenceItemCommand(titulo, (int)item.Quantidade, item.PrecoUnitario);

        var subtotal = Math.Round(item.Quantidade * item.PrecoUnitario, 2, MidpointRounding.AwayFromZero);
        return new PreferenceItemCommand(titulo, 1, subtotal);
    }

    private async Task<PreferenceCriadaResult> CriarPreferenciaAsync(CriarPreferenceCommand command, CancellationToken ct)
    {
        try
        {
            using var mpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            mpCts.CancelAfter(MpTimeout);
            return await mercadoPagoClient.CriarPreferenceAsync(command, mpCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Cobranca timeout-mp pedidoId={PedidoId} timeout={Timeout}s",
                command.PedidoId, MpTimeout.TotalSeconds);
            throw new MercadoPagoIndisponivelException(
                "MercadoPago não respondeu no tempo limite. Tente novamente em instantes.");
        }
        catch (MercadoPagoNaoConfiguradoException ex)
        {
            // I3.1: "não configurado" é dito como tal, não como instabilidade do provedor.
            logger.LogWarning("Cobranca mp-nao-configurado pedidoId={PedidoId}", command.PedidoId);
            throw new MercadoPagoIndisponivelException(ex.Message, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Cobranca erro-mp pedidoId={PedidoId}", command.PedidoId);
            throw new MercadoPagoIndisponivelException("MercadoPago indisponível. Tente novamente em instantes.", ex);
        }
    }

    /// <summary>
    /// #2228: o Mercado Pago mostra "Pague por &lt;item&gt;" no checkout e na prévia do link. A parcela (sinal ou restante)
    /// leva o pedido e a loja, com o número que o cliente lê na conversa; o resumo no WhatsApp explica sinal e restante.
    /// </summary>
    private async Task<string> TituloDaParcelaAsync(DadosCobranca dados, Guid? conversaId, CancellationToken ct)
    {
        var conversa = conversaId is { } id && conversaRepository is not null
            ? await conversaRepository.ObterPorIdAsync(dados.EmpresaId, id, ct)
            : null;
        var pedido = $"Pedido {VariaveisAtendimento.NumeroDoPedido(dados.PedidoId, conversa)}";
        return string.IsNullOrWhiteSpace(dados.StorefrontNome) ? pedido : $"{pedido} · {dados.StorefrontNome}";
    }

    /// <summary>
    /// #2684: a alteração da comanda (ou a encomenda) com Pix direto pendente continua no Pix direto. Mesmo saldo e mesmo
    /// copia e cola: reaproveita. Mudou: cancela e sai outro, porque o código velho cobra o valor antigo.
    /// </summary>
    private async Task<CobrancaPedidoResult> PixDiretoAsync(CobrancaPedido pendente, DadosCobranca dados, RecebedorPix recebedor,
        Guid? conversaId, DateTime agora, CancellationToken ct)
    {
        var valor = dados.Total - dados.Alocado;
        var valorDoPix = PixDiretoDoPedido.ValorDoPix(dados.AgendadoParaEm, dados.CriadoEm, dados.Total, valor);
        if (pendente.Valor == valor && pendente.PixCopiaCola == PixDiretoDoPedido.CopiaECola(recebedor, dados.PedidoId, valorDoPix))
            return CobrancaPedidoResult.De(pendente, reutilizada: true) with { TextoPix = PixDiretoDoPedido.Texto(pendente, recebedor, valorDoPix) };
        pendente.Cancelar(MotivoValorAlterado, agora);
        var nova = CobrancaPedido.CriarPixDireto(dados.EmpresaId, dados.PedidoId, valor, recebedor, agora,
            conversaId ?? pendente.ConversaId, valorDoPix);
        await cobrancaRepository.AddAsync(nova, ct);
        await unitOfWork.CommitAsync();
        return CobrancaPedidoResult.De(nova) with { TextoPix = PixDiretoDoPedido.Texto(nova, recebedor, valorDoPix) };
    }

    private Task<RecebedorPix?> RecebedorPixAsync(DadosCobranca dados) =>
        PixDiretoDoPedido.RecebedorAsync(lojas, dados.EmpresaId, dados.LojaId);

    private sealed record DadosCobranca(
        Guid EmpresaId, Guid PedidoId, Guid StorefrontId, string StorefrontNome, decimal Total,
        IReadOnlyList<PedidoItem> Itens, DateTime? AgendadoParaEm, DateTime CriadoEm, decimal Alocado = 0m, bool CobrarSaldo = false,
        bool NaEntregaSemProvedor = false, string? RetornoWhatsApp = null, Guid? LojaId = null);
}
