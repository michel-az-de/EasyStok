using System.Text.Json;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>Uma ferramenta pedida pelo modelo no turno e se ela deu certo.</summary>
public sealed record FerramentaDoTurno(string Nome, bool Ok);

/// <summary>
/// S67 (#2534, Onda 14): registro de um turno do Assistente que chamou o modelo. Guarda só números, nomes de
/// ferramenta e ids: o que o cliente perguntou e o que o robô respondeu vêm de <c>atendimento_mensagens</c> pelos
/// ids, então o registro não carrega texto nem telefone.
/// </summary>
public sealed class TurnoAgente
{
    public const int ModeloTamanhoMaximo = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid ConversaId { get; private set; }
    public Guid? MensagemClienteId { get; private set; }
    public Guid? MensagemRespostaId { get; private set; }
    public string Modelo { get; private set; } = null!;
    public int TokensEntrada { get; private set; }
    public int TokensSaida { get; private set; }
    public int TokensCacheLido { get; private set; }

    /// <summary>Em dólar, pela tabela de preço versionada; nulo quando o modelo não tem preço.</summary>
    public decimal? CustoEstimado { get; private set; }

    /// <summary>JSON <c>[{"nome":"consultar_pedido","ok":true}]</c>, na ordem em que o modelo pediu.</summary>
    public string FerramentasJson { get; private set; } = "[]";

    /// <summary>Chamadas ao modelo no turno.</summary>
    public int Passos { get; private set; }

    public DesfechoTurnoAgente Desfecho { get; private set; }
    public int DuracaoMs { get; private set; }
    public DateTime CriadoEm { get; private set; }

    public int TotalTokens => TokensEntrada + TokensSaida + TokensCacheLido;

    public IReadOnlyList<FerramentaDoTurno> Ferramentas =>
        JsonSerializer.Deserialize<List<FerramentaDoTurno>>(FerramentasJson, Json) ?? [];

    private TurnoAgente() { }

    public static TurnoAgente Registrar(
        Guid empresaId,
        Guid conversaId,
        Guid? mensagemClienteId,
        Guid? mensagemRespostaId,
        string? modelo,
        int tokensEntrada,
        int tokensSaida,
        int tokensCacheLido,
        decimal? custoEstimado,
        IEnumerable<FerramentaDoTurno> ferramentas,
        int passos,
        DesfechoTurnoAgente desfecho,
        int duracaoMs,
        DateTime agora)
    {
        if (empresaId == Guid.Empty || conversaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Empresa e conversa são obrigatórias no registro do turno.");
        if (tokensEntrada < 0 || tokensSaida < 0 || tokensCacheLido < 0 || passos < 0 || duracaoMs < 0)
            throw new RegraDeDominioVioladaException("Tokens, passos e duração do turno não podem ser negativos.");
        if (!Enum.IsDefined(desfecho))
            throw new RegraDeDominioVioladaException("Desfecho do turno inválido.");

        var nomeModelo = string.IsNullOrWhiteSpace(modelo) ? "desconhecido" : modelo.Trim();
        if (nomeModelo.Length > ModeloTamanhoMaximo) nomeModelo = nomeModelo[..ModeloTamanhoMaximo];

        return new TurnoAgente
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ConversaId = conversaId,
            MensagemClienteId = mensagemClienteId,
            MensagemRespostaId = mensagemRespostaId,
            Modelo = nomeModelo,
            TokensEntrada = tokensEntrada,
            TokensSaida = tokensSaida,
            TokensCacheLido = tokensCacheLido,
            CustoEstimado = custoEstimado,
            FerramentasJson = JsonSerializer.Serialize(ferramentas.ToList(), Json),
            Passos = passos,
            Desfecho = desfecho,
            DuracaoMs = duracaoMs,
            CriadoEm = Datas.Utc(agora),
        };
    }
}
