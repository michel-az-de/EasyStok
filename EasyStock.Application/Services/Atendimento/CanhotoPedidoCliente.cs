using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>O fato do pedido que dispara o canhoto em imagem (#2937).</summary>
public enum GatilhoCanhoto
{
    /// <summary><c>pedido.pago</c>: o pedido ficou quitado. A imagem leva o selo "Pago".</summary>
    Pagamento = 1,

    /// <summary><c>pedido.mudou_status</c> para "Preparando": só para quem paga na entrega, ou já quitou sem receber.</summary>
    EmPreparo = 2,
}

/// <summary>
/// #2937: canhoto do pedido em imagem, mandado à cliente uma vez por pedido. Sai no pagamento, com "Pago"; pedido
/// com cobrança na entrega recebe ao entrar em preparo, sem o selo. Só dentro da janela de 24 h do WhatsApp: fora
/// dela nada sai e nada é marcado, e a cliente segue com o aviso em texto.
///
/// <para>
/// O envio é direto, sem a fila de notificações. Por isso o marco <see cref="Marco"/> é gravado e commitado logo
/// depois do envio que deu certo: a falha de outro handler do mesmo evento não repete a imagem. Falha de envio
/// não sobe ao outbox, para não repetir os outros avisos do evento.
/// </para>
/// </summary>
public sealed class CanhotoPedidoCliente(
    IPedidoRepository pedidos,
    IClienteRepository clientes,
    IConversaRepository conversas,
    ICobrancaPedidoRepository cobrancas,
    IStorefrontRepository storefronts,
    IConfiguracaoAtendimentoRepository configuracoes,
    PoliticaEnvioCliente politica,
    IWhatsAppCloudClient cloudClient,
    IConfiguration configuration,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ITenantContextAccessor tenant,
    ILogger<CanhotoPedidoCliente> logger,
    IRenderizadorResumoPedido? renderizador = null,
    IFileStorage? armazenamento = null,
    EasyStock.Application.UseCases.Atendimento.Entregas.ConfiguracaoLogistica? logistica = null)
{
    /// <summary>Tipo do <see cref="PedidoEvento"/> que registra o envio; um por pedido.</summary>
    public const string Marco = "canhoto_enviado";

    /// <summary>Não começa como o resumo do Assistente: não conta como o resumo que pede o ok antes do link.</summary>
    public static string Legenda(Guid pedidoId) => $"Canhoto do pedido {ResumoPedidoAtendimento.Codigo(pedidoId)}.";

    /// <summary>Envia o canhoto se couber. Retorna se a imagem saiu. Nunca lança por regra ou por falha do canal.</summary>
    public async Task<bool> EnviarAsync(
        Guid empresaId, Guid pedidoId, GatilhoCanhoto gatilho, Guid? conversaId, CancellationToken ct)
    {
        if (renderizador is null || armazenamento is null) return false;
        if (logistica is not null && logistica.EmpresaDemonstracaoId != Guid.Empty && empresaId == logistica.EmpresaDemonstracaoId)
            return false;
        // O dispatcher de integração roda sem claim JWT: sem o tenant, o filtro global e a RLS zeram a busca.
        tenant.SetCurrentTenant(empresaId);

        var pedido = await pedidos.GetByIdWithDetailsAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId || PedidoStateMachine.EstaFinalizado(pedido.StatusEnum)) return false;
        if (await pedidos.ExisteEventoAsync(empresaId, pedidoId, Marco, ct)) return false;
        if (await PagoAsync(pedido, gatilho, ct) is not { } pago) return false;

        var cliente = pedido.ClienteId is { } clienteId ? await clientes.GetByIdAsync(empresaId, clienteId) : null;
        if (cliente is null || cliente.EmpresaId != empresaId || cliente.Bloqueado) return false;
        // O aviso de preparo respeita quem desligou os avisos (S24); a confirmação do pagamento sai sempre.
        if (gatilho == GatilhoCanhoto.EmPreparo && !cliente.AvisosStatusAtivos) return false;
        if (!await politica.PodeEnviarAsync(empresaId, cliente.Id, CanalConversa.WhatsApp, FinalidadeContato.Transacional, ct))
            return false;

        var agora = relogio.GetUtcNow().UtcDateTime;
        var conversa = await ConversaAsync(empresaId, pedido, cliente.Id, conversaId, ct);
        if (conversa is null || conversa.Canal != CanalConversa.WhatsApp || !conversa.DentroDaJanela(agora))
        {
            logger.LogInformation("Canhoto do pedido {PedidoId} não enviado: sem conversa de WhatsApp na janela", pedidoId);
            return false;
        }

        var storefront = await storefronts.GetByEmpresaAsync(empresaId, ct);
        var fecho = (await configuracoes.GetOrDefaultAsync(empresaId)).FraseFechoCanhoto;
        var resumo = ResumoPedidoAtendimento.Montar(pedido, storefront?.TituloPublico, pago, fecho);
        var legenda = Legenda(pedido.Id);

        EnvioWhatsAppResult envio;
        try
        {
            var imagem = await renderizador.RenderizarAsync(resumo, ct);
            // Nome aleatório em atendimento/*: a URL é pública porque o WhatsApp baixa a mídia por ela.
            var salvo = await armazenamento.UploadAsync(new FileUploadRequest(
                $"atendimento/resumos/{empresaId:N}", $"{Guid.NewGuid():D}.{imagem.Extensao}",
                imagem.ContentType, imagem.Conteudo), ct);
            var url = MidiaPublica.Url(salvo.Url, configuration)
                ?? throw new InvalidOperationException("O canhoto não ficou em URL HTTPS pública.");
            envio = await cloudClient.EnviarImagemAsync(conversa.ContatoIdExterno, url, legenda, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Canhoto do pedido {PedidoId} não chegou à cliente", pedidoId);
            return false;
        }

        try
        {
            await pedidos.AddEventoAsync(new PedidoEvento
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = Marco, OcorridoEm = agora, Origem = "sistema",
                Detalhes = pago ? "Canhoto em imagem enviado à cliente, com o selo de pago." : "Canhoto em imagem enviado à cliente.",
            });
            if (conversa.EstaAberta) conversa.RegistrarSaida(agora);
            await conversas.AddMensagemAsync(
                Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, agora, TipoConteudoMensagem.Imagem, legenda, envio.Wamid), ct);
            await unitOfWork.CommitAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A imagem já saiu: lançar faria o outbox repetir o evento e mandar a segunda.
            logger.LogError(ex, "Canhoto do pedido {PedidoId} enviado, mas o marco não foi gravado", pedidoId);
            unitOfWork.DescartarAlteracoesPendentes();
        }
        return true;
    }

    /// <summary>Nulo quando o gatilho não vale para o pedido; senão, se a imagem leva o selo "Pago".</summary>
    private async Task<bool?> PagoAsync(Pedido pedido, GatilhoCanhoto gatilho, CancellationToken ct)
    {
        if (gatilho == GatilhoCanhoto.Pagamento) return true;
        if (await pedidos.ExisteEventoAsync(pedido.EmpresaId, pedido.Id, QuitacaoPedido.Marco, ct)) return true;
        var lista = await cobrancas.ListarDoPedidoAsync(pedido.EmpresaId, pedido.Id, ct);
        return lista.Any(c => c.EstaPendente && c.Provedor == CobrancaPedido.ProvedorNaEntrega) ? false : null;
    }

    private async Task<Conversa?> ConversaAsync(Guid empresaId, Pedido pedido, Guid clienteId, Guid? conversaId, CancellationToken ct)
    {
        var id = conversaId ?? await conversas.ObterIdPorPedidoAsync(empresaId, pedido.Id, ct);
        var conversa = id is { } achado ? await conversas.ObterPorIdAsync(empresaId, achado, ct) : null;
        // Pedido lançado pela equipe não tem conversa própria: vale a conversa aberta da cliente no WhatsApp.
        return conversa is { Canal: CanalConversa.WhatsApp }
            ? conversa
            : await conversas.ObterAbertaPorClienteNoCanalAsync(empresaId, clienteId, CanalConversa.WhatsApp, ct) ?? conversa;
    }
}
