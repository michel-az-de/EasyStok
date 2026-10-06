using System.Diagnostics;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using DomainCliente = EasyStock.Domain.Entities.Cliente;
using DomainPedido = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>
/// Checkout GUEST do storefront — cliente nao autenticado (issue #680), cobrado pelo Mercado Pago (#1254).
///
/// <para>
/// Mesmo caminho do <see cref="IniciarCheckoutUseCase"/> e do pedido da conversa: o
/// <see cref="CheckoutCoreService"/> (S10) valida CEP na area de entrega, janela e bloqueios, cria o
/// pedido, reserva a vaga e deixa o pedido em <c>AguardandoPagamento</c>; o
/// <see cref="GerarCobrancaPedidoUseCase"/> (S11) cria a preferencia de 30 min e grava a
/// <c>CobrancaPedido</c>. O webhook (S32) confirma; o job expira e cancela sem pagamento.
/// </para>
///
/// <para>
/// O que e so do guest: cadastro guest isolado, sem tratar telefone informado como identidade verificada,
/// snapshot de nome e telefone no pedido, endereco (CEP e numero) nas observacoes e token de
/// acompanhamento sem login (#681). CEP fora da area (zona ou raio) e recusado como no logado: sem
/// frete cotado nao ha o que cobrar. Cliente bloqueado e recusado antes da vaga (#1291).
/// </para>
///
/// <para>
/// <strong>Ponte (#1306), temporaria:</strong> o site ainda nao manda janela. Sem <c>JanelaId</c> e
/// <c>DataEntrega</c>, o guest segue o modo antigo (#680): pedido em <c>aguardando_aprovacao_baba</c>, sem
/// vaga, sem frete e sem cobranca; a dona agenda e cobra pelo WhatsApp. So um dos dois e recusado. Sai
/// quando o site publicar o passo de janela.
/// </para>
/// </summary>
public sealed class IniciarCheckoutGuestUseCase(
    IStorefrontRepository storefrontRepository,
    CheckoutCoreService checkoutCore,
    GerarCobrancaPedidoUseCase gerarCobranca,
    IClienteStorefrontRepository clienteRepository,
    IPedidoStorefrontRepository pedidoRepository,
    IUnitOfWork unitOfWork,
    AcompanhamentoTokenService tokenService,
    AtribuicaoPedidoCampanha atribuicaoCampanha,
    ITenantContextAccessor tenantContext,
    TimeProvider timeProvider,
    ILogger<IniciarCheckoutGuestUseCase> logger)
{
    /// <summary>Validade do link, em segundos (a mesma do checkout logado).</summary>
    private static readonly int ExpiresInSeconds = (int)GerarCobrancaPedidoUseCase.Validade.TotalSeconds;

    /// <summary>E.164 BR: <c>+55</c> + DDD (2) + numero (8 ou 9 digitos).</summary>
    private static readonly Regex TelefoneE164BrRegex =
        new(@"^\+55[1-9][0-9]\d{8,9}$", RegexOptions.Compiled);

    public async Task<IniciarCheckoutGuestResult> ExecuteAsync(
        IniciarCheckoutGuestInput input,
        CancellationToken ct = default,
        Func<Guid, CancellationToken, Task>? pedidoCriado = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sw = Stopwatch.StartNew();

        // ── Validacoes iniciais ──────────────────────────────────────────
        var nome = (input.Nome ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nome) || nome.Length < 2)
            throw new RegraDeDominioVioladaException("Nome e obrigatorio (min 2 caracteres).");

        var telefoneE164 = NormalizarTelefone(input.Telefone);

        var cep = CheckoutCoreService.ValidarEntrada(
            input.Cep, input.Items?.Select(i => (i.CardapioItemId, i.Qtd)).ToList());

        if (input.JanelaId.HasValue != input.DataEntrega.HasValue)
            throw new RegraDeDominioVioladaException(
                "Informe a janela e a data de entrega juntas, ou nenhuma das duas.");

        // ── Resolver storefront ──────────────────────────────────────────
        var storefront = await storefrontRepository.GetBySlugAsync(input.Slug, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException(input.Slug);

        // ── Cadastro guest isolado; telefone informado não equivale a OTP ──
        var (cliente, clienteNovo) = await ResolverClienteAsync(storefront.EmpresaId, nome, telefoneE164, cep, ct);

        // #1291: o bloqueio vale em todos os canais (S24); nada de vaga ocupada nem pedido.
        if (cliente.Bloqueado)
            throw new ClienteBloqueadoException(cliente.Id);

        if (input.JanelaId is not { } janelaId || input.DataEntrega is not { } dataEntrega)
            return await CriarSemJanelaAsync(storefront.Id, storefront.EmpresaId, cliente, clienteNovo, nome, telefoneE164,
                cep, input, sw, ct, pedidoCriado);

        // ── Fases 1 e 2: pedido, frete cotado e vaga (S10) ───────────────
        var reservado = await checkoutCore.CriarPedidoComReservaAsync(
            new CheckoutCoreInput(
                ClienteId: cliente.Id,
                Itens: input.Items!.Select(i => new ItemPedidoCheckout(i.CardapioItemId, i.Qtd)).ToList(),
                JanelaId: janelaId,
                DataEntrega: dataEntrega,
                Cep: cep,
                Origem: OrigemPedido.StorefrontGuest,
                Slug: input.Slug,
                Observacoes: MontarObservacoes(input.Observacoes, cep, input.Numero),
                Numero: input.Numero),
            ct);

        var pedido = reservado.Pedido;
        pedido.ClienteNome = nome;          // snapshot do nome fornecido AGORA
        pedido.ClienteTelefone = telefoneE164;
        await pedidoRepository.UpdateAsync(pedido, ct);

        // #1226: mesma conversão da campanha que o pedido da conversa (S30); requisição anônima, o tenant
        // da loja liga o filtro e a RLS. Commit antes da fase 3, que pode falhar com o pedido já criado
        // (mesma ordem do checkout logado).
        tenantContext.SetCurrentTenant(storefront.EmpresaId);
        await atribuicaoCampanha.AtribuirAsync(storefront.EmpresaId, cliente.Id, pedido.Id, ct);
        await unitOfWork.CommitAsync();

        // ── Fase 3: cobranca do Mercado Pago (S11) ─────────────────────────
        if (pedidoCriado is not null) await pedidoCriado(pedido.Id, ct);
        var cobranca = await gerarCobranca.ExecuteAsync(reservado, conversaId: input.ConversaId, ct);

        var token = tokenService.Gerar(pedido.Id);
        var numeroCurto = pedido.Id.ToString("N")[..8].ToUpperInvariant();
        var frete = reservado.ItemFrete.PrecoUnitario;

        logger.LogInformation(
            "Checkout guest ok pedidoId={PedidoId} numeroCurto={Numero} storefrontId={StorefrontId} clienteNovo={Novo} frete={Frete} elapsedMs={Ms}",
            pedido.Id, numeroCurto, storefront.Id, clienteNovo, frete, sw.ElapsedMilliseconds);

        return new IniciarCheckoutGuestResult(
            pedido.Id, numeroCurto, token, frete, cobranca.LinkPagamento, ExpiresInSeconds);
    }

    /// <summary>
    /// Ponte #1306: modo antigo do guest (#680), sem vaga nem cobranca. O pedido vai para a aprovacao da dona,
    /// que agenda e cobra pelo WhatsApp; o site monta a mensagem com o numero curto.
    /// </summary>
    private async Task<IniciarCheckoutGuestResult> CriarSemJanelaAsync(
        Guid storefrontId, Guid empresaId, DomainCliente cliente, bool clienteNovo, string nome, string telefoneE164,
        string cep, IniciarCheckoutGuestInput input, Stopwatch sw, CancellationToken ct,
        Func<Guid, CancellationToken, Task>? pedidoCriado)
    {
        var itensPedidos = input.Items!.Select(i => new ItemPedidoCheckout(i.CardapioItemId, i.Qtd)).ToList();
        var cardapioItens = await checkoutCore.CarregarItensCardapioAsync(
            storefrontId, itensPedidos.Select(i => i.CardapioItemId), ct);

        var pedido = DomainPedido.Criar(empresaId: empresaId, cliente: cliente, origem: OrigemPedido.StorefrontGuest);
        pedido.ClienteNome = nome;
        pedido.ClienteTelefone = telefoneE164;
        pedido.Status = StatusPedidoMapper.AguardandoAprovacaoBaba;
        pedido.Observacoes = MontarObservacoes(input.Observacoes, cep, input.Numero);
        await pedidoRepository.AddAsync(pedido, ct);

        foreach (var item in await checkoutCore.AdicionarItensAsync(pedido, itensPedidos, cardapioItens, ct))
            pedido.Itens.Add(item);
        pedido.RecalcularTotal();
        pedido.AlteradoEm = DateTime.UtcNow;
        await pedidoRepository.UpdateAsync(pedido, ct);

        tenantContext.SetCurrentTenant(empresaId);
        await atribuicaoCampanha.AtribuirAsync(empresaId, cliente.Id, pedido.Id, ct);
        await unitOfWork.CommitAsync();

        var numeroCurto = pedido.Id.ToString("N")[..8].ToUpperInvariant();
        logger.LogInformation(
            "Checkout guest sem janela (ponte #1306) pedidoId={PedidoId} numeroCurto={Numero} storefrontId={StorefrontId} clienteNovo={Novo} elapsedMs={Ms}",
            pedido.Id, numeroCurto, storefrontId, clienteNovo, sw.ElapsedMilliseconds);

        if (pedidoCriado is not null) await pedidoCriado(pedido.Id, ct);
        return new IniciarCheckoutGuestResult(pedido.Id, numeroCurto, tokenService.Gerar(pedido.Id), FreteEstimado: null);
    }

    private async Task<(DomainCliente Cliente, bool Novo)> ResolverClienteAsync(
        Guid empresaId, string nome, string telefoneE164, string cep, CancellationToken ct)
    {
        // O bloqueio comercial continua valendo; consultar não concede identidade nem altera cadastro.
        var declarado = await clienteRepository.GetByTelefoneHashAsync(empresaId, ClienteOtp.CalcularTelefoneHash(telefoneE164), ct);
        if (declarado is { Bloqueado: true }) throw new ClienteBloqueadoException(declarado.Id);
        // O telefone informado não prova posse: o pedido usa um cadastro guest separado.
        var cliente = DomainCliente.CriarParaStorefront(empresaId,
            ClienteOtp.CalcularTelefoneHash($"guest:{Guid.NewGuid():N}"), timeProvider);
        cliente.Nome = nome;
        cliente.Telefone = telefoneE164;
        cliente.Cep = cep;
        await clienteRepository.AddAsync(cliente, ct);
        return (cliente, true);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string NormalizarTelefone(string telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
            throw new TelefoneInvalidoException();

        var span = telefone.Trim();
        var sb = new System.Text.StringBuilder(span.Length);
        var primeiro = true;
        foreach (var c in span)
        {
            if (primeiro && c == '+') sb.Append('+');
            else if (char.IsDigit(c)) sb.Append(c);
            else if (c is ' ' or '(' or ')' or '-' or '.') { }
            else throw new TelefoneInvalidoException();
            primeiro = false;
        }
        var normalizado = sb.ToString();
        if (!normalizado.StartsWith('+'))
        {
            if (normalizado.Length is 10 or 11) normalizado = "+55" + normalizado;
            else throw new TelefoneInvalidoException();
        }
        if (!TelefoneE164BrRegex.IsMatch(normalizado))
            throw new TelefoneInvalidoException();
        return normalizado;
    }

    private static string MontarObservacoes(string? obsCliente, string cepNormalizado, string? numero)
    {
        var partes = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(obsCliente))
            partes.Add(obsCliente.Trim());
        var cepFmt = cepNormalizado.Length == 8
            ? $"{cepNormalizado[..5]}-{cepNormalizado[5..]}"
            : cepNormalizado;
        var enderecoInfo = string.IsNullOrWhiteSpace(numero)
            ? $"[Guest] CEP {cepFmt}"
            : $"[Guest] CEP {cepFmt}, numero {numero!.Trim()}";
        partes.Add(enderecoInfo);
        return string.Join(" | ", partes);
    }
}
