using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>enviar_cardapio_imagem</c>: envia <c>Storefront.CardapioImagemUrl</c> (S08) como imagem, com o
/// link do cardápio na legenda, e grava a <c>Mensagem(Saida, Agente)</c>.
/// </summary>
public sealed class EnviarCardapioImagemFerramenta(
    IStorefrontRepository storefrontRepository,
    SaudacaoAtendimento saudacao,
    IWhatsAppCloudClient cloudClient,
    IConversaRepository conversaRepository) : IFerramentaAgente
{
    public string Nome => "enviar_cardapio_imagem";

    public string Descricao =>
        "Envia ao cliente a imagem do cardápio com o link do site na legenda. Não precisa repetir o link depois.";

    public string SchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var link = await saudacao.ResolverLinkCardapioAsync(contexto.EmpresaId, ct);
        var storefront = await storefrontRepository.GetByEmpresaAsync(contexto.EmpresaId, ct);
        if (string.IsNullOrWhiteSpace(storefront?.CardapioImagemUrl))
            return FerramentaJson.Serializar(new { enviado = false, motivo = "sem_imagem_configurada", link });

        var conversa = contexto.Conversa;
        var envio = await cloudClient.EnviarImagemAsync(conversa.ContatoIdExterno, storefront.CardapioImagemUrl, link, ct);

        if (conversa.EstaAberta) conversa.RegistrarSaida(contexto.Agora);
        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(contexto.EmpresaId, conversa.Id, AutorMensagem.Agente, contexto.Agora,
                TipoConteudoMensagem.Imagem, link, envio.Wamid),
            ct);

        return FerramentaJson.Serializar(new { enviado = true, link });
    }
}
