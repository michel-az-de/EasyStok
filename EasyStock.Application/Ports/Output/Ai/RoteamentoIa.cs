using System.Text.Json.Serialization;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;

namespace EasyStock.Application.Ports.Output.Ai;

/// <summary>
/// #3064 (S2b de #2948): a chamada nem saiu (disjuntor aberto, cliente desligado, loja sem rota, sem chave, cota
/// estourada ou tenant divergente). Herda <see cref="HttpRequestException"/> para quem já trata falha de LLM seguir igual;
/// o registro de uso não grava Erro por ela (achado A1 da S2a).
/// </summary>
public sealed class ProvedorIaIndisponivelException(string mensagem, Exception? inner = null)
    : HttpRequestException(HttpRequestError.ConnectionError, mensagem, inner);

/// <summary>#3064: o tenant da requisição atual (claim, webhook ou job); vazio quando nenhum foi definido.</summary>
public interface ITenantAtual
{
    Guid EmpresaId { get; }
}

/// <summary>
/// #3064: provedor e modelo da loja para uma chamada, e o gasto do mês por provedor. A implementação lê com o tenant da
/// própria loja (RLS), num escopo próprio.
/// </summary>
public interface IRotasIa
{
    /// <summary>
    /// O modelo ligado da loja cujo <c>id_externo</c> casa com <paramref name="modeloPedido"/>; sem casamento, o padrão.
    /// Nulo quando o modelo escolhido não existe ou o provedor dele está desligado.
    /// </summary>
    Task<RotaIa?> ObterAsync(Guid empresaId, string? modeloPedido, CancellationToken ct = default);

    /// <summary>Soma de <c>custo_usd</c> do mês corrente (de Brasília) da loja nesse provedor.</summary>
    Task<decimal> GastoDoMesAsync(Guid empresaId, Guid provedorId, DateTime agoraUtc, CancellationToken ct = default);
}

/// <summary>
/// <paramref name="CaiuNoPadrao"/>: o modelo pedido não casou com nenhum modelo ligado da loja.
/// <paramref name="PrazoSegundos"/>: prazo da chamada ao provedor (#3304); nulo segue o global do <c>.env</c>.
/// </summary>
public sealed record RotaIa(
    Guid ProvedorId,
    string BaseUrl,
    AutenticacaoProvedorIa Autenticacao,
    decimal? OrcamentoMensalUsd,
    ModeloIa Modelo,
    bool CaiuNoPadrao,
    int? PrazoSegundos = null)
{
    public string ProviderKey => $"ia:{ProvedorId}";
}

/// <summary>#3064: fala com um provedor no formato Anthropic Messages, com URL, autenticação e chave da loja.</summary>
public interface IClienteProvedorIa
{
    Task<RespostaLlm> EnviarAsync(DestinoIa destino, RequisicaoLlm requisicao, CancellationToken ct = default);
}

/// <summary>
/// Para onde a chamada vai. O <see cref="ToString"/> nunca mostra a chave.
/// <paramref name="SaidaMax"/>: o máximo de tokens de saída que o modelo aceita (#3304). Toda chamada sai com ele, então depois de
/// <c>max_tokens</c> não há nova tentativa com teto maior (#3313). <paramref name="PrazoSegundos"/>: prazo de CADA chamada ao provedor;
/// nulo em ambos segue o global do <c>.env</c>.
/// O prazo do turno (180 s por padrão; <c>PrazoTurnoSegundos</c> 0 desliga) só corta a espera pelo modelo: a ferramenta já iniciada e o
/// envio ao cliente terminam, então o turno pode passar dele. Desligado, nada fora do laço limita o turno (a fila não reserva o item com
/// prazo) e o limite são as voltas (<c>MaximoIteracoes</c>, 6) com até 3 chamadas cada: a chamada, a nova tentativa depois de falha
/// transitória (repete a chamada após 300 ms) e, sem <c>SaidaMax</c>, a repetição depois de <c>max_tokens</c>; cada uma leva até o prazo
/// do provedor. No pior caso, com 300 s e sem <c>SaidaMax</c>, são 6 voltas de 900 s (90 min só de espera pelo provedor; com
/// <c>SaidaMax</c>, 60 min). Somam a isso as ferramentas, a leitura da chave no cofre antes de cada chamada (flag, rota e gasto do mês vêm do banco só na primeira do escopo), as esperas de 300 ms
/// (até 1,8 s no turno) e a gravação de cada chamada.
/// </summary>
public sealed record DestinoIa(Guid ProvedorId, string BaseUrl, AutenticacaoProvedorIa Autenticacao, string Chave, string Modelo,
    int? SaidaMax = null, int? PrazoSegundos = null)
{
    public override string ToString() => $"DestinoIa {{ ProvedorId = {ProvedorId}, BaseUrl = {BaseUrl}, Modelo = {Modelo} }}";
}

/// <summary>Payload cifrado no cofre (<c>CategoriaIntegracao.Ia</c>, <c>ia:{provedorId}</c>).</summary>
public sealed record ChaveProvedorIa([property: JsonPropertyName("apiKey")] string ApiKey)
{
    public override string ToString() => "ChaveProvedorIa { *** }";
}
