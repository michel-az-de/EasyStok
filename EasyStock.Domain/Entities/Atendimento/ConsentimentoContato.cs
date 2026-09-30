using System.Globalization;
using System.Text;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Consentimento do cliente final num canal para uma finalidade (S38, ADR-0051). Uma linha atual
/// por (empresa, cliente, canal, finalidade), alterada no lugar; <see cref="Origem"/> diz quem mudou
/// ("console", "palavra_sair", "backfill_consentiu_marketing"). Substitui o booleano
/// <c>Cliente.ConsentiuMarketing</c>, que continua só para compatibilidade até a poda.
/// </summary>
public class ConsentimentoContato
{
    public const int OrigemTamanhoMaximo = 60;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid ClienteId { get; private set; }
    public CanalConversa Canal { get; private set; }
    public FinalidadeContato Finalidade { get; private set; }
    public SituacaoConsentimento Situacao { get; private set; }
    public string Origem { get; private set; } = null!;
    public DateTime AtualizadoEm { get; private set; }

    // EF Core ctor sem parâmetros
    private ConsentimentoContato() { }

    public static ConsentimentoContato Registrar(
        Guid empresaId, Guid clienteId, CanalConversa canal, FinalidadeContato finalidade,
        SituacaoConsentimento situacao, string origem, DateTime em)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (clienteId == Guid.Empty)
            throw new RegraDeDominioVioladaException("ClienteId é obrigatório.");
        if (!Enum.IsDefined(canal))
            throw new RegraDeDominioVioladaException($"Canal inválido: {(int)canal}.");
        if (!Enum.IsDefined(finalidade))
            throw new RegraDeDominioVioladaException($"Finalidade inválida: {(int)finalidade}.");

        var consentimento = new ConsentimentoContato
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ClienteId = clienteId,
            Canal = canal,
            Finalidade = finalidade,
        };
        consentimento.Alterar(situacao, origem, em);
        return consentimento;
    }

    public void Alterar(SituacaoConsentimento situacao, string origem, DateTime em)
    {
        if (!Enum.IsDefined(situacao))
            throw new RegraDeDominioVioladaException($"Situação de consentimento inválida: {(int)situacao}.");
        if (string.IsNullOrWhiteSpace(origem))
            throw new RegraDeDominioVioladaException("Origem do consentimento é obrigatória.");

        var limpa = origem.Trim();
        Situacao = situacao;
        Origem = limpa.Length > OrigemTamanhoMaximo ? limpa[..OrigemTamanhoMaximo] : limpa;
        AtualizadoEm = em.Kind == DateTimeKind.Utc ? em : DateTime.SpecifyKind(em, DateTimeKind.Utc);
    }
}

/// <summary>Regras de envio ao cliente final por consentimento (S38). Sem estado.</summary>
public static class PoliticaConsentimento
{
    private static readonly HashSet<string> PalavrasDeOptOut = new(StringComparer.Ordinal) { "SAIR", "PARAR", "STOP" };

    /// <summary>
    /// Marketing só com <see cref="SituacaoConsentimento.Concedido"/> naquele canal. Transacional
    /// (aviso do pedido, resposta do atendimento) passa, salvo revogação explícita.
    /// </summary>
    public static bool PodeEnviar(IEnumerable<ConsentimentoContato> doCliente, CanalConversa canal, FinalidadeContato finalidade)
    {
        var atual = doCliente.FirstOrDefault(c => c.Canal == canal && c.Finalidade == finalidade);
        return finalidade == FinalidadeContato.Marketing
            ? atual?.Situacao == SituacaoConsentimento.Concedido
            : atual?.Situacao != SituacaoConsentimento.Revogado;
    }

    /// <summary>
    /// A mensagem inteira é só a palavra (SAIR, PARAR ou STOP), sem distinguir maiúsculas, acentos
    /// e pontuação nas pontas. "quero sair do grupo" não é opt-out: vai para o atendimento.
    /// </summary>
    public static bool EhPedidoDeOptOut(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var palavra = SemAcentos(texto.Trim().Trim('.', '!', '?', ',', ';')).ToUpperInvariant();
        return PalavrasDeOptOut.Contains(palavra);
    }

    private static string SemAcentos(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
