using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>enviar_cardapio_imagem</c>: envia <c>Storefront.CardapioImagemUrl</c> (S08) como imagem, com o
/// link do cardápio na legenda, e grava a <c>Mensagem(Saida, Agente)</c>. Desde a S48 o link leva o
/// token da conversa (<see cref="LinkCardapioConversaService"/>): o pedido que o cliente marcar no site
/// volta pronto para esta conversa.
/// </summary>
public sealed class EnviarCardapioImagemFerramenta(
    IStorefrontRepository storefrontRepository,
    LinkCardapioConversaService linkCardapio,
    IWhatsAppCloudClient cloudClient,
    IConversaRepository conversaRepository) : IFerramentaAgente
{
    public string Nome => "enviar_cardapio_imagem";

    public string Descricao =>
        "Compartilha a comanda pessoal: imagem com legenda, ou texto quando não houver imagem; pelo link cliente e atendimento acompanham seleção, envio e pagamento. " +
        "A comanda vale a conversa toda: com uma aberta, não envia de novo. Use reenviar=true só quando o cliente pedir o link outra vez ou disser que não abre.";

    public string SchemaJson =>
        """{"type":"object","properties":{"reenviar":{"type":"boolean","description":"true só quando o cliente pede o link de novo"}},"additionalProperties":false}""";

    /// <summary>Em produção saíram 3 links em 5 minutos (#2516); antes disso, quem diz que o link não abre vai para a equipe.</summary>
    public static readonly TimeSpan IntervaloMinimoReenvio = TimeSpan.FromMinutes(10);

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        // #2516: cada envio gera link novo e vence o anterior; item dito na conversa não é motivo para reenviar.
        var reenviar = entrada.ValueKind == JsonValueKind.Object
            && entrada.TryGetProperty("reenviar", out var r) && r.ValueKind == JsonValueKind.True;
        if (await ComandaEntregueAsync(contexto, ct) is { } entregue)
        {
            if (!reenviar)
                return FerramentaJson.Serializar(new
                {
                    enviado = false,
                    motivo = "comanda_ja_enviada",
                    orientacao = "O cliente já recebeu a comanda e ela continua aberta. Responda na conversa ao que ele disse; ele marca os itens no mesmo link. " +
                        "Use reenviar=true só se ele pedir o link de novo ou disser que não abre.",
                });
            if (contexto.Agora - entregue.CriadoEm < IntervaloMinimoReenvio)
                return FerramentaJson.Serializar(new
                {
                    enviado = false,
                    motivo = "comanda_recem_enviada",
                    orientacao = "A comanda saiu há poucos minutos; não reenvie. Se o cliente disse que não recebeu ou que o link não abre, use escalar_para_dona; " +
                        "senão, responda na conversa.",
                });
        }

        var link = (await linkCardapio.GerarAsync(contexto.EmpresaId, contexto.Conversa.Id, contexto.Agora, ct)).Url;
        var storefront = await storefrontRepository.GetByEmpresaAsync(contexto.EmpresaId, ct);
        var conversa = contexto.Conversa;
        var titulo = conversa.NumeroComanda > 0 ? $"Comanda #{conversa.NumeroComanda}" : "Sua comanda pessoal";
        var texto = $"{titulo}. Escolha os itens e acompanhe seu pedido e pagamento por aqui: {link}";
        var temImagem = !string.IsNullOrWhiteSpace(storefront?.CardapioImagemUrl);
        var envio = temImagem
            ? await cloudClient.EnviarImagemAsync(conversa.ContatoIdExterno, storefront!.CardapioImagemUrl!, texto, ct)
            : await cloudClient.EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct: ct);
        contexto.RegistrarEnvioVerificado();

        if (conversa.EstaAberta) conversa.RegistrarSaida(contexto.Agora);
        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(contexto.EmpresaId, conversa.Id, AutorMensagem.Agente, contexto.Agora,
                temImagem ? TipoConteudoMensagem.Imagem : TipoConteudoMensagem.Texto, texto, envio.Wamid),
            ct);
        await contexto.GravarSaidaAsync(ct);

        return FerramentaJson.Serializar(new { enviado = true, link });
    }

    /// <summary>
    /// Comanda vigente que chegou ao cliente (#2516): link gerado no inbox para copiar, ou gravado quando o envio ao WhatsApp
    /// falhou, não conta. Só a mensagem de saída que levou o token, sem falha, prova o envio.
    /// </summary>
    private async Task<LinkCardapioConversa?> ComandaEntregueAsync(ContextoTurnoAgente contexto, CancellationToken ct)
    {
        if (await linkCardapio.VigenteAsync(contexto.EmpresaId, contexto.Conversa.Id, contexto.Agora, ct) is not { } vigente)
            return null;
        var mensagens = await conversaRepository.ListarMensagensDepoisAsync(
            contexto.EmpresaId, contexto.Conversa.Id, vigente.CriadoEm.AddSeconds(-1), 500, ct);
        return mensagens.Any(m => m.Direcao == DirecaoMensagem.Saida && m.Status != StatusMensagem.Falhou
            && LinkCardapioConversaService.LevaOLink(m.Texto, vigente)) ? vigente : null;
    }
}
