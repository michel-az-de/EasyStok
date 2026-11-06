using System.Text.Json;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// #2249: <c>encaminhar_para_responsavel(assunto)</c>. Quem não é cliente (fornecedor, orçamento, banco, vaga, assunto
/// pessoal) vai para a responsável e o robô para de atender a pessoa em todas as conversas, até a equipe devolver.
/// O contato recebe o molde de encaminhamento da loja, nunca texto livre.
/// </summary>
public sealed class EncaminharParaResponsavelFerramenta(IEscaladorConversa escalador, IClienteRepository clientes) : IFerramentaAgente
{
    public const string PrefixoMotivo = "encaminhado à responsável: ";

    public string Nome => "encaminhar_para_responsavel";

    public string Descricao =>
        "Para quem não é cliente nem está comprando: fornecedor, orçamento ou proposta para a loja, banco, contador, " +
        "vaga de emprego, parceria ou assunto pessoal com a dona. Passa a conversa para a responsável e o atendimento " +
        "automático para de responder esta pessoa até a equipe devolver. O sistema avisa a pessoa: não escreva outra mensagem.";

    public string SchemaJson =>
        """{"type":"object","properties":{"assunto":{"type":"string","description":"Do que se trata, em uma frase, para a responsável"}},"required":["assunto"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var assunto = FerramentaJson.LerTexto(entrada, "assunto") ?? "contato fora do atendimento";
        if (contexto.Conversa.ClienteId is { } clienteId
            && await clientes.GetByIdAsync(contexto.EmpresaId, clienteId) is { } cliente)
            cliente.PausarAtendimentoAutomatico(assunto, contexto.Agora);

        contexto.RegistrarEncaminhamento();
        await escalador.EscalarAsync(contexto.EmpresaId, contexto.Conversa, PrefixoMotivo + assunto, contexto.Agora, ct);
        return FerramentaJson.Serializar(new { encaminhado = true });
    }
}
