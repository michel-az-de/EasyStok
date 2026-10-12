using System.Text.Json;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>registrar_nota(texto)</c> (S24): o agente anota para a equipe algo útil sobre o cliente. A nota é
/// interna (<see cref="ClienteNota"/>): o resultado não ecoa o texto, e esta ferramenta nunca lê notas.
/// O commit é do turno do agente.
/// </summary>
public sealed class RegistrarNotaFerramenta(IClienteCrmRepository crm) : IFerramentaAgente
{
    public const string Autor = "agente";

    public string Nome => "registrar_nota";

    public string Descricao =>
        "Anota para a equipe algo útil sobre o cliente (preferência de horário, ocasião, detalhe de entrega). " +
        "A nota é interna: nunca a repita para o cliente.";

    public string SchemaJson =>
        $$$"""{"type":"object","properties":{"texto":{"type":"string","maxLength":{{{ClienteNota.TextoTamanhoMaximo}}}}},"required":["texto"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (contexto.Conversa.ClienteId is not { } clienteId)
            return FerramentaJson.Serializar(new { erro = "cliente_nao_identificado" });

        ClienteNota nota;
        try
        {
            nota = ClienteNota.Criar(contexto.EmpresaId, clienteId, FerramentaJson.LerTexto(entrada, "texto") ?? string.Empty,
                Autor, contexto.Agora);
        }
        catch (RegraDeDominioVioladaException)
        {
            return FerramentaJson.Serializar(new { erro = "nota_invalida" });
        }

        await crm.AdicionarNotaAsync(nota, ct);
        return FerramentaJson.Serializar(new { situacao = "registrada" });
    }
}
