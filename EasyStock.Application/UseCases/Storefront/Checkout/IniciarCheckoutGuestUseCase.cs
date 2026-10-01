using System.Diagnostics;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Entities.Storefront;
using DomainCliente = EasyStock.Domain.Entities.Cliente;

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
/// O que e so do guest: Cliente resolvido por <c>telefoneHash</c> em vez de cookie de sessao,
/// snapshot de nome e telefone no pedido, endereco (CEP e numero) nas observacoes e token de
/// acompanhamento sem login (#681). CEP fora da area e recusado como no logado: sem zona nao ha
/// frete para cobrar.
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
        CancellationToken ct = default)
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

        // ── Resolver storefront ──────────────────────────────────────────
        var storefront = await storefrontRepository.GetBySlugAsync(input.Slug, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException(input.Slug);

        // ── Resolver/criar Cliente por telefoneHash ──────────────────────
        var (cliente, clienteNovo) = await ResolverClienteAsync(storefront.EmpresaId, nome, telefoneE164, cep, ct);

        // ── Fases 1 e 2: pedido, frete da zona e vaga (S10) ──────────────
        var reservado = await checkoutCore.CriarPedidoComReservaAsync(
            new CheckoutCoreInput(
                ClienteId: cliente.Id,
                Itens: input.Items!.Select(i => new ItemPedidoCheckout(i.CardapioItemId, i.Qtd)).ToList(),
                JanelaId: input.JanelaId,
                DataEntrega: input.DataEntrega,
                Cep: cep,
                Origem: OrigemPedido.StorefrontGuest,
                Slug: input.Slug,
                Observacoes: MontarObservacoes(input.Observacoes, cep, input.Numero)),
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
        var cobranca = await gerarCobranca.ExecuteAsync(reservado, conversaId: null, ct);

        var token = tokenService.Gerar(pedido.Id);
        var numeroCurto = pedido.Id.ToString("N")[..8].ToUpperInvariant();
        var frete = reservado.ItemFrete.PrecoUnitario;

        logger.LogInformation(
            "Checkout guest ok pedidoId={PedidoId} numeroCurto={Numero} storefrontId={StorefrontId} clienteNovo={Novo} frete={Frete} elapsedMs={Ms}",
            pedido.Id, numeroCurto, storefront.Id, clienteNovo, frete, sw.ElapsedMilliseconds);

        return new IniciarCheckoutGuestResult(
            pedido.Id, numeroCurto, token, frete, cobranca.LinkPagamento, ExpiresInSeconds);
    }

    private async Task<(DomainCliente Cliente, bool Novo)> ResolverClienteAsync(
        Guid empresaId, string nome, string telefoneE164, string cep, CancellationToken ct)
    {
        var telefoneHash = ClienteOtp.CalcularTelefoneHash(telefoneE164);
        var cliente = await clienteRepository.GetByTelefoneHashAsync(empresaId, telefoneHash, ct);
        if (cliente is null)
        {
            cliente = DomainCliente.CriarParaStorefront(empresaId, telefoneHash, timeProvider);
            cliente.Nome = nome;
            cliente.Telefone = telefoneE164;
            cliente.Cep = cep;
            await clienteRepository.AddAsync(cliente, ct);
            return (cliente, true);
        }

        // Idempotente: se cliente recorrente ainda nao tinha nome (telefone-only),
        // preencher agora. Se ja tem nome diferente, preservar o existente.
        if (string.IsNullOrWhiteSpace(cliente.Nome))
            cliente.Nome = nome;
        if (string.IsNullOrWhiteSpace(cliente.Telefone))
            cliente.Telefone = telefoneE164;
        cliente.RegistrarAcessoStorefront(timeProvider);
        await clienteRepository.UpdateAsync(cliente, ct);
        return (cliente, false);
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
