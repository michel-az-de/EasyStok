namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #3175: o código que <c>TurnoAgente.Motivo</c> guarda. Curto, estável e montado só aqui: nunca frase de log, texto do
/// modelo, valor, nome ou trecho de mensagem. Os códigos são consumidos por busca e por tela; mudar um quebra o histórico.
/// </summary>
public static class MotivoDoTurno
{
    public const string GuardaValor = "guarda:valor";
    public const string GuardaOutro = "guarda:outro";
    public const string Iteracoes = "iteracoes";
    public const string Menu = "menu";
    public const string FalhaLlm = "falha_llm";
    public const string FalhaLlmDefinitiva = "falha_llm_definitiva";
    public const string TetoSaida = "teto_saida";
    public const string PrazoTurno = "prazo_turno";
    public const string RecusaModelo = "recusa_modelo";
    public const string EscaladaModelo = "escalada_modelo";
    public const string Interrompido = "interrompido";

    // Lista fechada, uma por categoria de LogsDoAssistente.CategoriasDaGuarda (o teste falha se faltar).
    private static readonly Dictionary<string, string> Slugs = new(StringComparer.Ordinal)
    {
        ["o agente não produziu resposta"] = "guarda:sem_resposta",
        ["texto com chamada de ferramenta no lugar da resposta"] = "guarda:texto_de_ferramenta",
        ["promete encaminhamento sem acionar a equipe"] = "guarda:promete_encaminhar",
        ["afirma situação do pedido sem a ferramenta confirmar"] = "guarda:situacao_pedido",
        ["afirma pagamento ou devolução sem a ferramenta confirmar"] = "guarda:pagamento_devolucao",
        ["promete frete grátis ou desconto, que é decisão da equipe"] = "guarda:frete_desconto",
        ["afirma frete combinado sem a ferramenta confirmar"] = "guarda:frete_combinado",
        ["afirma envio sem a ferramenta confirmar"] = "guarda:envio_sem_ferramenta",
        ["link sem origem em ferramenta"] = "guarda:link_sem_origem",
        [LogsDoAssistente.CategoriaFerramentaFalhou] = "guarda:ferramenta_falhou",
    };

    public static string DoPortao(string codigoDoPortao) => "portao:" + codigoDoPortao;

    /// <param name="categoria">Saída de <see cref="LogsDoAssistente.CategoriaDaGuarda"/>.</param>
    public static string DaGuarda(string categoria) =>
        categoria == LogsDoAssistente.CategoriaValor ? GuardaValor
        : Slugs.GetValueOrDefault(categoria, GuardaOutro);

    /// <summary>
    /// #3347 (D4): o código da falha do LLM. Definitiva é a resposta do provedor com status que a nova tentativa não repete
    /// (400, 401, 402, 403, 404, 3xx e afins: chave, crédito, modelo ou pedido recusados). O resto segue <c>falha_llm</c>: o
    /// transitório (rede, prazo, 5xx, 408, 429) e o provedor indisponível (disjuntor, cota, sem chave), que não traz status.
    /// </summary>
    public static string DaFalhaDoLlm(bool definitiva) => definitiva ? FalhaLlmDefinitiva : FalhaLlm;

    /// <summary>Motivo que a geração devolve quando não chegou a uma resposta; o texto dele não é gravado.</summary>
    public static string DaFalhaDaGeracao(string motivoDaGeracao, bool falhaLlm, bool falhaLlmDefinitiva = false) =>
        falhaLlm ? DaFalhaDoLlm(falhaLlmDefinitiva)
        : motivoDaGeracao.StartsWith(GeracaoAgenteAtendimento.MotivoTetoDeSaida, StringComparison.Ordinal) ? TetoSaida
        : motivoDaGeracao.StartsWith(GeracaoAgenteAtendimento.MotivoPrazoDoTurno, StringComparison.Ordinal) ? PrazoTurno
        : motivoDaGeracao.StartsWith(GeracaoAgenteAtendimento.MotivoRecusa, StringComparison.Ordinal) ? RecusaModelo
        : Slugs.FirstOrDefault(par => motivoDaGeracao.StartsWith(par.Key, StringComparison.Ordinal)) is { Value: { } slug } ? slug
        : GuardaOutro;
}
