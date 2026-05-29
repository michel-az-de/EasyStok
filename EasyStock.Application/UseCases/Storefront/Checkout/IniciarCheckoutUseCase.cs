using System.Diagnostics;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using DomainPedido = EasyStock.Domain.Entities.Pedido;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>
/// Checkout Storefront — protocolo de 3 fases (ADR-0014).
///
/// <para>
/// <strong>Fase 1 (transação curta):</strong> Cria <c>Pedido(Rascunho)</c> com itens,
/// janela escolhida e valor total. Persiste sem reservar vaga ainda.
/// </para>
///
/// <para>
/// <strong>Fase 2 (transação curta, crítica):</strong>
/// <see cref="IVagaOcupadaRepository.OcuparAsync"/> INSERT atômico com advisory lock.
/// Falha com <see cref="JanelaSemVagasException"/> → 409 + janelas alternativas.
/// Sucesso → <c>Pedido.status = AguardandoPagamento</c>.
/// </para>
///
/// <para>
/// <strong>Fase 3 (fora de transação):</strong>
/// <see cref="IMercadoPagoClient.CriarPreferenceAsync"/> timeout 5 s.
/// Falha → Pedido fica AguardandoPagamento; background service cancela em 30 min.
/// Sucesso → retorna <c>{pedidoId, initPointUrl, expiresIn}</c>.
/// </para>
/// </summary>
public sealed class IniciarCheckoutUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioItemRepository,
    IJanelaEntregaRepository janelaEntregaRepository,
    IBloqueioEntregaRepository bloqueioEntregaRepository,
    IFreteZonaRepository freteZonaRepository,
    IVagaOcupadaRepository vagaOcupadaRepository,
    IPedidoStorefrontRepository pedidoRepository,
    CheckoutIdempotencyService idempotencyService,
    IMercadoPagoClient mercadoPagoClient,
    ILogger<IniciarCheckoutUseCase> logger)
{
    private static readonly TimeSpan MpTimeout = TimeSpan.FromSeconds(5);
    private static readonly Regex CepDigitosRegex = new(@"^\d{8}$", RegexOptions.Compiled);
    private const int ExpiresInSeconds = 1800;

    public async Task<CheckoutCriadoDto> ExecuteAsync(
        IniciarCheckoutInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sw = Stopwatch.StartNew();

        // ── Validações iniciais ──────────────────────────────────────────────

        var cep = NormalizarCep(input.Cep);
        if (!CepDigitosRegex.IsMatch(cep))
            throw new CepInvalidoException();

        if (input.Items is null || input.Items.Count == 0)
            throw new RegraDeDominioVioladaException("Carrinho vazio — informe ao menos 1 item.");

        foreach (var item in input.Items)
        {
            if (item.Qtd <= 0)
                throw new RegraDeDominioVioladaException(
                    $"Quantidade inválida para item {item.CardapioItemId}: deve ser > 0.");
        }

        // ── Idempotência (verificação antecipada) ─────────────────────────
        if (input.IdempotencyKey.HasValue && input.ContentHash is not null)
        {
            var cached = await idempotencyService.TentarReservarAsync(
                input.IdempotencyKey.Value, input.ContentHash, ct);
            if (cached is not null)
                return cached;
        }

        // ── Resolver storefront ───────────────────────────────────────────
        var storefront = await storefrontRepository.GetBySlugAsync(input.Slug, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException(input.Slug);

        // ── Validar cobertura de CEP ──────────────────────────────────────
        var zonas = await freteZonaRepository.GetAtivasDoStorefrontOrdenadasAsync(storefront.Id, ct);
        var zonaMatch = zonas.FirstOrDefault(z => z.CobreCep(cep));
        if (zonaMatch is null)
            throw new CepSemCoberturaException();

        // ── Validar janela ────────────────────────────────────────────────
        var janela = await janelaEntregaRepository.GetByIdAsync(input.JanelaId, ct);
        if (janela is null || !janela.Ativa || janela.StorefrontId != storefront.Id)
            throw new RegraDeDominioVioladaException(
                $"Janela de entrega {input.JanelaId} inválida ou inativa.");

        if (janela.DiaDaSemana != (int)input.DataEntrega.DayOfWeek)
            throw new RegraDeDominioVioladaException(
                $"Janela {input.JanelaId} não atende o dia {input.DataEntrega:ddd}.");

        var bloqueios = await bloqueioEntregaRepository.GetByStorefrontPeriodoAsync(
            storefront.Id, input.DataEntrega, input.DataEntrega, ct);

        var diaBloqueado = bloqueios.Any(b => b.JanelaEspecificaId == null);
        var janelaEspecificaBloqueada = bloqueios.Any(b => b.JanelaEspecificaId == input.JanelaId);

        if (diaBloqueado || janelaEspecificaBloqueada)
            throw new RegraDeDominioVioladaException(
                $"Data {input.DataEntrega:yyyy-MM-dd} bloqueada para entrega.");

        // ── Validar e carregar itens do cardápio ──────────────────────────
        var cardapioItemIds = input.Items.Select(i => i.CardapioItemId).Distinct().ToList();
        var cardapioItens = new Dictionary<Guid, Domain.Entities.Storefront.CardapioItem>();

        foreach (var itemId in cardapioItemIds)
        {
            var ci = await cardapioItemRepository.GetByIdAsync(storefront.Id, itemId, ct);
            if (ci is null || !ci.Visivel)
                throw new RegraDeDominioVioladaException(
                    $"Item de cardápio {itemId} não encontrado ou indisponível.");
            cardapioItens[itemId] = ci;
        }

        logger.LogInformation(
            "Checkout fase-validacao ok storefrontId={StorefrontId} clienteId={ClienteId} elapsed={Ms}ms",
            storefront.Id, input.ClienteId, sw.ElapsedMilliseconds);

        // ═══════════════════════════════════════════════════════════════════
        // FASE 1 — Criar Pedido (Rascunho) em transação separada
        // ═══════════════════════════════════════════════════════════════════

        var swFase1 = Stopwatch.StartNew();

        var pedido = DomainPedido.Criar(
            empresaId: storefront.EmpresaId,
            origem: "storefront");

        // Sobrescrever campos com dados do cliente Storefront
        pedido.ClienteId = input.ClienteId;
        pedido.Status = StatusPedidoMapper.Rascunho;
        pedido.Observacoes = input.Observacoes;

        await pedidoRepository.AddAsync(pedido, ct);

        foreach (var inputItem in input.Items)
        {
            var ci = cardapioItens[inputItem.CardapioItemId];
            var precoUnit = ci.PrecoEfetivo();
            var item = new PedidoItem
            {
                Id = Guid.NewGuid(),
                PedidoId = pedido.Id,
                ProdutoId = ci.ProdutoId,
                Nome = ci.Produto?.Nome ?? $"Item {ci.ProdutoId}",
                Quantidade = inputItem.Qtd,
                PrecoUnitario = precoUnit,
                Subtotal = inputItem.Qtd * precoUnit,
                CriadoEm = DateTime.UtcNow,
            };
            await pedidoRepository.AddItemAsync(item, ct);
        }

        // Item de frete
        var itemFrete = new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            ProdutoId = null,
            Nome = $"Entrega — {zonaMatch.Label}",
            Quantidade = 1,
            PrecoUnitario = zonaMatch.Valor,
            Subtotal = zonaMatch.Valor,
            CriadoEm = DateTime.UtcNow,
        };
        await pedidoRepository.AddItemAsync(itemFrete, ct);

        pedido.AlteradoEm = DateTime.UtcNow;
        await pedidoRepository.UpdateAsync(pedido, ct);

        logger.LogInformation(
            "Checkout fase-1 ok pedidoId={PedidoId} storefrontId={StorefrontId} elapsed={Ms}ms",
            pedido.Id, storefront.Id, swFase1.ElapsedMilliseconds);

        // ═══════════════════════════════════════════════════════════════════
        // FASE 2 — Reservar Vaga (INSERT atômico com advisory lock)
        // ═══════════════════════════════════════════════════════════════════

        var swFase2 = Stopwatch.StartNew();

        try
        {
            await vagaOcupadaRepository.OcuparAsync(
                janelaEntregaId: input.JanelaId,
                dataEntrega: input.DataEntrega,
                pedidoId: pedido.Id,
                ct: ct);
        }
        catch (JanelaSemVagasException)
        {
            // Rollback fase 1: cancela pedido (status Rascunho → Cancelado)
            pedido.Status = StatusPedidoMapper.Cancelado;
            pedido.CanceladoEm = DateTime.UtcNow;
            pedido.AlteradoEm = DateTime.UtcNow;
            await pedidoRepository.UpdateAsync(pedido, ct);

            // Busca janelas alternativas (best-effort)
            var alternativas = await BuscarJanelasAlternativasAsync(
                storefront.Id, input.DataEntrega, input.JanelaId, cep, ct);

            logger.LogWarning(
                "Checkout fase-2 janela-esgotada janelaId={JanelaId} data={Data} pedidoId={PedidoId}",
                input.JanelaId, input.DataEntrega, pedido.Id);

            throw new JanelaSemVagasException(
                $"Janela {input.JanelaId} esgotada para {input.DataEntrega:yyyy-MM-dd}. " +
                $"Alternativas: [{string.Join(", ", alternativas)}]");
        }

        pedido.Status = StatusPedidoMapper.AguardandoPagamento;
        pedido.AlteradoEm = DateTime.UtcNow;
        await pedidoRepository.UpdateAsync(pedido, ct);

        logger.LogInformation(
            "Checkout fase-2 ok pedidoId={PedidoId} janelaId={JanelaId} data={Data} elapsed={Ms}ms",
            pedido.Id, input.JanelaId, input.DataEntrega, swFase2.ElapsedMilliseconds);

        // ═══════════════════════════════════════════════════════════════════
        // FASE 3 — Criar Preference MP (fora de transação, timeout 5 s)
        // ═══════════════════════════════════════════════════════════════════

        var swFase3 = Stopwatch.StartNew();

        var preferenceItems = input.Items
            .Select(i =>
            {
                var ci = cardapioItens[i.CardapioItemId];
                return new PreferenceItemCommand(
                    ci.Produto?.Nome ?? $"Item {ci.ProdutoId}",
                    i.Qtd,
                    ci.PrecoEfetivo());
            })
            .ToList();

        if (zonaMatch.Valor > 0m)
            preferenceItems.Add(new PreferenceItemCommand($"Entrega — {zonaMatch.Label}", 1, zonaMatch.Valor));

        decimal total = input.Items.Sum(i => cardapioItens[i.CardapioItemId].PrecoEfetivo() * i.Qtd)
                        + zonaMatch.Valor;

        var command = new CriarPreferenceCommand(
            PedidoId: pedido.Id,
            StorefrontId: storefront.Id,
            StorefrontNome: storefront.TituloPublico,
            ValorTotal: total,
            Items: preferenceItems);

        PreferenceCriadaResult preferenceResult;
        try
        {
            using var mpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            mpCts.CancelAfter(MpTimeout);
            preferenceResult = await mercadoPagoClient.CriarPreferenceAsync(command, mpCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError(
                "Checkout fase-3 timeout-mp pedidoId={PedidoId} timeout={Timeout}s",
                pedido.Id, MpTimeout.TotalSeconds);
            // Pedido fica AguardandoPagamento — background service limpa em 30 min
            throw new MercadoPagoIndisponivelException(
                "MercadoPago não respondeu no tempo limite. Tente novamente em instantes.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Checkout fase-3 erro-mp pedidoId={PedidoId}", pedido.Id);
            // Idem: pedido fica AguardandoPagamento
            throw new MercadoPagoIndisponivelException(
                "MercadoPago indisponível. Tente novamente em instantes.", ex);
        }

        // Registrar resposta de idempotência
        if (input.IdempotencyKey.HasValue && input.ContentHash is not null)
        {
            await idempotencyService.RegistrarRespostaAsync(
                input.IdempotencyKey.Value, input.ContentHash,
                pedido.Id, preferenceResult.InitPointUrl, ct);
        }

        logger.LogInformation(
            "Checkout fase-3 ok pedidoId={PedidoId} storefrontId={StorefrontId} totalMs={Ms}ms",
            pedido.Id, storefront.Id, sw.ElapsedMilliseconds);

        return new CheckoutCriadoDto(pedido.Id, preferenceResult.InitPointUrl, ExpiresInSeconds);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<string>> BuscarJanelasAlternativasAsync(
        Guid storefrontId,
        DateOnly dataEntrega,
        Guid janelaExcluidaId,
        string cep,
        CancellationToken ct)
    {
        try
        {
            var janelas = await janelaEntregaRepository.GetAtivasDoStorefrontAsync(storefrontId, ct);
            var janelaIds = janelas
                .Where(j => j.Id != janelaExcluidaId)
                .Select(j => j.Id)
                .ToList();

            if (janelaIds.Count == 0) return Array.Empty<string>();

            var contagens = await vagaOcupadaRepository.ContarPorJanelaPeriodoAsync(
                janelaIds, dataEntrega, dataEntrega, ct);

            return janelas
                .Where(j => j.Id != janelaExcluidaId
                         && j.DiaDaSemana == (int)dataEntrega.DayOfWeek)
                .Select(j =>
                {
                    var ocupadas = contagens.TryGetValue((j.Id, dataEntrega), out var c) ? c : 0;
                    return (Janela: j, Restantes: j.CapacidadeMaxima - ocupadas);
                })
                .Where(x => x.Restantes > 0)
                .OrderBy(x => x.Janela.HoraInicio)
                .Take(5)
                .Select(x => $"{x.Janela.Label} ({x.Restantes} vaga(s))")
                .ToList();
        }
        catch
        {
            return Array.Empty<string>(); // best-effort
        }
    }

    private static string NormalizarCep(string? cep)
    {
        if (string.IsNullOrWhiteSpace(cep)) return string.Empty;
        var sb = new System.Text.StringBuilder(cep.Length);
        foreach (var c in cep)
            if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
