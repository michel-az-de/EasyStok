using System.Globalization;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.Storefront.Pedidos;

/// <summary>
/// Lista os pedidos do cliente storefront autenticado dentro do tenant do
/// <see cref="Domain.Entities.Storefront.Storefront"/>, ordenados por
/// <c>CriadoEm DESC</c>. Consumido pelo endpoint <c>GET /api/storefront/{slug}/pedidos</c>.
///
/// <para>
/// <strong>Filtros aplicados:</strong>
/// </para>
/// <list type="bullet">
///   <item><c>EmpresaId = storefront.EmpresaId</c></item>
///   <item><c>ClienteId = input.ClienteId</c> (do cookie de sessão)</item>
///   <item><c>Origem = "storefront"</c></item>
///   <item><c>Status != "rascunho"</c> (pedido em rascunho ainda não é visível pro cliente)</item>
/// </list>
///
/// <para>
/// <strong>Anti-N+1:</strong> 3 queries totais — pedidos+itens (1 com Include),
/// avaliações por PedidoIds (1), vagas+janelas por PedidoIds (1).
/// </para>
///
/// <para>
/// <strong>Limitações conhecidas (MVP):</strong>
/// </para>
/// <list type="bullet">
///   <item><c>Endereco</c> sempre null — o checkout atual não snapshota endereço de entrega no Pedido (ver TASK-EZ-PEDIDOS-002).</item>
///   <item><c>InitPointUrl</c> sempre null — preference MP não é persistida (ver TASK-EZ-PEDIDOS-003 pra reabertura de pagamento).</item>
/// </list>
/// </summary>
public sealed class ListarPedidosClienteUseCase(
    IStorefrontRepository storefrontRepository,
    IPedidoStorefrontRepository pedidoRepository,
    IPedidoAvaliacaoRepository avaliacaoRepository,
    IVagaOcupadaRepository vagaOcupadaRepository,
    ILogger<ListarPedidosClienteUseCase> logger)
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;
    private const int MinLimit = 1;
    private const string FreteNomePrefix = "Entrega";

    public async Task<ListarPedidosClienteResult> ExecuteAsync(
        ListarPedidosClienteInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.ClienteId == Guid.Empty)
            throw new ArgumentException("ClienteId obrigatório.", nameof(input));

        var limit = ClampLimit(input.Limit);

        // ── Resolver storefront ───────────────────────────────────────────
        var storefront = await storefrontRepository.GetBySlugAsync(input.Slug, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException(input.Slug);

        // ── Carregar pedidos do cliente (+ itens) ─────────────────────────
        var pedidos = await pedidoRepository.ListarPorClienteAsync(
            storefront.EmpresaId, input.ClienteId, limit, ct);

        if (pedidos.Count == 0)
        {
            logger.LogInformation(
                "ListarPedidosCliente vazio empresaId={EmpresaId} clienteId={ClienteId}",
                storefront.EmpresaId, input.ClienteId);
            return new ListarPedidosClienteResult(Array.Empty<PedidoStorefrontDto>());
        }

        var pedidoIds = pedidos.Select(p => p.Id).ToArray();

        // ── Lookups paralelos: avaliações + vagas+janelas (anti-N+1) ───
        var avaliacoesTask = avaliacaoRepository.GetByPedidoIdsAsync(pedidoIds, ct);
        var vagasTask = vagaOcupadaRepository.GetByPedidoIdsAsync(pedidoIds, ct);
        await Task.WhenAll(avaliacoesTask, vagasTask);
        var avaliacoes = await avaliacoesTask;
        var vagas = await vagasTask;

        // ── Montar DTOs ──────────────────────────────────────────────────
        var resultado = new List<PedidoStorefrontDto>(pedidos.Count);
        foreach (var pedido in pedidos)
        {
            avaliacoes.TryGetValue(pedido.Id, out var avaliacao);
            vagas.TryGetValue(pedido.Id, out var vagaInfo);

            resultado.Add(MapearParaDto(pedido, avaliacao, vagaInfo));
        }

        logger.LogInformation(
            "ListarPedidosCliente ok empresaId={EmpresaId} clienteId={ClienteId} total={Total} limit={Limit}",
            storefront.EmpresaId, input.ClienteId, resultado.Count, limit);

        return new ListarPedidosClienteResult(resultado);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static int ClampLimit(int? raw)
    {
        if (raw is null || raw.Value <= 0) return DefaultLimit;
        return Math.Min(raw.Value, MaxLimit);
    }

    internal static PedidoStorefrontDto MapearParaDto(
        PedidoEntity pedido,
        PedidoAvaliacao? avaliacao,
        (VagaOcupada Vaga, JanelaEntrega Janela) vagaInfo)
    {
        // Item de frete = ProdutoId null + Nome começa com "Entrega" (convenção do
        // IniciarCheckoutUseCase). Snapshot, então pode haver pedido legado sem item de frete.
        var itemFrete = pedido.Itens.FirstOrDefault(i =>
            i.ProdutoId is null
            && i.Nome.StartsWith(FreteNomePrefix, StringComparison.OrdinalIgnoreCase));

        var itensProdutos = pedido.Itens
            .Where(i => i != itemFrete)
            .Select(i => new PedidoStorefrontItemDto(
                Nome: i.Nome,
                Qtd: (int)Math.Round(i.Quantidade, MidpointRounding.AwayFromZero),
                PrecoCentavos: ToCentavos(i.PrecoUnitario)))
            .ToList();

        var freteCentavos = itemFrete is null
            ? 0L
            : ToCentavos(itemFrete.Subtotal);

        var subtotalCentavos = itensProdutos.Sum(i => i.PrecoCentavos * i.Qtd);
        var totalCentavos = ToCentavos((decimal)pedido.Total);

        PedidoStorefrontJanelaDto? janelaDto = null;
        if (vagaInfo.Janela is not null)
        {
            janelaDto = new PedidoStorefrontJanelaDto(
                Data: vagaInfo.Vaga.DataEntrega,
                HoraInicio: vagaInfo.Janela.HoraInicio.ToString("HH:mm", CultureInfo.InvariantCulture),
                HoraFim: vagaInfo.Janela.HoraFim.ToString("HH:mm", CultureInfo.InvariantCulture),
                Label: vagaInfo.Janela.Label);
        }

        PedidoStorefrontAvaliacaoDto? avaliacaoDto = null;
        if (avaliacao is not null && !avaliacao.OcultadoEm.HasValue)
        {
            avaliacaoDto = new PedidoStorefrontAvaliacaoDto(
                Estrelas: avaliacao.Estrelas,
                Comentario: avaliacao.Comentario);
        }

        // Motivo de cancelamento: prioriza mensagem ao cliente (texto humano),
        // fallback pra MotivoRecusa canônico ("estoque_insuficiente" etc).
        string? motivoCancelamento = null;
        if (StatusPedidoMapper.TryParse(pedido.Status, out var statusEnum)
            && statusEnum == StatusPedido.Cancelado)
        {
            motivoCancelamento = !string.IsNullOrWhiteSpace(pedido.MensagemRecusaCliente)
                ? pedido.MensagemRecusaCliente
                : pedido.MotivoRecusa;
        }

        return new PedidoStorefrontDto(
            PedidoId: pedido.Id,
            CriadoEm: DateTime.SpecifyKind(pedido.CriadoEm, DateTimeKind.Utc),
            Status: StatusToContract(pedido.Status),
            Itens: itensProdutos,
            SubtotalCentavos: subtotalCentavos,
            FreteCentavos: freteCentavos,
            TotalCentavos: totalCentavos,
            JanelaEntrega: janelaDto,
            Endereco: null,         // MVP: ver TASK-EZ-PEDIDOS-002
            Avaliacao: avaliacaoDto,
            InitPointUrl: null,     // MVP: ver TASK-EZ-PEDIDOS-003
            MotivoCancelamento: motivoCancelamento);
    }

    /// <summary>
    /// Converte o status interno (string lowercase canônica do <see cref="StatusPedidoMapper"/>)
    /// para o PascalCase que o frontend casa via map (ver fixture
    /// <c>casa-da-baba/src/api/fixtures/meus-pedidos-sucesso.json</c>).
    /// </summary>
    internal static string StatusToContract(string statusInterno)
    {
        // Pedido em rascunho não chega aqui (filter do repo), mas defensivo:
        return statusInterno switch
        {
            StatusPedidoMapper.AguardandoPagamento => "AguardandoPagamento",
            StatusPedidoMapper.AguardandoAprovacaoBaba => "AguardandoAprovacaoBaba",
            StatusPedidoMapper.AprovadoBaba => "AprovadoBaba",
            StatusPedidoMapper.Aguardando => "EmPreparo",       // ERP "aguardando" mapeia pra "EmPreparo" no contrato cliente
            StatusPedidoMapper.Preparando => "EmPreparo",
            StatusPedidoMapper.Pronto => "SaiuParaEntrega",
            StatusPedidoMapper.Entregue => "Entregue",
            StatusPedidoMapper.Cancelado => "Cancelado",
            StatusPedidoMapper.Rascunho => "Rascunho",          // não deveria aparecer
            _ => statusInterno, // unknown — passa raw pra não esconder bug
        };
    }

    /// <summary>Conversão decimal (BRL) → long (centavos). Round half-away (mesma regra do checkout).</summary>
    internal static long ToCentavos(decimal valor) =>
        (long)Math.Round(valor * 100m, MidpointRounding.AwayFromZero);
}
