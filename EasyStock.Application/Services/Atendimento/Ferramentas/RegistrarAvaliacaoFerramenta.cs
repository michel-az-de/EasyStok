using System.Text.Json;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>registrar_avaliacao(pedido_id, resultado, comentario?)</c> (S26): quando o cliente responde ao pedido
/// de avaliação em texto livre ("adorei!") em vez do botão, o agente grava a mesma avaliação de um toque.
/// Negativa passa a conversa para a dona (TODO(S27): abrir <c>Ocorrencia</c>). O commit é do turno.
/// </summary>
public sealed class RegistrarAvaliacaoFerramenta(RegistrarAvaliacaoSimplesUseCase registrar, IEscaladorConversa escalador) : IFerramentaAgente
{
    public string Nome => "registrar_avaliacao";

    public string Descricao =>
        "Registra a avaliação do cliente sobre um pedido entregue, quando ele responde em texto em vez do botão. " +
        "Use resultado \"positiva\" ou \"negativa\"; na negativa, diga que a Tatiana vai falar com ele.";

    public string SchemaJson =>
        """{"type":"object","properties":{"pedido_id":{"type":"string"},"resultado":{"type":"string","enum":["positiva","negativa"]},"comentario":{"type":"string","maxLength":500}},"required":["pedido_id","resultado"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (contexto.Conversa.ClienteId is not { } clienteId)
            return FerramentaJson.Serializar(new { erro = "cliente_nao_identificado" });

        if (!Guid.TryParse(FerramentaJson.LerTexto(entrada, "pedido_id"), out var pedidoId))
            return FerramentaJson.Serializar(new { erro = "pedido_invalido" });

        if (!RegistrarAvaliacaoSimplesUseCase.TryInterpretarResultado(FerramentaJson.LerTexto(entrada, "resultado"), out var resultado))
            return FerramentaJson.Serializar(new { erro = "resultado_invalido" });

        try
        {
            await registrar.ExecutarAsync(contexto.EmpresaId, clienteId, pedidoId, resultado,
                FerramentaJson.LerTexto(entrada, "comentario"), contexto.Agora, ct);
        }
        catch (AvaliacaoDuplicadaException)
        {
            return FerramentaJson.Serializar(new { erro = "avaliacao_duplicada", mensagem = AvaliacaoAcaoBotao.RespostaDuplicada });
        }
        catch (StorefrontNaoEncontradoException)
        {
            return FerramentaJson.Serializar(new { erro = "pedido_nao_encontrado" });
        }
        catch (PedidoNaoElegivelParaAvaliacaoException)
        {
            return FerramentaJson.Serializar(new { erro = "pedido_nao_entregue" });
        }
        catch (RegraDeDominioVioladaException)
        {
            return FerramentaJson.Serializar(new { erro = "avaliacao_invalida" });
        }

        if (resultado == ResultadoAvaliacao.Negativa)
            await escalador.EscalarAsync(contexto.EmpresaId, contexto.Conversa, AvaliacaoAcaoBotao.MotivoEscalada, contexto.Agora, ct);

        return FerramentaJson.Serializar(new { situacao = "registrada", resultado = resultado.ToString().ToLowerInvariant() });
    }
}
