using System.Text.RegularExpressions;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Ocorrencias;

/// <summary>Ocorrência para o console (S27). Enums em snake_case, como na spec.</summary>
public sealed record OcorrenciaDto(
    Guid Id,
    Guid PedidoId,
    Guid ClienteId,
    Guid? ConversaId,
    string Origem,
    string Categoria,
    string Relato,
    string Status,
    string? Resolucao,
    decimal? ReembolsoValor,
    string? ReembolsoIdSolicitacao,
    DateTime? ReembolsoEm,
    DateTime CriadaEm,
    DateTime? ResolvidaEm,
    Guid? ResolvidaPorUsuarioId,
    DateTime? ApuradaEm = null,
    Guid? ApuradaPorUsuarioId = null,
    string? ApuradaPorNome = null,
    string? ResolvidaPorNome = null,
    DateTime? ReembolsoSolicitadoEm = null,
    string? ReembolsoSituacao = null)
{
    public OcorrenciaDto ComEstorno(PedidoEstornoOnline? e) => this with
    {
        ReembolsoSituacao = e?.Situacao ?? (ReembolsoEm is not null ? PedidoEstornoOnline.Confirmado : null),
        ReembolsoSolicitadoEm = ReembolsoSolicitadoEm ?? (Status == "aberta" ? e?.CriadoEm : null),
        ReembolsoValor = ReembolsoValor ?? (Status == "aberta" ? e?.Valor : null),
        Resolucao = Resolucao ?? (Status == "aberta" ? e?.Motivo : null)
    };

    public static OcorrenciaDto De(Ocorrencia o) => new(
        o.Id, o.PedidoId, o.ClienteId, o.ConversaId,
        Snake(o.Origem.ToString()), Snake(o.Categoria.ToString()), o.Relato, Snake(o.Status.ToString()),
        o.Resolucao, o.ReembolsoValor, o.ReembolsoIdSolicitacao, o.ReembolsoEm,
        o.CriadaEm, o.ResolvidaEm, o.ResolvidaPorUsuarioId,
        o.ApuradaEm, o.ApuradaPorUsuarioId, o.ApuradaPorNome, o.ResolvidaPorNome, o.ReembolsoSolicitadoEm);

    /// <summary><c>ProdutoImproprio</c> vira <c>produto_improprio</c>.</summary>
    public static string Snake(string nome) => Regex.Replace(nome, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();

    /// <summary>Lê <c>produto_improprio</c> (ou <c>ProdutoImproprio</c>) de volta para o enum. Número não vale.</summary>
    public static bool TryParse<TEnum>(string? valor, out TEnum resultado) where TEnum : struct, Enum
    {
        resultado = default;
        if (string.IsNullOrWhiteSpace(valor)) return false;
        var nome = valor.Trim().Replace("_", string.Empty);
        return !int.TryParse(nome, out _) && Enum.TryParse(nome, ignoreCase: true, out resultado) && Enum.IsDefined(resultado);
    }
}
