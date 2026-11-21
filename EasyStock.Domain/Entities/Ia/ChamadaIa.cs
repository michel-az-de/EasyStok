using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Ia;

namespace EasyStock.Domain.Entities.Ia;

/// <summary>
/// #2957 (S1 de #2948): uma chamada a um modelo de IA, só inserção (D4). Guarda números e ids, nunca texto.
/// Provedor e modelo ficam nulos enquanto a chamada sai pelo <c>.env</c> (S2a), antes do roteador por loja.
/// A cota em US$ soma <see cref="CustoUsd"/> por empresa, provedor e mês.
/// </summary>
public sealed class ChamadaIa
{
    public const int ModeloTamanhoMaximo = 100;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid? ProvedorId { get; private set; }
    public Guid? ModeloId { get; private set; }

    /// <summary>Modelo que a API diz ter respondido.</summary>
    public string ModeloRespondido { get; private set; } = null!;
    public FinalidadeIa Finalidade { get; private set; }
    public Guid? TurnoAgenteId { get; private set; }
    public int TokensEntrada { get; private set; }
    public int TokensSaida { get; private set; }
    public int TokensCacheLido { get; private set; }
    public int TokensCacheEscrito { get; private set; }

    /// <summary>Nulo quando o modelo não tem preço.</summary>
    public decimal? CustoUsd { get; private set; }
    public int DuracaoMs { get; private set; }
    public DesfechoChamadaIa Desfecho { get; private set; }
    public DateTime CriadoEm { get; private set; }

    private ChamadaIa() { }

    public static ChamadaIa Registrar(Guid empresaId, Guid? provedorId, Guid? modeloId, string? modeloRespondido,
        FinalidadeIa finalidade, Guid? turnoAgenteId, int tokensEntrada, int tokensSaida, int tokensCacheLido,
        int tokensCacheEscrito, decimal? custoUsd, int duracaoMs, DesfechoChamadaIa desfecho, DateTime agora)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Empresa é obrigatória na chamada de IA.");
        if (tokensEntrada < 0 || tokensSaida < 0 || tokensCacheLido < 0 || tokensCacheEscrito < 0 || duracaoMs < 0)
            throw new RegraDeDominioVioladaException("Tokens e duração da chamada não podem ser negativos.");
        // #2967: arredonda como o numeric(12,6) arredondaria; o resto do limite vale como no modelo.
        custoUsd = custoUsd is { } c ? decimal.Round(c, LimitesIa.CasasValor) : null;
        LimitesIa.ValidarValor(custoUsd, "Custo da chamada");
        if (!Enum.IsDefined(finalidade) || !Enum.IsDefined(desfecho))
            throw new RegraDeDominioVioladaException("Finalidade ou desfecho da chamada inválido.");

        var nome = string.IsNullOrWhiteSpace(modeloRespondido) ? "desconhecido" : modeloRespondido.Trim();
        if (nome.Length > ModeloTamanhoMaximo) nome = nome[..ModeloTamanhoMaximo];

        return new ChamadaIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProvedorId = provedorId,
            ModeloId = modeloId,
            ModeloRespondido = nome,
            Finalidade = finalidade,
            TurnoAgenteId = turnoAgenteId,
            TokensEntrada = tokensEntrada,
            TokensSaida = tokensSaida,
            TokensCacheLido = tokensCacheLido,
            TokensCacheEscrito = tokensCacheEscrito,
            CustoUsd = custoUsd,
            DuracaoMs = duracaoMs,
            Desfecho = desfecho,
            CriadoEm = Datas.Utc(agora),
        };
    }
}
