namespace EasyStock.Admin.Pages.Tickets;

public class DashboardModel(AdminApiClient api, AdminSessionService session, ILogger<DashboardModel> log) : AdminPageBase(session)
{
    public int Abertos { get; private set; }
    public int EmAtendimento { get; private set; }
    public int AguardandoCliente { get; private set; }
    public int ResolvidosHoje { get; private set; }
    public int SlaRisco { get; private set; }
    public int SlaViolado { get; private set; }
    public IReadOnlyList<JsonElement> FilaCriticidade { get; private set; } = Array.Empty<JsonElement>();
    public IReadOnlyDictionary<string, int> PorNivel { get; private set; } = new Dictionary<string, int>();
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            // Snapshot por status (uma chamada que retorna todos abertos com dados pra agregar)
            var raw = await api.GetRawAsync("api/admin/tickets?page=1&pageSize=200");
            var data = raw.TryGetProperty("data", out var d) ? d.EnumerateArray().ToList() : new List<JsonElement>();

            var hojeUtc = DateTime.UtcNow.Date;
            var nivelCounts = new Dictionary<string, int> { ["N1"] = 0, ["N2"] = 0, ["N3"] = 0, ["N4"] = 0 };
            var fila = new List<JsonElement>();

            foreach (var t in data)
            {
                var status = t.TryGetProperty("status", out var sp) ? sp.GetString() : "";
                var nivel = t.TryGetProperty("nivel", out var nv) ? nv.GetString() ?? "N1" : "N1";
                var slaRespV = t.TryGetProperty("slaRespostaViolado", out var srv) && srv.GetBoolean();
                var slaResolV = t.TryGetProperty("slaResolucaoViolado", out var srlv) && srlv.GetBoolean();
                var alteradoEm = t.TryGetProperty("alteradoEm", out var ap) && DateTime.TryParse(ap.GetString(), out var dt) ? dt : DateTime.MinValue;

                if (status == "Aberto") Abertos++;
                else if (status == "EmAtendimento") EmAtendimento++;
                else if (status == "AguardandoCliente") AguardandoCliente++;
                else if (status == "Resolvido" && alteradoEm.Date == hojeUtc) ResolvidosHoje++;

                if (status is "Aberto" or "EmAtendimento" or "AguardandoCliente")
                {
                    if (nivelCounts.ContainsKey(nivel)) nivelCounts[nivel]++;

                    if (slaRespV || slaResolV)
                    {
                        SlaViolado++;
                        fila.Add(t);
                    }
                    else
                    {
                        // Em risco se prazo restante < 4h
                        if (t.TryGetProperty("prazoResposta", out var pp) && pp.ValueKind != JsonValueKind.Null
                            && DateTime.TryParse(pp.GetString(), out var prazoDt))
                        {
                            var rest = prazoDt - DateTime.UtcNow;
                            if (rest.TotalHours < 4) { SlaRisco++; fila.Add(t); }
                        }
                    }
                }
            }

            PorNivel = nivelCounts;
            FilaCriticidade = fila.OrderBy(t => t.TryGetProperty("prazoResposta", out var pp) && pp.ValueKind != JsonValueKind.Null && DateTime.TryParse(pp.GetString(), out var dt) ? dt : DateTime.MaxValue).Take(10).ToList();
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar dashboard de tickets");
            Erro = "Não foi possível carregar o dashboard.";
        }
    }
}
