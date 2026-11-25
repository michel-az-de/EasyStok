using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Ia;

namespace EasyStock.Domain.Entities.Ia;

/// <summary>
/// #2957 (S1 de #2948): um provedor de IA da loja, no formato Anthropic Messages. A chave fica no cofre
/// (<c>credencial_integracao</c>, categoria Ia, <see cref="ProviderKey"/>); aqui só a referência.
/// Nasce desligado e só liga com chave.
/// </summary>
public sealed class ProvedorIa
{
    public const int NomeTamanhoMaximo = 60;
    public const int BaseUrlTamanhoMaximo = 200;
    public const int PrazoMinimoSegundos = 5;
    public const int PrazoMaximoSegundos = 300;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string Nome { get; private set; } = null!;
    public string BaseUrl { get; private set; } = null!;
    public AutenticacaoProvedorIa Autenticacao { get; private set; }
    public bool Ligado { get; private set; }

    /// <summary>Teto em US$ por mês; nulo é sem cota.</summary>
    public decimal? OrcamentoMensalUsd { get; private set; }

    /// <summary>#3304 (R8): prazo por chamada em segundos; nulo usa o global (<c>Anthropic:TimeoutSegundos</c>).</summary>
    public int? PrazoSegundos { get; private set; }

    public void DefinirPrazo(int? segundos, DateTime agora)
    {
        if (segundos is { } v && (v < PrazoMinimoSegundos || v > PrazoMaximoSegundos))
            throw new RegraDeDominioVioladaException(
                $"Prazo do provedor precisa ficar entre {PrazoMinimoSegundos} e {PrazoMaximoSegundos} segundos.");
        PrazoSegundos = segundos;
        AlteradoEm = Datas.Utc(agora);
    }

    public Guid? CredencialId { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    /// <summary>Chave da credencial no cofre.</summary>
    public string ProviderKey => $"ia:{Id}";

    private ProvedorIa() { }

    public static ProvedorIa Criar(Guid empresaId, string nome, string baseUrl, AutenticacaoProvedorIa autenticacao,
        decimal? orcamentoMensalUsd, DateTime agora)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Empresa é obrigatória no provedor de IA.");
        if (string.IsNullOrWhiteSpace(nome) || nome.Trim().Length > NomeTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Nome do provedor é obrigatório, com até {NomeTamanhoMaximo} caracteres.");
        if (!Enum.IsDefined(autenticacao))
            throw new RegraDeDominioVioladaException("Autenticação do provedor inválida.");
        ValidarOrcamento(orcamentoMensalUsd);

        var utc = Datas.Utc(agora);
        return new ProvedorIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = nome.Trim(),
            BaseUrl = ValidarUrl(baseUrl),
            Autenticacao = autenticacao,
            OrcamentoMensalUsd = orcamentoMensalUsd,
            Ligado = false,
            CriadoEm = utc,
            AlteradoEm = utc,
        };
    }

    public void VincularCredencial(Guid credencialId, DateTime agora)
    {
        if (credencialId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Credencial inválida.");
        CredencialId = credencialId;
        AlteradoEm = Datas.Utc(agora);
    }

    public void Ligar(DateTime agora)
    {
        if (CredencialId is null)
            throw new RegraDeDominioVioladaException("Cadastre a chave antes de ligar o provedor.");
        Ligado = true;
        AlteradoEm = Datas.Utc(agora);
    }

    public void Desligar(DateTime agora)
    {
        Ligado = false;
        AlteradoEm = Datas.Utc(agora);
    }

    public void DefinirOrcamento(decimal? orcamentoMensalUsd, DateTime agora)
    {
        ValidarOrcamento(orcamentoMensalUsd);
        OrcamentoMensalUsd = orcamentoMensalUsd;
        AlteradoEm = Datas.Utc(agora);
    }

    private static void ValidarOrcamento(decimal? valor)
    {
        LimitesIa.ValidarValor(valor, "Orçamento mensal");
    }

    private static string ValidarUrl(string? url)
    {
        var texto = url?.Trim() ?? "";
        if (texto.Length > BaseUrlTamanhoMaximo
            || !Uri.TryCreate(texto, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
            throw new RegraDeDominioVioladaException("A URL do provedor precisa ser https e absoluta.");
        // #2967: segredo vai no cofre, nunca na URL.
        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new RegraDeDominioVioladaException("A URL do provedor não pode ter usuário, senha, query nem fragmento.");
        return texto.TrimEnd('/');
    }
}
