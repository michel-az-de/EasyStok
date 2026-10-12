using System.Text.Json;
using EasyStock.Application.UseCases.Atendimento.Endereco;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>validar_endereco</c> (S14): o agente extrai os campos do texto livre e o backend decide área, taxa e
/// tempo. Dentro da área, o endereço fica pendente na conversa até o cliente confirmar
/// (<c>confirmar_endereco</c> ou botão <c>acao:confirmar_endereco</c>).
/// </summary>
public sealed class ValidarEnderecoFerramenta(ValidarEnderecoUseCase validarEndereco) : IFerramentaAgente
{
    public string Nome => "validar_endereco";

    public string Descricao =>
        "Valida o endereço de entrega que o cliente escreveu: completa pelo CEP e diz se está na área, com taxa e " +
        "tempo. Fora da área, responda com mensagemForaArea; se o cliente insistir, use escalar_para_dona com " +
        "motivo fora_de_area. Dentro da área, repita o endereço e peça confirmação antes de confirmar_endereco.";

    public string SchemaJson =>
        """{"type":"object","properties":{"cep":{"type":"string"},"logradouro":{"type":"string"},"numero":{"type":"string"},"complemento":{"type":"string"},"bairro":{"type":"string"},"cidade":{"type":"string"},"uf":{"type":"string"},"referencia":{"type":"string"}},"required":["cep"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        string? L(string p) => FerramentaJson.LerTexto(entrada, p);
        var r = await validarEndereco.ExecuteAsync(new ValidarEnderecoInput(
            contexto.EmpresaId, L("cep"), L("logradouro"), L("numero"), L("complemento"), L("bairro"), L("cidade"),
            L("uf"), L("referencia")), ct);

        ContextoConversaJson.Gravar(contexto.Conversa, ContextoConversaJson.EnderecoPendente,
            r.DentroDaArea ? r.EnderecoNormalizado : null);

        return FerramentaJson.Serializar(new
        {
            dentroDaArea = r.DentroDaArea,
            motivo = r.Motivo,
            mensagemForaArea = r.MensagemForaArea,
            endereco = r.EnderecoNormalizado,
            completo = r.EnderecoNormalizado.Completo,
            taxaEntrega = r.TaxaEntrega is { } t ? FerramentaJson.FormatarReais(t) : null,
            tempoEstimadoMin = r.TempoEstimadoMin
        });
    }
}
