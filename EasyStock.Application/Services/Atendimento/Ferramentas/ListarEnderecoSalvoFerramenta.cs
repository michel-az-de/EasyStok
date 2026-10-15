using System.Text.Json;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>listar_endereco_salvo</c> (S14, US-014): endereços do cliente da conversa. Mais de um, o agente pede para
/// escolher com botões <c>acao:escolher_endereco:&lt;id&gt;</c>.
/// </summary>
public sealed class ListarEnderecoSalvoFerramenta(IClienteRepository clienteRepository) : IFerramentaAgente
{
    public string Nome => "listar_endereco_salvo";

    public string Descricao =>
        "Lista os endereços salvos do cliente desta conversa (o padrão primeiro). Com mais de um, peça para o " +
        "cliente escolher; o escolhido passa por validar_endereco antes de confirmar.";

    public string SchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (contexto.Conversa.ClienteId is not { } clienteId)
            return FerramentaJson.Serializar(new { enderecos = Array.Empty<object>() });

        var cliente = await clienteRepository.GetByIdWithDetailsAsync(contexto.EmpresaId, clienteId);
        var enderecos = (cliente?.Enderecos ?? [])
            .OrderByDescending(e => e.Padrao)
            .ThenByDescending(e => e.AlteradoEm)
            .Select(e => new
            {
                id = e.Id, padrao = e.Padrao, cep = e.Cep, logradouro = e.Logradouro, numero = e.Numero,
                complemento = e.Complemento, bairro = e.Bairro, cidade = e.Cidade, uf = e.Estado, referencia = e.Referencia,
                botaoId = $"acao:escolher_endereco:{e.Id}"
            });

        return FerramentaJson.Serializar(new { enderecos });
    }
}
