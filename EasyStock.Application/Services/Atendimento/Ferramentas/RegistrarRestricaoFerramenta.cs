using System.Text.Json;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>registrar_restricao(tag)</c> (S24): o cliente contou uma restrição ou preferência alimentar e ela
/// vira tag com <see cref="OrigemClienteTag.Agente"/>. Repetida é no-op. Altera o cliente rastreado;
/// o commit é do turno do agente.
/// </summary>
public sealed class RegistrarRestricaoFerramenta(IClienteCrmRepository crm) : IFerramentaAgente
{
    public string Nome => "registrar_restricao";

    public string Descricao =>
        "Registra no cadastro uma restrição ou preferência alimentar que o cliente contou (ex.: " +
        string.Join(", ", ClienteTag.Sugeridas.Where(t => t is not "risco")) +
        "). Use uma palavra curta; não comente com o cliente que registrou.";

    public string SchemaJson =>
        """{"type":"object","properties":{"tag":{"type":"string","description":"Restrição em poucas palavras, ex.: intolerante_lactose"}},"required":["tag"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (contexto.Conversa.ClienteId is not { } clienteId)
            return FerramentaJson.Serializar(new { erro = "cliente_nao_identificado" });

        var cliente = await crm.ObterComTagsAsync(contexto.EmpresaId, clienteId, ct);
        if (cliente is null)
            return FerramentaJson.Serializar(new { erro = "cliente_nao_identificado" });

        ClienteTag? tag;
        try
        {
            tag = cliente.AdicionarTag(FerramentaJson.LerTexto(entrada, "tag") ?? string.Empty, OrigemClienteTag.Agente, contexto.Agora);
        }
        catch (RegraDeDominioVioladaException)
        {
            return FerramentaJson.Serializar(new { erro = "tag_invalida" });
        }

        return tag is null
            ? FerramentaJson.Serializar(new { situacao = "ja_registrada" })
            : FerramentaJson.Serializar(new { situacao = "registrada", tag = tag.Tag });
    }
}
