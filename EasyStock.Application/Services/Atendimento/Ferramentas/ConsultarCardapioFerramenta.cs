using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Menu;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary><c>consultar_cardapio</c>: itens visíveis e disponíveis, com preço, porção, linha, tempo de preparo e a ficha (alérgenos e ingredientes) quando preenchida.</summary>
public sealed class ConsultarCardapioFerramenta(
    IStorefrontRepository storefrontRepository,
    ListarCardapioPublicoUseCase listarCardapio,
    ICardapioItemRepository cardapioItemRepository,
    IConfiguracaoAtendimentoRepository configuracaoRepository) : IFerramentaAgente
{
    public string Nome => "consultar_cardapio";

    public string Descricao =>
        "Lista os itens do cardápio disponíveis agora, com preço, porção, linha (categoria), o tempo de preparo e, " +
        "quando a ficha estiver preenchida, alérgenos e ingredientes. " +
        "Use antes de falar de preço, disponibilidade, prazo, ingredientes ou alergia.";

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

        // #1314: a ficha não está no DTO público (contrato da vitrine); vem da entidade, pelo mesmo id.
        var fichas = (await cardapioItemRepository.GetVisiveisDoStorefrontAsync(storefront.Id, ct))
            .ToDictionary(i => i.Id);

        var itens = cardapio.Itens
            // M1.2 (#1482, RN-15): item novo em validação não é oferecido até a dona confirmar.
            .Where(i => i.Disponivel && !i.EmValidacao)
            .Select(i =>
            {
                var ficha = fichas.GetValueOrDefault(i.Id);
                return new
                {
                    id = i.Id, // S11: criar_pedido recebe o id do item do cardápio
                    nome = i.Nome,
                    descricao = i.Descricao,
                    preco = FerramentaJson.FormatarReais(i.PrecoCentavos / 100m),
                    porcao = i.PesoExibicao,
                    linha = i.Categoria,
                    alergenos = Preenchido(ficha?.Alergenos),
                    ingredientes = Preenchido(ficha?.Ingredientes)
                };
            })
            .ToList();

        return FerramentaJson.Serializar(new { tempoPreparoMinutos = tempoPreparo, itens });
    }

    /// <summary>Campo vazio sai do JSON: o agente não pode ler ficha em branco como "não tem alérgeno".</summary>
    private static string? Preenchido(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor;
}
