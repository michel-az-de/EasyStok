using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Storefront;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <summary>Reemissão ou cobrança de um pedido já gravado (job, troca de forma, operadora).</summary>
public sealed record GerarCobrancaPedidoInput(Guid EmpresaId, Guid PedidoId, Guid? ConversaId = null, int Tentativa = 1);

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
    public static CobrancaPedidoResult De(CobrancaPedido c, bool reutilizada = false) =>
        new(c.Id, c.PedidoId, c.Provedor, c.Status.ToString(), c.LinkPagamento, c.Valor, c.ExpiraEm, c.Tentativa, reutilizada);
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
/// Falha ou timeout (5 s) do Mercado Pago: <see cref="MercadoPagoIndisponivelException"/>, nada gravado.
/// </para>
/// </summary>
public sealed class GerarCobrancaPedidoUseCase(
    IPedidoRepository pedidoRepository,
    IStorefrontRepository storefrontRepository,
    ICobrancaPedidoRepository cobrancaRepository,
    IMercadoPagoClient mercadoPagoClient,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<GerarCobrancaPedidoUseCase> logger)
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MpTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Pedido recém-criado pelo núcleo do checkout (site ou conversa).</summary>
    public Task<CobrancaPedidoResult> ExecuteAsync(PedidoReservado reservado, Guid? conversaId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservado);
        var itens = reservado.Itens.Append(reservado.ItemFrete).ToList();
        var dados = new DadosCobranca(
            reservado.Pedido.EmpresaId, reservado.Pedido.Id, reservado.Storefront.Id,
            reservado.Storefront.TituloPublico, reservado.Total, itens);
        return CobrarAsync(dados, conversaId, tentativa: 1, ct);
    }

    /// <summary>Pedido já gravado: carrega itens e frete do banco.</summary>
    public async Task<CobrancaPedidoResult> ExecuteAsync(GerarCobrancaPedidoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");

        var pedido = await pedidoRepository.GetByIdWithDetailsAsync(input.EmpresaId, input.PedidoId)
            ?? throw new CobrancaPedidoNaoEncontradoException(input.PedidoId);
        if (PedidoStateMachine.EstaFinalizado(pedido.StatusEnum))
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoFinalizado,
                "Pedido cancelado ou entregue não recebe cobrança.");

        var storefront = await storefrontRepository.GetByEmpresaAsync(input.EmpresaId, ct);
        var dados = new DadosCobranca(
            pedido.EmpresaId, pedido.Id, storefront?.Id ?? Guid.Empty, storefront?.TituloPublico ?? string.Empty,
            pedido.Total.Valor, pedido.Itens.ToList());
        return await CobrarAsync(dados, input.ConversaId, input.Tentativa, ct);
    }

    private async Task<CobrancaPedidoResult> CobrarAsync(DadosCobranca dados, Guid? conversaId, int tentativa, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var existentes = await cobrancaRepository.ListarDoPedidoAsync(dados.EmpresaId, dados.PedidoId, ct);

        if (existentes.Any(c => c.Status == Domain.Enums.Pagamentos.StatusCobrancaPedido.Paga))
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.PedidoJaPago, "Pedido já pago.");

        var pendente = existentes.FirstOrDefault(c => c.EstaPendente);
        if (pendente is not null && !pendente.EhOnline)
            throw new CobrancaPedidoConflitoException(CobrancaPedidoConflitoException.FormaNaEntrega,
                "O pedido está combinado para pagar na entrega; troque a forma para gerar link.");
        if (pendente is not null && !pendente.Venceu(agora))
            return CobrancaPedidoResult.De(pendente, reutilizada: true);
        pendente?.Expirar(agora);

        var expiraEm = agora.Add(Validade);
        var command = new CriarPreferenceCommand(
            PedidoId: dados.PedidoId,
            StorefrontId: dados.StorefrontId,
            StorefrontNome: dados.StorefrontNome,
            ValorTotal: dados.Total,
            Items: dados.Itens
                .Where(i => i.PrecoUnitario > 0m)
                .Select(i => new PreferenceItemCommand(i.Nome, (int)i.Quantidade, i.PrecoUnitario))
                .ToList(),
            ExpiraEm: expiraEm,
            IdempotencyKey: $"{dados.PedidoId:N}-{existentes.Count + 1}");

        var preferencia = await CriarPreferenciaAsync(command, ct);

        var cobranca = CobrancaPedido.CriarOnline(
            dados.EmpresaId, dados.PedidoId, dados.Total, preferencia.PreferenceId, preferencia.InitPointUrl,
            expiraEm, tentativa, agora, conversaId);
        await cobrancaRepository.AddAsync(cobranca, ct);
        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Cobranca pedido criada pedidoId={PedidoId} cobrancaId={CobrancaId} tentativa={Tentativa}",
            dados.PedidoId, cobranca.Id, tentativa);
        return CobrancaPedidoResult.De(cobranca);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Cobranca erro-mp pedidoId={PedidoId}", command.PedidoId);
            throw new MercadoPagoIndisponivelException("MercadoPago indisponível. Tente novamente em instantes.", ex);
        }
    }

    private sealed record DadosCobranca(
        Guid EmpresaId, Guid PedidoId, Guid StorefrontId, string StorefrontNome, decimal Total, IReadOnlyList<PedidoItem> Itens);
}
