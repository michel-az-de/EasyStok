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

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        // #2516: cada envio gera link novo e vence o anterior; item dito na conversa não é motivo para reenviar.
        var reenviar = entrada.ValueKind == JsonValueKind.Object
            && entrada.TryGetProperty("reenviar", out var r) && r.ValueKind == JsonValueKind.True;
        if (!reenviar && await linkCardapio.VigenteAsync(contexto.EmpresaId, contexto.Conversa.Id, contexto.Agora, ct) is not null)
            return FerramentaJson.Serializar(new
            {
                enviado = false,
                motivo = "comanda_ja_enviada",
                orientacao = "O cliente já tem a comanda aberta. Responda na conversa ao que ele disse; ele marca os itens no mesmo link.",
            });

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
}
