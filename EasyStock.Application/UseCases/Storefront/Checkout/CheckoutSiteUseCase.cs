using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Checkout;

public sealed class CheckoutSiteUseCase(
    AcessoChatSite acessoChat, AbrirSessaoChatSiteUseCase abrirSessao,
    ConversaChatSiteService conversaChat, IConversaRepository conversas,
    IClienteStorefrontRepository clientes, IPedidoStorefrontRepository pedidos,
    CheckoutIdempotencyService idempotencia, ICheckoutIdempotencyRepository registros,
    IniciarCheckoutUseCase logado, IniciarCheckoutGuestUseCase guest,
    AcompanhamentoTokenService acompanhamento, IOperacaoEventPublisher eventos, ICobrancaPedidoRepository cobrancas,
    IUnitOfWork unitOfWork, TimeProvider relogio, ILogger<CheckoutSiteUseCase> logger)
{
    public async Task<CheckoutCriadoDto> LogadoAsync(IniciarCheckoutInput input, EnderecoCheckout? endereco,
        string? chatToken, Guid? chave, CancellationToken ct = default)
    {
        endereco = endereco?.Validar(input.Cep, input.Numero);
        var sessao = await SessaoAsync(input.Slug, chatToken, ct);
        if (sessao is null)
            return await logado.ExecuteAsync(input with { Numero = endereco?.Numero ?? input.Numero,
                Observacoes = endereco?.Snapshot(input.Observacoes) ?? input.Observacoes }, ct);
        var cliente = await clientes.GetByIdAsync(input.ClienteId, ct);
        if (cliente is null || cliente.EmpresaId != sessao.EmpresaId)
            throw new RegraDeDominioVioladaException("Cliente não pertence a esta loja.");
        if (cliente.Bloqueado) throw new ClienteBloqueadoException(cliente.Id);
        var key = ChaveEscopada(sessao, cliente.Id, chave);
        var hash = Hash(new { input.Slug, input.ClienteId, input.Items, input.JanelaId, input.DataEntrega,
            input.Cep, input.Numero, input.Observacoes, endereco });
        var anterior = await ReservarAsync(key, hash, sessao.EmpresaId, ct);
        if (anterior is not null) return anterior;
        try
        {
            var conversa = await conversaChat.ObterOuCriarAsync(sessao, cliente.Id, relogio.GetUtcNow().UtcDateTime, ct);
            var result = await logado.ExecuteAsync(input with { IdempotencyKey = null, ContentHash = null,
                ConversaId = conversa.Id, Numero = endereco?.Numero ?? input.Numero,
                Observacoes = endereco?.Snapshot(input.Observacoes) ?? input.Observacoes }, ct,
                (id, token) => CheckpointAsync(sessao, conversa, key, hash, id, token));
            await RegistrarAsync(key, hash, result.PedidoId, result.InitPointUrl, ct);
            return result;
        }
        catch (Exception ex) when (FalhaRecuperavel(ex))
        {
            await LiberarFalhaConfirmadaAsync(key, hash);
            throw;
        }
    }

    public async Task<IniciarCheckoutGuestResult> GuestAsync(IniciarCheckoutGuestInput input, EnderecoCheckout? endereco,
        string? chatToken, Guid? chave, CancellationToken ct = default)
    {
        endereco = endereco?.Validar(input.Cep, input.Numero);
        var sessao = await SessaoAsync(input.Slug, chatToken, ct);
        if (sessao is null)
            return await guest.ExecuteAsync(input with { Numero = endereco?.Numero ?? input.Numero,
                Observacoes = endereco?.Snapshot(input.Observacoes) ?? input.Observacoes }, ct);
        var key = ChaveEscopada(sessao, null, chave);
        var hash = Hash(new { input.Slug, input.Nome, input.Telefone, input.Items, input.JanelaId, input.DataEntrega,
            input.Cep, input.Numero, input.Observacoes, endereco });
        var anterior = await ReservarAsync(key, hash, sessao.EmpresaId, ct);
        if (anterior is not null)
        {
            var pedido = await pedidos.GetByIdComItensAsync(anterior.PedidoId, ct);
            if (pedido is null || pedido.EmpresaId != sessao.EmpresaId) throw new IdempotencyMismatchException();
            var frete = pedido.Itens.FirstOrDefault(i => i.Nome.StartsWith("Entrega ", StringComparison.Ordinal))?.PrecoUnitario;
            return new IniciarCheckoutGuestResult(pedido.Id, pedido.Id.ToString("N")[..8].ToUpperInvariant(),
                acompanhamento.Gerar(pedido.Id), frete, anterior.InitPointUrl == "sem-cobranca" ? null : anterior.InitPointUrl,
                anterior.InitPointUrl == "sem-cobranca" ? 0 : anterior.ExpiresIn);
        }
        try
        {
            var conversa = await conversaChat.ObterOuCriarAsync(sessao, null, relogio.GetUtcNow().UtcDateTime, ct);
            var result = await guest.ExecuteAsync(input with { ConversaId = conversa.Id, Numero = endereco?.Numero ?? input.Numero,
                Observacoes = endereco?.Snapshot(input.Observacoes) ?? input.Observacoes }, ct,
                (id, token) => CheckpointAsync(sessao, conversa, key, hash, id, token));
            await RegistrarAsync(key, hash, result.PedidoId, result.LinkPagamento ?? "sem-cobranca", ct);
            return result;
        }
        catch (Exception ex) when (FalhaRecuperavel(ex))
        {
            await LiberarFalhaConfirmadaAsync(key, hash);
            throw;
        }
    }

    private async Task<SessaoChatSite?> SessaoAsync(string slug, string? token, CancellationToken ct)
    {
        // Sem token, uma loja legada sem chat conserva seu checkout comercial. Com token, nunca há downgrade.
        if (string.IsNullOrWhiteSpace(token))
        {
            try { token = (await abrirSessao.ExecuteAsync(slug, ct)).Token; }
            catch (ChatSiteIndisponivelException) { return null; }
        }
        return await acessoChat.ResolverSessaoAsync(slug, token, relogio.GetUtcNow().UtcDateTime, ct);
    }

    public static Guid ChaveEscopada(SessaoChatSite sessao, Guid? clienteVerificadoId, Guid? chave)
    {
        chave = chave is null || chave == Guid.Empty ? Guid.NewGuid() : chave;
        var identidade = clienteVerificadoId?.ToString("N") ?? "guest";
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"checkout:{sessao.StorefrontId:N}:{identidade}:{chave:N}"))[..16]);
    }

    private static string Hash<T>(T valor) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(valor)));
    private async Task<CheckoutCriadoDto?> ReservarAsync(Guid chave, string hash, Guid empresaId, CancellationToken ct)
    {
        try { return await idempotencia.TentarReservarAsync(chave, hash, ct); }
        catch (CheckoutEmAndamentoException)
        {
            // Recupera resposta perdida depois do commit da cobrança, sem chamar o gateway novamente.
            var registro = await registros.GetByKeyHashAsync(chave, hash, ct);
            if (registro?.FaturaId is not { } pedidoId) throw;
            var cobranca = (await cobrancas.ListarDoPedidoAsync(empresaId, pedidoId, ct))
                .LastOrDefault(c => c.EmpresaId == empresaId && c.PedidoId == pedidoId && c.EstaPendente
                    && c.ExpiraEm > relogio.GetUtcNow().UtcDateTime && c.LinkPagamento is not null);
            if (cobranca is null) throw;
            await RegistrarAsync(chave, hash, pedidoId, cobranca.LinkPagamento!, ct);
            return new CheckoutCriadoDto(pedidoId, cobranca.LinkPagamento!,
                (int)(cobranca.ExpiraEm!.Value - relogio.GetUtcNow().UtcDateTime).TotalSeconds);
        }
    }

    private async Task RegistrarAsync(Guid chave, string hash, Guid pedidoId, string link, CancellationToken ct)
    {
        await idempotencia.RegistrarRespostaAsync(chave, hash, pedidoId, link, ct);
        await unitOfWork.CommitAsync();
    }

    private async Task VincularAsync(SessaoChatSite sessao, Conversa conversa, Guid pedidoId, CancellationToken ct)
    {
        var pedido = await pedidos.GetByIdComItensAsync(pedidoId, ct)
            ?? throw new InvalidOperationException("Pedido recém-criado não foi encontrado.");
        if (pedido.EmpresaId != sessao.EmpresaId || conversa.EmpresaId != sessao.EmpresaId)
            throw new InvalidOperationException("Pedido e conversa pertencem a lojas diferentes.");
        await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
        {
            if (await conversas.TravarParaPedidoAsync(sessao.EmpresaId, conversa.Id, token) == pedidoId)
            {
                await unitOfWork.CommitAsync();
                return false;
            }
            conversa.DefinirPedidoEmAndamento(pedido.Id);
            conversa.RegistrarEntrada(relogio.GetUtcNow().UtcDateTime);
            var linhas = string.Join("; ", pedido.Itens.Select(i => $"{i.Quantidade} × {i.Nome}"));
            // Nota interna: contém o snapshot informado, mas não concede identidade nem expõe cadastro ao visitante.
            var resumo = Mensagem.Saida(sessao.EmpresaId, conversa.Id, AutorMensagem.Sistema,
                relogio.GetUtcNow().UtcDateTime, TipoConteudoMensagem.Texto,
                $"Pedido do site #{pedido.Id.ToString("N")[..8].ToUpperInvariant()}: {linhas}. Total R$ {pedido.Total}. {pedido.Observacoes}");
            await conversas.AddMensagemAsync(resumo, token);
            await unitOfWork.CommitAsync();
            return true;
        }, ct);
        try
        {
            await eventos.PublicarAsync(EventosOperacao.ConversaPedidoPelaPagina, sessao.EmpresaId,
                new ConversaPedidoPelaPaginaOperacao(conversa.Id, pedidoId, pedidoId.ToString("N")[..8].ToUpperInvariant(), pedido.Total), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Pedido {PedidoId} vinculado ao chat; aviso ao console falhou.", pedidoId);
        }
    }

    private async Task CheckpointAsync(SessaoChatSite sessao, Conversa conversa, Guid chave, string hash, Guid pedidoId, CancellationToken ct)
    {
        var registro = await registros.GetByKeyHashAsync(chave, hash, ct)
            ?? throw new InvalidOperationException("Reserva idempotente não encontrada.");
        registro.VincularPedidoEmProcessamento(pedidoId);
        await registros.UpdateAsync(registro, ct);
        await VincularAsync(sessao, conversa, pedidoId, ct);
    }

    private async Task LiberarFalhaConfirmadaAsync(Guid chave, string hash)
    {
        var registro = await registros.GetByKeyHashAsync(chave, hash, CancellationToken.None);
        if (registro?.FaturaId is { } id)
        {
            var pedido = await pedidos.GetByIdAsync(id, CancellationToken.None);
            if (pedido?.Status != "cancelado") return;
        }
        await registros.RemoverSemRespostaAsync(chave, hash, CancellationToken.None);
    }

    private static bool FalhaRecuperavel(Exception ex) => ex is CepInvalidoException or CepSemCoberturaException
        or JanelaSemVagasException or LojaFechadaException or MercadoPagoIndisponivelException or RegraDeDominioVioladaException;
}
