using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// #2813 (B4): consulta dos fechamentos de caixa que vieram no snapshot do aparelho, SOMENTE LEITURA. O import guarda o
/// snapshot inteiro em DeviceBackup (Note "legacy-import:ID") e trata cashClosings como archive-only; aqui so se le dali.
/// Nao grava FechamentoCaixa, nao abre nem fecha caixa, nao cria lancamento e nao mexe em saldo. Valores em reais, como no aparelho.
/// </summary>
public sealed class LegacyCashClosingsReadService(EasyStockDbContext db)
{
    public const string Origin = "aparelho-legado";
    private const string NotePrefix = "legacy-import:";
    private static readonly long MinMs = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
    private static readonly long MaxMs = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    public async Task<LegacyCashClosingsResponse> ListAsync(Guid empresaId, CancellationToken ct)
    {
        // Import mais novo primeiro: o mesmo id de fechamento em dois imports vale pelo mais recente.
        var imports = await db.Set<DeviceBackup>().AsNoTracking()
            .Where(b => b.EmpresaId == empresaId && b.Note != null && b.Note.StartsWith(NotePrefix))
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Select(b => b.Id).ToListAsync(ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<LegacyCashClosingDto>();
        var ignored = 0;
        foreach (var importId in imports)
        {
            // Um snapshot por vez: cada um pode passar de 7 MB. So a colecao cashClosings e lida.
            var snapshot = await db.Set<DeviceBackup>().AsNoTracking().Where(b => b.Id == importId).Select(b => b.SnapshotJson).SingleAsync(ct);
            ignored += Collect(snapshot, importId, seen, items);
        }
        var ordered = items.OrderByDescending(x => x.DateKey, StringComparer.Ordinal)
            .ThenByDescending(x => x.ClosedAt).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        return new LegacyCashClosingsResponse(ordered, ignored);
    }

    /// <summary>Le os fechamentos de um snapshot. Devolve quantos itens foram pulados por campo ausente ou de tipo errado.</summary>
    private static int Collect(string snapshotJson, Guid importId, HashSet<string> seen, List<LegacyCashClosingDto> items)
    {
        using var document = JsonDocument.Parse(snapshotJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("cashClosings", out var rows) || rows.ValueKind != JsonValueKind.Array) return 0;
        var ignored = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !TryText(row, "id", out var id) || id.Length == 0) { ignored++; continue; }
            if (!seen.Add(id)) continue; // mesmo id em import mais antigo: ja vale o do mais recente
            if (TryRead(row, id, importId) is { } item) items.Add(item);
            else ignored++;
        }
        return ignored;
    }

    private static LegacyCashClosingDto? TryRead(JsonElement row, string id, Guid importId)
    {
        if (!TryText(row, "dateKey", out var dateKey)
            || !DateOnly.TryParseExact(dateKey, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)
            || !row.TryGetProperty("closedAt", out var closedAt) || closedAt.ValueKind != JsonValueKind.Number
            || !closedAt.TryGetInt64(out var ms) || ms < MinMs || ms > MaxMs
            || !TryText(row, "closedBy", out var closedBy) || !TryNullableText(row, "notes", out var notes)
            || !row.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object
            || !TryMoney(summary, "sold", out var sold) || !TryMoney(summary, "in", out var inflow)
            || !TryMoney(summary, "out", out var outflow) || !TryMoney(summary, "balance", out var balance)
            || !TryNullableMoney(summary, "conferredBalance", out var conferred)
            || !TryCount(summary, "entriesCount", out var entries) || !TryCount(summary, "ordersCount", out var orders))
            return null;
        return new LegacyCashClosingDto(id, dateKey, DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, closedBy, notes,
            new LegacyCashClosingTotalsDto(sold, inflow, outflow, balance, conferred, entries, orders), Origin, importId);
    }

    private static bool TryText(JsonElement parent, string name, out string value)
    {
        value = "";
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString()!;
        return true;
    }

    /// <summary>Ausente ou nulo vale nulo; qualquer outro tipo que nao texto invalida o item.</summary>
    private static bool TryNullableText(JsonElement parent, string name, out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return true;
    }

    private static bool TryMoney(JsonElement parent, string name, out decimal value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value);
    }

    /// <summary>Ausente ou nulo vale nulo (ninguem conferiu a gaveta); qualquer outro tipo que nao numero invalida o item.</summary>
    private static bool TryNullableMoney(JsonElement parent, string name, out decimal? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out var number)) return false;
        value = number;
        return true;
    }

    private static bool TryCount(JsonElement parent, string name, out int value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }
}
