using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Domain.Entities.Ia;

/// <summary>
/// #2957 (S1 de #2948): um modelo de um provedor de IA da loja, com preço em US$ por milhão de tokens no
/// horário de pico e o fator aplicado fora dele (D7). Um só padrão por loja (índice único parcial), e o
/// padrão precisa estar ligado.
/// </summary>
public sealed class ModeloIa
{
    public const int IdExternoTamanhoMaximo = 100;
    public const int RotuloTamanhoMaximo = 80;
    public const int SaidaMaxMinima = 256;
    public const int SaidaMaxMaxima = 200_000;
    private const decimal Milhao = 1_000_000m;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid ProvedorId { get; private set; }

    /// <summary>Nome do modelo na API do provedor.</summary>
    public string IdExterno { get; private set; } = null!;
    public string Rotulo { get; private set; } = null!;
    public bool Ligado { get; private set; }
    public bool Padrao { get; private set; }
    public decimal? PrecoEntrada { get; private set; }
    public decimal? PrecoSaida { get; private set; }
    public decimal? PrecoCacheLido { get; private set; }

    /// <summary>Multiplicador do preço fora do pico, em (0, 1]. 1 = mesmo preço.</summary>
    public decimal FatorForaPico { get; private set; } = 1m;

    /// <summary>#3304 (R8): teto de <c>max_tokens</c> deste modelo; nulo usa o global (<c>Anthropic:MaxTokensAgente</c>).</summary>
    public int? SaidaMax { get; private set; }

    public void DefinirSaidaMax(int? saidaMax, DateTime agora)
    {
        if (saidaMax is { } v && (v < SaidaMaxMinima || v > SaidaMaxMaxima))
            throw new RegraDeDominioVioladaException(
                $"Teto de saída do modelo precisa ficar entre {SaidaMaxMinima} e {SaidaMaxMaxima} tokens.");
        SaidaMax = saidaMax;
        AlteradoEm = Datas.Utc(agora);
    }

    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    private ModeloIa() { }

    public static ModeloIa Criar(Guid empresaId, Guid provedorId, string idExterno, string rotulo,
        decimal? precoEntrada, decimal? precoSaida, decimal? precoCacheLido, decimal fatorForaPico, DateTime agora)
    {
        if (empresaId == Guid.Empty || provedorId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Empresa e provedor são obrigatórios no modelo de IA.");
        if (string.IsNullOrWhiteSpace(idExterno) || idExterno.Trim().Length > IdExternoTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Id do modelo é obrigatório, com até {IdExternoTamanhoMaximo} caracteres.");
        if (string.IsNullOrWhiteSpace(rotulo) || rotulo.Trim().Length > RotuloTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Rótulo do modelo é obrigatório, com até {RotuloTamanhoMaximo} caracteres.");

        var modelo = new ModeloIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProvedorId = provedorId,
            IdExterno = idExterno.Trim(),
            Rotulo = rotulo.Trim(),
            CriadoEm = Datas.Utc(agora),
        };
        modelo.DefinirPrecos(precoEntrada, precoSaida, precoCacheLido, fatorForaPico, agora);
        return modelo;
    }

    public void DefinirPrecos(decimal? entrada, decimal? saida, decimal? cacheLido, decimal fatorForaPico, DateTime agora)
    {
        LimitesIa.ValidarValor(entrada, "Preço do modelo");
        LimitesIa.ValidarValor(saida, "Preço do modelo");
        LimitesIa.ValidarValor(cacheLido, "Preço do modelo");
        if (fatorForaPico <= 0 || fatorForaPico > 1 || decimal.Round(fatorForaPico, LimitesIa.CasasFator) != fatorForaPico)
            throw new RegraDeDominioVioladaException(
                $"Fator fora do pico precisa ficar entre 0 (exclusivo) e 1, com até {LimitesIa.CasasFator} casas.");
        PrecoEntrada = entrada;
        PrecoSaida = saida;
        PrecoCacheLido = cacheLido;
        FatorForaPico = fatorForaPico;
        AlteradoEm = Datas.Utc(agora);
    }

    public void Ligar(DateTime agora)
    {
        Ligado = true;
        AlteradoEm = Datas.Utc(agora);
    }

    public void Desligar(DateTime agora)
    {
        if (Padrao)
            throw new RegraDeDominioVioladaException("Escolha outro modelo padrão antes de desligar este.");
        Ligado = false;
        AlteradoEm = Datas.Utc(agora);
    }

    public void TornarPadrao(DateTime agora)
    {
        if (!Ligado)
            throw new RegraDeDominioVioladaException("O modelo padrão precisa estar ligado.");
        Padrao = true;
        AlteradoEm = Datas.Utc(agora);
    }

    public void DeixarDeSerPadrao(DateTime agora)
    {
        Padrao = false;
        AlteradoEm = Datas.Utc(agora);
    }

    /// <summary>
    /// Custo em US$ da chamada; nulo quando falta preço de entrada ou saída. Cache sem preço custa zero.
    /// Cache escrito fica fora: a DeepSeek não cobra a mais por ele, e o preço próprio só entra com outro provedor.
    /// </summary>
    public decimal? Custo(int tokensEntrada, int tokensSaida, int tokensCacheLido, bool foraDoPico)
    {
        if (tokensEntrada < 0 || tokensSaida < 0 || tokensCacheLido < 0)
            throw new RegraDeDominioVioladaException("Tokens da chamada não podem ser negativos.");
        if (PrecoEntrada is not { } entrada || PrecoSaida is not { } saida) return null;
        var cheio = (tokensEntrada * entrada + tokensSaida * saida + tokensCacheLido * (PrecoCacheLido ?? 0m)) / Milhao;
        return foraDoPico ? cheio * FatorForaPico : cheio;
    }
}
