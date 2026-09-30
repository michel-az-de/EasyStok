using System.Diagnostics;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using DomainCliente = EasyStock.Domain.Entities.Cliente;
using DomainPedido = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>
/// Checkout GUEST do storefront — fluxo simplificado pra cliente nao
/// autenticado (issue #680).
///
/// <para>
/// <strong>Diferencas vs <see cref="IniciarCheckoutUseCase"/>:</strong>
/// (a) sem JanelaId/DataEntrega — pedido nasce em
/// <c>aguardando_aprovacao_baba</c> e Babá agenda manualmente pelo WhatsApp;
/// (b) sem reserva de vaga (Fase 2 do ADR-0014); (c) sem MercadoPago
/// preference (Fase 3); (d) Cliente resolvido por <c>telefoneHash</c> em
/// vez de cookie de sessao; (e) frete e best-effort estimado (zonas) —
/// fora de cobertura nao bloqueia o pedido, Babá negocia depois.
/// </para>
///
/// <para>
/// Validacao do carrinho, carga do cardapio e gravacao dos itens vem do
/// <see cref="CheckoutCoreService"/> (S10), o mesmo nucleo do checkout logado.
/// </para>
/// </summary>
public sealed class IniciarCheckoutGuestUseCase(
    IStorefrontRepository storefrontRepository,
    CheckoutCoreService checkoutCore,
    IFreteZonaRepository freteZonaRepository,
    IClienteStorefrontRepository clienteRepository,
    IPedidoStorefrontRepository pedidoRepository,
    IUnitOfWork unitOfWork,
    AcompanhamentoTokenService tokenService,
    TimeProvider timeProvider,
    ILogger<IniciarCheckoutGuestUseCase> logger)
{
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
        var telefoneHash = ClienteOtp.CalcularTelefoneHash(telefoneE164);
        var cliente = await clienteRepository.GetByTelefoneHashAsync(storefront.EmpresaId, telefoneHash, ct);
        var clienteNovo = cliente is null;
        if (clienteNovo)
        {
            cliente = DomainCliente.CriarParaStorefront(storefront.EmpresaId, telefoneHash, timeProvider);
            cliente.Nome = nome;
            cliente.Telefone = telefoneE164;
            cliente.Cep = cep;
            await clienteRepository.AddAsync(cliente, ct);
        }
        else
        {
            // Idempotente: se cliente recorrente ainda nao tinha nome (telefone-only),
            // preencher agora. Se ja tem nome diferente, preservar o existente.
            if (string.IsNullOrWhiteSpace(cliente!.Nome))
                cliente.Nome = nome;
            if (string.IsNullOrWhiteSpace(cliente.Telefone))
                cliente.Telefone = telefoneE164;
            cliente.RegistrarAcessoStorefront(timeProvider);
            await clienteRepository.UpdateAsync(cliente, ct);
        }

        // ── Validar items do cardapio ────────────────────────────────────
        var cardapioItens = await checkoutCore.CarregarItensCardapioAsync(
            storefront.Id, input.Items!.Select(i => i.CardapioItemId), ct);

        // ── Frete best-effort (zonas, sem bloqueio) ──────────────────────
        decimal? freteEstimado = null;
        FreteZona? zonaMatch = null;
        try
        {
            var zonas = await freteZonaRepository.GetAtivasDoStorefrontOrdenadasAsync(storefront.Id, ct);
            zonaMatch = zonas.FirstOrDefault(z => z.CobreCep(cep));
            freteEstimado = zonaMatch?.Valor;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Frete best-effort falhou; pedido segue sem item de frete. storefrontId={StorefrontId} cep={Cep}",
                storefront.Id, cep);
        }

        // ── Criar Pedido ─────────────────────────────────────────────────
        var pedido = DomainPedido.Criar(
            empresaId: storefront.EmpresaId,
            cliente: cliente,
            origem: "storefront-guest");
        pedido.ClienteNome = nome;          // snapshot do nome fornecido AGORA
        pedido.ClienteTelefone = telefoneE164;
        pedido.Status = StatusPedidoMapper.AguardandoAprovacaoBaba;
        pedido.Observacoes = MontarObservacoes(input.Observacoes, cep, input.Numero);
        await pedidoRepository.AddAsync(pedido, ct);

        var itens = await checkoutCore.AdicionarItensAsync(
            pedido, input.Items!.Select(i => new ItemPedidoCheckout(i.CardapioItemId, i.Qtd)), cardapioItens, ct);
        foreach (var item in itens)
            pedido.Itens.Add(item);

        if (zonaMatch is not null && zonaMatch.Valor > 0m)
            pedido.Itens.Add(await checkoutCore.AdicionarItemFreteAsync(pedido, zonaMatch, ct));

        pedido.RecalcularTotal();
        pedido.AlteradoEm = DateTime.UtcNow;
        await pedidoRepository.UpdateAsync(pedido, ct);

        await unitOfWork.CommitAsync();

        var token = tokenService.Gerar(pedido.Id);
        var numeroCurto = pedido.Id.ToString("N")[..8].ToUpperInvariant();

        logger.LogInformation(
            "Checkout guest ok pedidoId={PedidoId} numeroCurto={Numero} storefrontId={StorefrontId} clienteNovo={Novo} freteEstimado={Frete} elapsedMs={Ms}",
            pedido.Id, numeroCurto, storefront.Id, clienteNovo, freteEstimado, sw.ElapsedMilliseconds);

        return new IniciarCheckoutGuestResult(pedido.Id, numeroCurto, token, freteEstimado);
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
