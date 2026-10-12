using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento;

/// <summary>
/// Ferramentas de <b>proposta</b> do assistente da dona (#1445). O modelo pede a ação pelo tool use da API
/// Messages, mas nada roda no servidor: cada uso vira uma <see cref="AcaoPropostaAssistente"/> que o console
/// mostra com um botão, e só o clique da atendente executa (pelas rotas que já existem). Não são
/// <c>IFerramentaAgente</c> de propósito: o agente do WhatsApp nunca as enxerga.
/// </summary>
public static class PropostasAssistenteDona
{
    public const string EnviarCardapio = "propor_envio_cardapio";
    public const string NotaInterna = "propor_nota_interna";
    public const string Rascunho = "propor_rascunho";
    public const string AbrirTela = "abrir_tela";

    public const int TamanhoMaximoRascunho = 1000;

    private static readonly string[] Telas = ["cardapio", "comanda"];

    public static readonly IReadOnlyList<FerramentaLlm> Ferramentas =
    [
        new(EnviarCardapio,
            "Propõe mandar ao cliente desta conversa o link do cardápio da loja. Use quando a dona pedir para " +
            "mandar ou enviar o cardápio. Não envia nada: a atendente confirma na tela.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new(NotaInterna,
            "Propõe anotar uma nota interna no cadastro do cliente (o cliente nunca vê). Use quando a dona pedir " +
            "para anotar, registrar ou lembrar algo sobre o cliente. Escreva a nota em uma frase objetiva.",
            $$$"""{"type":"object","properties":{"texto":{"type":"string","maxLength":{{{ClienteNota.TextoTamanhoMaximo}}}}},"required":["texto"],"additionalProperties":false}"""),
        new(Rascunho,
            "Propõe o texto da próxima mensagem da loja para o cliente; vai para o campo de escrever e a dona " +
            "revisa e envia. Mensagem de WhatsApp de 1 a 3 frases curtas, sem floreio e sem markdown.",
            $$$"""{"type":"object","properties":{"texto":{"type":"string","maxLength":{{{TamanhoMaximoRascunho}}}}},"required":["texto"],"additionalProperties":false}"""),
        new(AbrirTela,
            "Propõe abrir na tela do console o cardápio (para montar o pedido) ou a comanda do pedido desta conversa.",
            """{"type":"object","properties":{"tela":{"type":"string","enum":["cardapio","comanda"]}},"required":["tela"],"additionalProperties":false}"""),
    ];

    /// <summary>
    /// Traduz os usos de ferramenta em ações para o console. Ferramenta desconhecida, texto vazio ou longo
    /// demais e tela fora da lista são descartados; a mesma ação repetida vale uma vez só.
    /// </summary>
    public static IReadOnlyList<AcaoPropostaAssistente> Traduzir(IEnumerable<BlocoUsoFerramentaLlm> usos)
    {
        var acoes = new List<AcaoPropostaAssistente>();
        foreach (var uso in usos)
        {
            var acao = uso.Nome switch
            {
                EnviarCardapio => new AcaoPropostaAssistente(AcaoPropostaAssistente.EnviarCardapio, null, null),
                NotaInterna => Texto(uso.Entrada, ClienteNota.TextoTamanhoMaximo) is { } nota
                    ? new AcaoPropostaAssistente(AcaoPropostaAssistente.NotaInterna, nota, null)
                    : null,
                Rascunho => Texto(uso.Entrada, TamanhoMaximoRascunho) is { } rascunho
                    ? new AcaoPropostaAssistente(AcaoPropostaAssistente.Rascunho, TextoWhatsApp.SemTravessao(rascunho), null)
                    : null,
                AbrirTela => Campo(uso.Entrada, "tela") is { } tela && Telas.Contains(tela)
                    ? new AcaoPropostaAssistente(AcaoPropostaAssistente.AbrirTela, null, tela)
                    : null,
                _ => null
            };
            if (acao is not null && !acoes.Contains(acao)) acoes.Add(acao);
        }
        return acoes;
    }

    private static string? Texto(JsonElement entrada, int maximo) =>
        Campo(entrada, "texto") is { Length: > 0 } texto && texto.Length <= maximo ? texto : null;

    private static string? Campo(JsonElement entrada, string nome) =>
        entrada.ValueKind == JsonValueKind.Object
        && entrada.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()?.Trim()
            : null;
}
