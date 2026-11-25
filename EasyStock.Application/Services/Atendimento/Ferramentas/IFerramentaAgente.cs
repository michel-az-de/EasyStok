using System.Text.Json;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// #2544: ferramenta que chama provedor externo lento (Mercado Pago) e grava pelo próprio use case. Confere o vínculo
/// da conversa sob lock, mas executa fora da transação, para não travar a conversa durante a rede.
/// </summary>
public interface IFerramentaForaDaTransacao;

/// <summary>
/// Ferramenta que o agente de atendimento (S06) pode chamar. Executa contra a Application, nunca
/// contra o banco direto, e devolve o texto do <c>tool_result</c> (JSON curto ou frase).
/// </summary>
public interface IFerramentaAgente
{
    string Nome { get; }
    string Descricao { get; }

    /// <summary>JSON Schema do <c>input_schema</c>.</summary>
    string SchemaJson { get; }

    Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default);
}

/// <summary>
/// Estado do turno que as ferramentas leem e alteram. A <see cref="Conversa"/> é a instância
/// rastreada do turno: o commit é do serviço, no fim, salvo a saída enviada pela ferramenta, que
/// <see cref="GravarSaidaAsync"/> grava na hora (#1791).
/// </summary>
public sealed class ContextoTurnoAgente(Guid empresaId, Conversa conversa, DateTime agora,
    Func<CancellationToken, Task>? gravarSaida = null)
{
    public Guid EmpresaId { get; } = empresaId;
    public Conversa Conversa { get; private set; } = conversa;

    /// <summary>
    /// #3340: depois de uma ferramenta que lançou, o serviço solta as entidades rastreadas e a conversa relida pelo lock é
    /// outra instância. O contexto passa a apontar para ela, a única que o commit grava.
    /// </summary>
    public void Reanexar(Conversa relida) => Conversa = relida;
    public Guid? ClienteIdEsperado { get; } = conversa.ClienteId;
    public long VinculoRevisaoEsperada { get; } = conversa.VinculoRevisao;
    public EasyStock.Domain.Enums.Atendimento.SituacaoConversa SituacaoEsperada { get; } = conversa.Situacao;
    public DateTime Agora { get; } = agora;
    public string? RespostaVerificada { get; private set; }
    public bool EnvioVerificado { get; private set; }
    public bool HouveEnvio { get; private set; }
    public bool Interrompido { get; private set; }
    public void Interromper() { Interrompido = true; LimparRespostaVerificada(); _envioAdiado = null; }

    private Func<CancellationToken, Task<bool>>? _envioAdiado;

    /// <summary>#2515: há um envio que só sai no fim do turno, depois do texto do agente (menu de opções).</summary>
    public bool TemEnvioAdiado => _envioAdiado is not null;

    /// <summary>
    /// #2515: o menu de opções sai depois do texto do agente, não no meio do laço. Uma vaga só: o último adiamento vale.
    /// Ferramenta seguinte não o apaga; conversa interrompida, escalada ou com texto barrado não o envia.
    /// </summary>
    public void AdiarEnvio(Func<CancellationToken, Task<bool>> envio) => _envioAdiado = envio;
    public void DescartarEnvioAdiado() => _envioAdiado = null;

    /// <summary>Executa o envio adiado uma vez; devolve se algo saiu ao cliente.</summary>
    public async Task<bool> EnviarAdiadoAsync(CancellationToken ct = default)
    {
        var envio = _envioAdiado;
        _envioAdiado = null;
        return envio is not null && await envio(ct);
    }

    /// <summary>Ferramenta desconhecida ou que lançou no turno: o texto livre que vier depois não sai (#1704).</summary>
    public bool FerramentaFalhou { get; private set; }

    /// <summary>Resultados das ferramentas do turno: de onde o texto livre pode tirar valor e link (#1704).</summary>
    public List<string> ResultadosDasFerramentas { get; } = [];

    /// <summary>#3340: uma ferramenta lançou exceção: o que ela fez no mundo é incerto e o turno não volta ao modelo para repeti-la.</summary>
    public bool FerramentaLancou { get; private set; }

    public void RegistrarFalhaDeFerramenta(bool lancou = false)
    {
        FerramentaFalhou = true;
        if (lancou) FerramentaLancou = true;
    }

    /// <summary>#2249: a conversa foi para a responsável porque o contato não é cliente; sai o molde de encaminhamento.</summary>
    public bool Encaminhou { get; private set; }

    public void RegistrarEncaminhamento() => Encaminhou = true;

    // O que só existe na resposta verificada, e não no JSON que volta ao modelo (a URL do link de pagamento): sobrevive à ferramenta seguinte.
    private string? _respostaDuravel;

    /// <summary>
    /// #3340 (R2I-03): cada ferramenta do turno soma o que o cliente deve receber. <paramref name="duravel"/> é o que o modelo
    /// não reproduz (o link de pagamento): a ferramenta seguinte não o apaga. O resto (consulta de pedido ou de entrega) vale
    /// só até a próxima ferramenta, para o status de uma consulta antiga não tomar o lugar do texto do modelo.
    /// </summary>
    public void RegistrarRespostaVerificada(string texto, bool duravel = false)
    {
        RespostaVerificada = Somar(RespostaVerificada, texto);
        if (duravel) _respostaDuravel = Somar(_respostaDuravel, texto);
    }

    private static string Somar(string? atual, string texto) =>
        atual is null ? texto : atual.Contains(texto, StringComparison.Ordinal) ? atual : $"{atual}\n\n{texto}";

    /// <summary>Começo de cada ferramenta: cai o que uma consulta anterior respondeu; fica o link de pagamento.</summary>
    public void DescartarRespostaDeConsulta() => RespostaVerificada = _respostaDuravel;

    public void RegistrarEnvioVerificado() => EnvioVerificado = HouveEnvio = true;

    /// <summary>
    /// #2544: a ferramenta mandou algo ao cliente que não é a resposta (a foto do produto). O turno já respondeu, mas o
    /// texto do agente ainda sai depois, pela guarda.
    /// </summary>
    public void RegistrarEnvio() => HouveEnvio = true;

    /// <summary>#2544: fotos de produto mandadas neste turno, para o limite por turno.</summary>
    public int FotosEnviadas { get; private set; }

    public void RegistrarFotoEnviada() { FotosEnviadas++; RegistrarEnvio(); }

    /// <summary>
    /// Grava já a <see cref="Mensagem"/> que a ferramenta acabou de enviar e adicionar (#1791). O status e o eco da
    /// Meta chegam enquanto o LLM ainda responde e procuram a mensagem pelo wamid: sem ela no banco, o status se
    /// perde (sem reenvio S57 nem SMS S60) e o eco vira mensagem da dona, que assume a conversa.
    /// </summary>
    public Task GravarSaidaAsync(CancellationToken ct = default) => gravarSaida?.Invoke(ct) ?? Task.CompletedTask;

    public void LimparRespostaVerificada()
    {
        RespostaVerificada = null;
        _respostaDuravel = null;
        EnvioVerificado = false;
    }
}
