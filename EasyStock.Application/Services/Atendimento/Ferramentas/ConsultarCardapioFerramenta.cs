using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Menu;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary><c>consultar_cardapio</c>: itens visíveis e disponíveis, com preço, porção, linha e tempo de preparo.</summary>
public sealed class ConsultarCardapioFerramenta(
    IStorefrontRepository storefrontRepository,
    ListarCardapioPublicoUseCase listarCardapio,
    IConfiguracaoAtendimentoRepository configuracaoRepository) : IFerramentaAgente
{
    public string Nome => "consultar_cardapio";

    public string Descricao =>
        "Lista os itens do cardápio disponíveis agora, com preço, porção, linha (categoria) e o tempo de preparo. " +
        "Use antes de falar de preço, disponibilidade ou prazo.";

    public string SchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var storefront = await storefrontRepository.GetByEmpresaAsync(contexto.EmpresaId, ct);
        if (storefront is null || !storefront.Ativo)
            return FerramentaJson.Serializar(new { erro = "cardapio_indisponivel" });

        var cardapio = await listarCardapio.ExecuteAsync(new ListarCardapioPublicoInput(storefront.Slug), ct);
        var configuracao = await configuracaoRepository.GetByEmpresaIdAsync(contexto.EmpresaId);
        var tempoPreparo = configuracao?.TempoPreparoPadraoMinutos
            ?? Domain.Entities.Atendimento.ConfiguracaoAtendimento.CriarPadrao(contexto.EmpresaId).TempoPreparoPadraoMinutos;

        var itens = cardapio.Itens
            .Where(i => i.Disponivel)
            .Select(i => new
            {
                id = i.Id, // S11: criar_pedido recebe o id do item do cardápio
                nome = i.Nome,
                descricao = i.Descricao,
                preco = FerramentaJson.FormatarReais(i.PrecoCentavos / 100m),
                porcao = i.PesoExibicao,
                linha = i.Categoria
            })
            .ToList();

        return FerramentaJson.Serializar(new { tempoPreparoMinutos = tempoPreparo, itens });
    }
}
