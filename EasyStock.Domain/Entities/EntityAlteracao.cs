namespace EasyStock.Domain.Entities;

/// <summary>
/// F10-B — Auditoria universal: registro generico de alteracao em qualquer
/// entidade do allowlist. Substitui a necessidade de criar *_alteracoes por
/// entidade (Pedido, Caixa, ItemEstoque, Lote, etc.).
///
/// PII: <see cref="ValorAntigo"/>/<see cref="ValorNovo"/> sao mascarados
/// quando o campo pertence ao set PII. Original criptografado em
/// <see cref="PiiCriptografado"/> (F10-B-2, KMS; F10-B-1 deixa null).
///
/// Principio #2: operacao principal nunca falha por auditoria.
/// Principio #4: default-deny — allowlist explicita por entidade.
/// </summary>
public class EntityAlteracao
{
    public Guid Id { get; set; }

    /// <summary>Tenant isolation — Global Query Filter aplica.</summary>
    public Guid EmpresaId { get; set; }

    /// <summary>Nome do tipo da entidade (ex: "Pedido", "MovimentoCaixa").</summary>
    public string TipoEntidade { get; set; } = null!;

    /// <summary>PK da entidade auditada.</summary>
    public Guid EntidadeId { get; set; }

    /// <summary>"criado" | "atualizado" | "removido"</summary>
    public string Acao { get; set; } = null!;

    /// <summary>Nome do campo alterado (null em criacao/remocao com AlteracoesJson).</summary>
    public string? Campo { get; set; }

    /// <summary>Valor anterior (mascarado se PII).</summary>
    public string? ValorAntigo { get; set; }

    /// <summary>Valor novo (mascarado se PII).</summary>
    public string? ValorNovo { get; set; }

    /// <summary>Usuario que realizou a alteracao (pode ser null em operacoes de sistema).</summary>
    public Guid? AlteradoPorUserId { get; set; }

    /// <summary>Nome do usuario/operador (mobile: operatorName do envelope).</summary>
    public string? AlteradoPorNome { get; set; }

    /// <summary>"web" | "mobile" | "api" | "sistema"</summary>
    public string? Origem { get; set; }

    public DateTime AlteradoEm { get; set; }

    /// <summary>
    /// JSON array de {campo, de, para} — max 50KB, truncado com flag _truncated.
    /// Usado em criacao (snapshot de todos os campos) e update (diff completo).
    /// </summary>
    public string? AlteracoesJson { get; set; }

    /// <summary>
    /// F10-B-2: PII criptografado via KMS (DataProtection per-tenant).
    /// F10-B-1: sempre null.
    /// </summary>
    public string? PiiCriptografado { get; set; }
}
