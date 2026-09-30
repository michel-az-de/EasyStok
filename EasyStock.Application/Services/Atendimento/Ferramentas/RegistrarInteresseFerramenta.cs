using System.Text.Json;
using EasyStock.Application.UseCases.Campanhas.Interesse;
using EasyStock.Domain.Entities.Campanhas;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>registrar_interesse(cardapio_item_id?, descricao?)</c> (S31): o cliente quis algo que não está
/// disponível. Quando o item voltar, o console avisa a dona; o cliente não recebe nada sozinho.
/// Não grava: o commit é do turno.
/// </summary>
public sealed class RegistrarInteresseFerramenta(RegistrarInteresseItemUseCase registrar) : IFerramentaAgente
{
    public string Nome => "registrar_interesse";

    public string Descricao =>
        "Registra que o cliente quer um item que está esgotado ou fora do cardápio, para a loja lembrar de avisar " +
        "quando voltar. Passe cardapio_item_id se souber o id do item; senão descreva o item em descricao. " +
        "Não prometa data de volta.";

    public string SchemaJson =>
        """{"type":"object","properties":{"cardapio_item_id":{"type":"string","description":"Id do item do cardápio, se souber"},"descricao":{"type":"string","description":"O que o cliente quer, com as palavras dele"}},"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (contexto.Conversa.ClienteId is not { } clienteId)
            return FerramentaJson.Serializar(new { erro = "cliente_nao_identificado" });

        Guid? itemId = Guid.TryParse(FerramentaJson.LerTexto(entrada, "cardapio_item_id"), out var id) ? id : null;
        try
        {
            var interesse = await registrar.RegistrarAsync(
                new RegistrarInteresseItemCommand(contexto.EmpresaId, clienteId, itemId,
                    FerramentaJson.LerTexto(entrada, "descricao"), OrigemInteresse.Agente),
                contexto.Agora, ct);
            return FerramentaJson.Serializar(new
            {
                registrado = true,
                itemIdentificado = interesse.CardapioItemId is not null,
                descricao = interesse.Descricao
            });
        }
        catch (ClienteNaoEncontradoParaInteresseException)
        {
            return FerramentaJson.Serializar(new { erro = "cliente_nao_identificado" });
        }
        catch (UseCaseValidationException ex)
        {
            return FerramentaJson.Serializar(new { erro = "descricao_obrigatoria", detalhe = ex.Message });
        }
    }
}
