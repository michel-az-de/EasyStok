namespace EasyStock.Domain.Sales;

/// <summary>
/// Conversão bidirecional <see cref="StatusPedido"/> ↔ string canônica
/// (lowercase). Necessário pra compat com o domínio legado que armazena
/// status como <c>varchar(20)</c> em DB, e com clients (PWA, mobile, MAUI)
/// que serializam/desserializam status como string nos DTOs.
///
/// <para>
/// Não muda armazenamento de dados existentes — apenas oferece tradução
/// segura entre o tipo enum e a string já em uso.
/// </para>
/// </summary>
public static class StatusPedidoMapper
{
    public const string Aguardando = "aguardando";
    public const string Preparando = "preparando";
    public const string Pronto = "pronto";
    public const string Entregue = "entregue";
    public const string Cancelado = "cancelado";
    public const string Rascunho = "rascunho";
    public const string AguardandoPagamento = "aguardando_pagamento";

    /// <summary>Converte enum em string canônica lowercase.</summary>
    public static string Format(StatusPedido status) => status switch
    {
        StatusPedido.Aguardando => Aguardando,
        StatusPedido.Preparando => Preparando,
        StatusPedido.Pronto => Pronto,
        StatusPedido.Entregue => Entregue,
        StatusPedido.Cancelado => Cancelado,
        StatusPedido.Rascunho => Rascunho,
        StatusPedido.AguardandoPagamento => AguardandoPagamento,
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "Status fora do enum StatusPedido."),
    };

    /// <summary>
    /// Tenta interpretar string como <see cref="StatusPedido"/>. Tolera
    /// variações de caixa e espaços. Retorna false pra null/empty/desconhecido.
    /// </summary>
    public static bool TryParse(string? raw, out StatusPedido status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        switch (raw.Trim().ToLowerInvariant())
        {
            case Aguardando: status = StatusPedido.Aguardando; return true;
            case Preparando: status = StatusPedido.Preparando; return true;
            case Pronto: status = StatusPedido.Pronto; return true;
            case Entregue: status = StatusPedido.Entregue; return true;
            case Cancelado: status = StatusPedido.Cancelado; return true;
            case Rascunho: status = StatusPedido.Rascunho; return true;
            case AguardandoPagamento: status = StatusPedido.AguardandoPagamento; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Parse estrito. Lança <see cref="ArgumentException"/> em null/empty/desconhecido.
    /// Use quando string vier de fonte confiável (DB, contrato interno).
    /// </summary>
    public static StatusPedido Parse(string? raw)
    {
        if (TryParse(raw, out var s)) return s;
        throw new ArgumentException(
            $"Status desconhecido: '{raw}'. Esperado um de: aguardando, preparando, pronto, entregue, cancelado.",
            nameof(raw));
    }
}
