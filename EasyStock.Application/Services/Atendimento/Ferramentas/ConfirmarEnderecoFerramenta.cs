using System.Text.Json;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary><c>confirmar_endereco</c> (S14): grava o endereço pendente como padrão do cliente da conversa.</summary>
public sealed class ConfirmarEnderecoFerramenta(ConfirmarEnderecoPendente confirmar) : IFerramentaAgente
{
    public string Nome => "confirmar_endereco";

    public string Descricao =>
        "Grava como endereço de entrega o último endereço validado por validar_endereco. Só chame depois que o " +
        "cliente confirmar o endereço.";

    public string SchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var r = await confirmar.ExecutarAsync(contexto.EmpresaId, contexto.Conversa, ct);
        return r.EnderecoId is { } id
            ? FerramentaJson.Serializar(new { enderecoId = id, endereco = r.Endereco })
            : FerramentaJson.Serializar(new { erro = r.Erro });
    }
}
