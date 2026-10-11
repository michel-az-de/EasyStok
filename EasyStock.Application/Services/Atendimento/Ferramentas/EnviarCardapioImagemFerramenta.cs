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
        "Não precisa repetir o link depois.";

    public string SchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var link = (await linkCardapio.GerarAsync(contexto.EmpresaId, contexto.Conversa.Id, contexto.Agora, ct)).Url;
        var storefront = await storefrontRepository.GetByEmpresaAsync(contexto.EmpresaId, ct);
        var conversa = contexto.Conversa;
        var titulo = conversa.NumeroComanda > 0 ? $"Comanda #{conversa.NumeroComanda}" : "Sua comanda pessoal";
        var texto = $"{titulo}. Escolha os itens e acompanhe seu pedido e pagamento por aqui: {link}";
        var temImagem = !string.IsNullOrWhiteSpace(storefront?.CardapioImagemUrl);
        var envio = temImagem
            ? await cloudClient.EnviarImagemAsync(conversa.ContatoIdExterno, storefront!.CardapioImagemUrl!, texto, ct)
            : await cloudClient.EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct: ct);

        if (conversa.EstaAberta) conversa.RegistrarSaida(contexto.Agora);
        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(contexto.EmpresaId, conversa.Id, AutorMensagem.Agente, contexto.Agora,
                temImagem ? TipoConteudoMensagem.Imagem : TipoConteudoMensagem.Texto, texto, envio.Wamid),
            ct);

        return FerramentaJson.Serializar(new { enviado = true, link });
    }
}
