using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>enviar_resumo_pedido</c> (#2544, S73): desenha o resumo do pedido (itens, frete, total, entrega e endereço) como
/// imagem (D2), sobe com URL pública e manda com a legenda de <see cref="ResumoPedidoAtendimento"/>, que
/// <c>enviar_link_pagamento</c> procura antes de cobrar. Envio que falha não é gravado como enviado.
/// Renderizador e armazenamento são opcionais: o host sem eles recusa com orientação em vez de quebrar as ferramentas.
/// </summary>
public sealed class EnviarResumoPedidoFerramenta(
    IPedidoRepository pedidoRepository,
    IStorefrontRepository storefrontRepository,
    IConversaRepository conversaRepository,
    IWhatsAppCloudClient cloudClient,
    IConfiguration configuration,
    IRenderizadorResumoPedido? renderizador = null,
    IFileStorage? armazenamento = null) : IFerramentaAgente
{
    public string Nome => "enviar_resumo_pedido";

    public string Descricao =>
        "Manda ao cliente o resumo do pedido como imagem (itens, frete, total, entrega e endereço) para ele conferir. " +
        "Use depois de criar_pedido, com o pedidoId devolvido, e peça o ok. Com o ok, use enviar_link_pagamento.";

    public string SchemaJson =>
        """{"type":"object","properties":{"pedidoId":{"type":"string","description":"id devolvido por criar_pedido"}},"required":["pedidoId"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (entrada.ValueKind != JsonValueKind.Object
            || !Guid.TryParse(FerramentaJson.LerTexto(entrada, "pedidoId"), out var pedidoId))
            return Recusa("pedido_nao_encontrado", "Use o pedidoId que criar_pedido devolveu.");

        var pedido = await pedidoRepository.GetByIdWithDetailsAsync(contexto.EmpresaId, pedidoId);
        if (pedido is null || contexto.ClienteIdEsperado is not { } clienteId || pedido.ClienteId != clienteId)
            return Recusa("pedido_nao_encontrado", "Use o pedidoId que criar_pedido devolveu.");
        if (PedidoStateMachine.EstaFinalizado(pedido.StatusEnum))
            return Recusa("pedido_finalizado",
                "O pedido foi cancelado ou já foi entregue. Se o cliente ainda quiser, monte de novo com criar_pedido.");
        if (renderizador is null || armazenamento is null)
            return Recusa("resumo_indisponivel", "O resumo não pode sair agora; use escalar_para_dona.");

        var storefront = await storefrontRepository.GetByEmpresaAsync(contexto.EmpresaId, ct);
        var resumo = new ResumoPedidoDto(
            storefront?.TituloPublico ?? string.Empty,
            ResumoPedidoAtendimento.Codigo(pedido.Id),
            pedido.Itens.Where(i => !i.EhLinhaDeFrete)
                .Select(i => new ResumoPedidoItemDto(i.Nome, i.Quantidade, i.Subtotal, i.Observacao)).ToList(),
            pedido.Frete,
            pedido.Total.Valor,
            pedido.AgendadoParaEm is { } quando
                ? HorarioBrasil.ConverterParaBrasilia(quando).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "a combinar",
            pedido.EnderecoEntrega);

        var legenda = ResumoPedidoAtendimento.Legenda(pedido.Id);
        var conversa = contexto.Conversa;
        EnvioWhatsAppResult envio;
        try
        {
            var imagem = await renderizador.RenderizarAsync(resumo, ct);
            // Nome aleatório: a URL é pública porque o WhatsApp baixa a mídia por ela. Guid no formato D é o
            // que o /files serve em atendimento/* (PublicUploadFileProvider).
            var salvo = await armazenamento.UploadAsync(new FileUploadRequest(
                $"atendimento/resumos/{contexto.EmpresaId:N}", $"{Guid.NewGuid():D}.{imagem.Extensao}",
                imagem.ContentType, imagem.Conteudo), ct);
            var url = MidiaPublica.Url(salvo.Url, configuration)
                ?? throw new InvalidOperationException("O resumo não ficou em URL HTTPS pública.");
            envio = await cloudClient.EnviarImagemAsync(conversa.ContatoIdExterno, url, legenda, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            contexto.RegistrarFalhaDeFerramenta();
            return Recusa("falha_no_envio", "O resumo não chegou ao cliente. Não diga que mandou; use escalar_para_dona.");
        }
        contexto.RegistrarEnvio();

        if (conversa.EstaAberta) conversa.RegistrarSaida(contexto.Agora);
        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(contexto.EmpresaId, conversa.Id, AutorMensagem.Agente, contexto.Agora,
                TipoConteudoMensagem.Imagem, legenda, envio.Wamid), ct);
        await contexto.GravarSaidaAsync(ct);

        return FerramentaJson.Serializar(new
        {
            enviado = true,
            total = FerramentaJson.FormatarReais(pedido.Total.Valor),
            orientacao = "O resumo chegou ao cliente com o pedido de ok. Responda curto; o link sai com enviar_link_pagamento depois do ok.",
        });
    }

    private static string Recusa(string motivo, string orientacao) =>
        FerramentaJson.Serializar(new { enviado = false, motivo, orientacao });
}
