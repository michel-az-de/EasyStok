using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>enviar_foto_produto</c> (#2544, S72): manda a foto de capa de um item do cardápio, com nome e preço na legenda, e grava
/// a <c>Mensagem(Saida, Agente, Imagem)</c>. Só item que <c>consultar_cardapio</c> oferece (disponível e fora de validação).
/// A foto não é a resposta do turno: o texto do agente sai depois dela.
/// </summary>
public sealed class EnviarFotoProdutoFerramenta(
    IStorefrontRepository storefrontRepository,
    ListarCardapioPublicoUseCase listarCardapio,
    IWhatsAppCloudClient cloudClient,
    IConversaRepository conversaRepository,
    IConfiguration configuration) : IFerramentaAgente
{
    /// <summary>Mais que isso no mesmo turno vira despejo de fotos, não oferta.</summary>
    public const int LimitePorTurno = 3;

    public string Nome => "enviar_foto_produto";

    public string Descricao =>
        "Manda ao cliente a foto de um item do cardápio, com nome e preço na legenda. Use só depois de o cliente aceitar ver " +
        "a foto, com o id de um item de consultar_cardapio que tenha temFoto=true. Depois da foto, responda normalmente.";

    public string SchemaJson =>
        """{"type":"object","properties":{"itemId":{"type":"string","description":"id do item em consultar_cardapio"}},"required":["itemId"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (entrada.ValueKind != JsonValueKind.Object || !entrada.TryGetProperty("itemId", out var id)
            || !Guid.TryParse(id.GetString(), out var itemId))
            return Recusa("item_nao_encontrado", "Use o id de um item que consultar_cardapio devolveu.");
        if (contexto.FotosEnviadas >= LimitePorTurno)
            return Recusa("limite_do_turno", $"Já saíram {LimitePorTurno} fotos nesta resposta; pergunte qual o cliente prefere.");

        var storefront = await storefrontRepository.GetByEmpresaAsync(contexto.EmpresaId, ct);
        if (storefront is null || !storefront.Ativo) return Recusa("item_nao_encontrado", "O cardápio não está disponível.");
        var cardapio = await listarCardapio.ExecuteAsync(new ListarCardapioPublicoInput(storefront.Slug), ct);
        var item = cardapio.Itens.FirstOrDefault(i => i.Id == itemId && i.Disponivel && !i.EmValidacao);
        if (item is null) return Recusa("item_nao_encontrado", "Use o id de um item que consultar_cardapio devolveu.");

        var url = MidiaPublica.Url(item.ImagemUrl, configuration);
        if (url is null) return Recusa("sem_foto", "Este item não tem foto; descreva-o com o que consultar_cardapio devolveu.");

        var preco = FerramentaJson.FormatarReais(item.PrecoCentavos / 100m);
        var legenda = $"{item.Nome}, {preco}";
        var conversa = contexto.Conversa;
        var envio = await cloudClient.EnviarImagemAsync(conversa.ContatoIdExterno, url, legenda, ct);
        contexto.RegistrarFotoEnviada();

        if (conversa.EstaAberta) conversa.RegistrarSaida(contexto.Agora);
        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(contexto.EmpresaId, conversa.Id, AutorMensagem.Agente, contexto.Agora,
                TipoConteudoMensagem.Imagem, legenda, envio.Wamid), ct);
        await contexto.GravarSaidaAsync(ct);

        return FerramentaJson.Serializar(new { enviado = true, item = item.Nome, preco });
    }

    private static string Recusa(string motivo, string orientacao) =>
        FerramentaJson.Serializar(new { enviado = false, motivo, orientacao });
}
