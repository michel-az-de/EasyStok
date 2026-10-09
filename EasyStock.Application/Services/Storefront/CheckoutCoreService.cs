using EasyStock.Domain.Enums.Storefront;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using DomainPedido = EasyStock.Domain.Entities.Pedido;
using DomainPedidoItem = EasyStock.Domain.Entities.PedidoItem;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Services.Storefront;

/// <summary>Item pedido no checkout: cardápio, quantidade e observação do item (RN-20).</summary>
public sealed record ItemPedidoCheckout(Guid CardapioItemId, int Qtd, string? Observacao = null);

/// <summary>
/// Entrada do núcleo do checkout (S10). A loja vem do <see cref="Slug"/> (site) ou da
/// <see cref="EmpresaId"/> (atendimento); um dos dois é obrigatório. <see cref="Numero"/> do endereço
/// melhora o geocode do frete por raio (opcional, como na cotação).
/// </summary>
public sealed record CheckoutCoreInput(
    Guid ClienteId,
    IReadOnlyList<ItemPedidoCheckout> Itens,
    Guid JanelaId,
    DateOnly DataEntrega,
    string Cep,
    string Origem,
    string? Slug = null,
    Guid? EmpresaId = null,
    string? Observacoes = null,
    PrazoPreparoCheckout? Prazo = null,
    string? Numero = null);

/// <summary>
/// Parâmetros do prazo mínimo (S16, RN-21): com eles o núcleo recusa a janela cujo início seja antes de
/// agora + <c>CalculadoraPrazoPedido.PrazoMinimo</c> dos itens. Nulo (site) não corta.
/// </summary>
public sealed record PrazoPreparoCheckout(int TempoPreparoPadraoMinutos, int RespiroMinutos);

/// <summary>
/// Pedido em <c>AguardandoPagamento</c> com a vaga ocupada. <see cref="Itens"/> são os itens do
/// cardápio na ordem da entrada; o frete vem à parte em <see cref="ItemFrete"/>. <see cref="Total"/> é
/// o somatório sem arredondamento, o mesmo que a cobrança usa.
/// </summary>
public sealed record PedidoReservado(
    DomainPedido Pedido,
    StorefrontEntity Storefront,
    IReadOnlyList<DomainPedidoItem> Itens,
    DomainPedidoItem ItemFrete,
    decimal Total);

/// <summary>
/// Núcleo do checkout compartilhado (S10): fases 1 e 2 do ADR-0014, as mesmas para o site
/// (<c>IniciarCheckoutUseCase</c>) e para o atendimento (<c>CriarPedidoAtendimentoUseCase</c>).
///
/// <para>
/// <strong>Fase 1:</strong> cria o <c>Pedido(Rascunho)</c> com itens (snapshot do cardápio e
/// observação por item) e o item de frete. O frete é o do <see cref="CalcularFreteUseCase"/>, a mesma
/// cotação que o cliente viu (raio quando a loja tem config, senão zona; #1291).
/// </para>
///
/// <para>
/// <strong>Fase 2:</strong> <see cref="IVagaOcupadaRepository.OcuparAsync"/> (INSERT atômico com
/// advisory lock). Janela lotada cancela o rascunho e relança <see cref="JanelaSemVagasException"/>
/// com as alternativas; sucesso leva o pedido a <c>AguardandoPagamento</c>.
/// </para>
///
/// <para>
/// A cobrança (fase 3) fica com quem chama. A idempotência do site também: o que ela guarda é a
/// resposta da fase 3 (pedido e link de pagamento). Cobrança que não sai volta por
/// <see cref="DesfazerReservaAsync"/>.
/// </para>
/// </summary>
public sealed class CheckoutCoreService(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioItemRepository,
    IJanelaEntregaRepository janelaEntregaRepository,
    IBloqueioEntregaRepository bloqueioEntregaRepository,
    CalcularFreteUseCase calcularFrete,
    IVagaOcupadaRepository vagaOcupadaRepository,
    IPedidoStorefrontRepository pedidoRepository,
    IExpedienteLojaRepository expedienteLojaRepository,
    ILogger<CheckoutCoreService> logger,
    TimeProvider timeProvider)
{
    private static readonly Regex CepDigitosRegex = new(@"^\d{8}$", RegexOptions.Compiled);

    /// <summary>Motivo da recusa quando a janela começa antes de agora + prazo mínimo (S16).</summary>
    public const string JanelaAbaixoDoPrazo = "janela_abaixo_do_prazo";

    /// <summary>Motivo do cancelamento quando a primeira cobrança do pedido não sai (#1301).</summary>
    public const string MotivoMercadoPagoIndisponivel = "mercado_pago_indisponivel";

    /// <summary>
    /// Valida CEP e carrinho antes de qualquer consulta. Devolve o CEP só com dígitos.
    /// </summary>
    public static string ValidarEntrada(string? cep, IReadOnlyCollection<(Guid CardapioItemId, int Qtd)>? itens)
    {
        var cepNormalizado = NormalizarCep(cep);
        if (!CepDigitosRegex.IsMatch(cepNormalizado))
            throw new CepInvalidoException();

        if (itens is null || itens.Count == 0)
            throw new RegraDeDominioVioladaException("Carrinho vazio — informe ao menos 1 item.");

        foreach (var (cardapioItemId, qtd) in itens)
        {
            if (qtd <= 0)
                throw new RegraDeDominioVioladaException(
                    $"Quantidade inválida para item {cardapioItemId}: deve ser > 0.");
        }

        return cepNormalizado;
    }

    public async Task<PedidoReservado> CriarPedidoComReservaAsync(
        CheckoutCoreInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sw = Stopwatch.StartNew();

        var cep = ValidarEntrada(input.Cep, input.Itens?.Select(i => (i.CardapioItemId, i.Qtd)).ToList());

        // ── Resolver storefront ───────────────────────────────────────────
        var storefront = await ResolverStorefrontAsync(input, ct);

        // ── Loja fechada na mão (S40) ─────────────────────────────────────
        // Só a pausa manual recusa: o pedido é agendado (data + janela), então o horário
        // de funcionamento governa o atendimento, não o checkout.
        var expediente = await expedienteLojaRepository.GetPublicoAsync(storefront.EmpresaId, ct);
        if (expediente?.ControleManual == Domain.Enums.Storefront.ControleManualLoja.ForcarFechada)
            throw new LojaFechadaException(expediente.MensagemLojaFechada);

        // ── Cobertura e valor do frete: a mesma cotação do site (#1291) ───
        // Fora da área (zona ou raio), o CalcularFrete lança CepSemCoberturaException.
        var frete = await calcularFrete.ExecuteAsync(new CalcularFreteInput(storefront.Slug, cep, input.Numero), ct);
        var valorFrete = frete.Valor / 100m;

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
        var cardapioItens = await CarregarItensCardapioAsync(
            storefront.Id, input.Itens!.Select(i => i.CardapioItemId), ct);

        // ── Prazo mínimo (S16, RN-21) ─────────────────────────────────────
        // Revalida o corte da listagem: a janela pode ter ficado curta entre listar e criar.
        if (input.Prazo is { } prazo)
        {
            var prazoMinimo = CalculadoraPrazoPedido.PrazoMinimo(
                cardapioItens.Values.Select(ci => ci.TempoPreparoMinutos),
                prazo.TempoPreparoPadraoMinutos,
                prazo.RespiroMinutos);
            if (!CalculadoraPrazoPedido.AtendePrazo(
                    input.DataEntrega, janela.HoraInicio, timeProvider.GetUtcNow().UtcDateTime, prazoMinimo))
                throw new RegraDeDominioVioladaException(JanelaAbaixoDoPrazo);
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
            origem: input.Origem);

        // Sobrescrever campos com dados do cliente Storefront
        pedido.ClienteId = input.ClienteId;
        pedido.Status = StatusPedidoMapper.Rascunho;
        pedido.Observacoes = input.Observacoes;

        await pedidoRepository.AddAsync(pedido, ct);

        var itens = await AdicionarItensAsync(pedido, input.Itens!, cardapioItens, ct);
        var itemFrete = await AdicionarItemFreteAsync(pedido, frete.ZonaLabel, valorFrete, ct);

        // Total agregado (itens + frete) — persistir no Pedido. Os itens são inseridos
        // via AddItemAsync (DbSet) e NÃO em pedido.Itens, então RecalcularTotal() computaria
        // 0 (coleção vazia); por isso atribuímos o Total diretamente. É o mesmo somatório
        // cobrado no MercadoPago (Fase 3), reutilizado aqui.
        decimal total = input.Itens!.Sum(i => cardapioItens[i.CardapioItemId].PrecoEfetivo() * i.Qtd)
                        + valorFrete;
        pedido.Total = Dinheiro.FromDecimal(total);
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
                storefront.Id, input.DataEntrega, input.JanelaId, ct);

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

        return new PedidoReservado(pedido, storefront, itens, itemFrete, total);
    }

    /// <summary>
    /// Desfaz a reserva de <see cref="CriarPedidoComReservaAsync"/> quando a cobrança não sai (#1301): libera a
    /// vaga, cancela o pedido e grava o motivo no histórico. Sem isso o pedido ficava em
    /// <c>AguardandoPagamento</c> sem <c>CobrancaPedido</c>, fora do alcance do <c>CobrancaPedidoJob</c>, e a vaga
    /// da janela presa.
    /// </summary>
    public async Task DesfazerReservaAsync(PedidoReservado reservado, string motivo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservado);
        var pedido = reservado.Pedido;
        var statusAntigo = pedido.Status;

        // A liberação só marca a vaga; o SaveChanges do UpdateAsync grava vaga e pedido juntos.
        await vagaOcupadaRepository.LiberarPorPedidoAsync(pedido.Id, $"Pedido cancelado: {motivo}", ct);
        pedido.Cancelar();
        await pedidoRepository.UpdateAsync(pedido, ct);
        await pedidoRepository.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "cancelado",
            StatusAntigo = statusAntigo,
            StatusNovo = pedido.Status,
            Detalhes = motivo,
            UsuarioNome = "Sistema",
            Origem = "sistema",
            OcorridoEm = timeProvider.GetUtcNow().UtcDateTime,
        }, ct);

        logger.LogWarning("Checkout reserva desfeita pedidoId={PedidoId} motivo={Motivo}", pedido.Id, motivo);
    }

    /// <summary>
    /// Carrega os itens do cardápio pedidos. Item inexistente, invisível ou esgotado recusa o checkout.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, CardapioItem>> CarregarItensCardapioAsync(
        Guid storefrontId,
        IEnumerable<Guid> cardapioItemIds,
        CancellationToken ct = default)
    {
        var cardapioItens = new Dictionary<Guid, CardapioItem>();

        foreach (var itemId in cardapioItemIds.Distinct())
        {
            var ci = await cardapioItemRepository.GetByIdAsync(storefrontId, itemId, ct);
            // M1.2 (#1482): item arquivado não se vende, nem por link antigo.
            if (ci is null || !ci.Visivel || !ci.Disponivel || ci.EstaArquivado)
                throw new RegraDeDominioVioladaException(
                    $"Item de cardápio {itemId} não encontrado ou indisponível.");
            cardapioItens[itemId] = ci;
        }

        return cardapioItens;
    }

    /// <summary>
    /// Grava um <see cref="DomainPedidoItem"/> por item pedido, com o snapshot do cardápio (nome,
    /// preço efetivo, <c>CardapioItemId</c>) e a observação do item. Devolve na ordem da entrada.
    /// </summary>
    public async Task<IReadOnlyList<DomainPedidoItem>> AdicionarItensAsync(
        DomainPedido pedido,
        IEnumerable<ItemPedidoCheckout> itens,
        IReadOnlyDictionary<Guid, CardapioItem> cardapioItens,
        CancellationToken ct = default)
    {
        var criados = new List<DomainPedidoItem>();
        foreach (var inputItem in itens)
        {
            var ci = cardapioItens[inputItem.CardapioItemId];
            var precoUnit = ci.PrecoEfetivo();
            var item = new DomainPedidoItem
            {
                Id = Guid.NewGuid(),
                PedidoId = pedido.Id,
                ProdutoId = ci.ProdutoId,
                CardapioItemId = ci.Id,
                LinhaSnapshot = ci.Linha.ParaContrato(),
                Nome = ci.NomeEfetivo() ?? $"Item {ci.ProdutoId}",
                Quantidade = inputItem.Qtd,
                PrecoUnitario = precoUnit,
                Subtotal = inputItem.Qtd * precoUnit,
                Observacao = string.IsNullOrWhiteSpace(inputItem.Observacao) ? null : inputItem.Observacao.Trim(),
                CriadoEm = DateTime.UtcNow,
            };
            await pedidoRepository.AddItemAsync(item, ct);
            criados.Add(item);
        }

        return criados;
    }

    /// <summary>Grava o item de frete cotado (<c>Entrega — rótulo</c>, quantidade 1).</summary>
    public async Task<DomainPedidoItem> AdicionarItemFreteAsync(
        DomainPedido pedido,
        string rotulo,
        decimal valor,
        CancellationToken ct = default)
    {
        var itemFrete = new DomainPedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            ProdutoId = null,
            Nome = $"Entrega — {rotulo}",
            Quantidade = 1,
            PrecoUnitario = valor,
            Subtotal = valor,
            CriadoEm = DateTime.UtcNow,
        };
        await pedidoRepository.AddItemAsync(itemFrete, ct);
        return itemFrete;
    }

    public static string NormalizarCep(string? cep)
    {
        if (string.IsNullOrWhiteSpace(cep)) return string.Empty;
        var sb = new System.Text.StringBuilder(cep.Length);
        foreach (var c in cep)
            if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<StorefrontEntity> ResolverStorefrontAsync(CheckoutCoreInput input, CancellationToken ct)
    {
        StorefrontEntity? storefront;
        string referencia;
        if (input.Slug is not null)
        {
            storefront = await storefrontRepository.GetBySlugAsync(input.Slug, ct);
            referencia = input.Slug;
        }
        else if (input.EmpresaId is { } empresaId)
        {
            storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
            referencia = empresaId.ToString();
        }
        else
        {
            throw new ArgumentException("Informe o Slug ou a EmpresaId da loja.", nameof(input));
        }

        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException(referencia);

        return storefront;
    }

    private async Task<IReadOnlyList<string>> BuscarJanelasAlternativasAsync(
        Guid storefrontId,
        DateOnly dataEntrega,
        Guid janelaExcluidaId,
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
}
