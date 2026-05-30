namespace EasyStock.Api.Controllers;

[ApiController]
public class AdminConfiguracoesController(
    IConfiguracaoSistemaRepository configuracoes,
    AdminAuditService audit) : EasyStockControllerBase
{
    private static readonly ConfiguracaoDefault[] _defaults =
    [
        new("manutencao_ativa",   "false",                    "Modo manutenção (bloqueia login de tenants)"),
        new("aviso_global",       "",                         "Banner de aviso exibido no topo do app (vazio = oculto)"),
        new("aviso_cor",          "gold",                     "Cor do banner: gold | red | basil"),
        new("dias_trial_padrao",  "30",                       "Dias padrão ao conceder trial para novos tenants"),
        new("email_suporte",      "suporte@easystock.com.br", "E-mail de suporte exibido no app"),
        new("versao_minima_pwa",  "1.0.0",                    "Força atualização do PWA se versão instalada for menor"),
    ];

    private static readonly HashSet<string> _publicKeys = ["manutencao_ativa", "aviso_global", "aviso_cor", "email_suporte", "versao_minima_pwa"];

    [HttpGet("api/admin/configuracoes")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetAll(CancellationToken ct = default)
    {
        await configuracoes.GarantirDefaultsAsync(_defaults, ct);
        var configs = await configuracoes.ListarTodasAsync(ct);
        return DataOk(configs);
    }

    [HttpPatch("api/admin/configuracoes")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Update([FromBody] PatchConfiguracoesRequest req, CancellationToken ct = default)
    {
        if (req.Items is null || req.Items.Length == 0)
            return DataBadRequest("Nenhum item informado.");

        await configuracoes.GarantirDefaultsAsync(_defaults, ct);

        var email = HttpContext.User.FindFirst("email")?.Value
                    ?? HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                    ?? "system";

        var itens = req.Items.Select(i => new ConfiguracaoPatchItem(i.Chave, i.Valor ?? "")).ToList();
        var atualizado = await configuracoes.AplicarPatchAsync(itens, email, ct);

        await audit.LogAsync("ConfiguracaoAlterada", string.Join(", ", req.Items.Select(i => $"{i.Chave}={i.Valor}")));

        return DataOk(new { atualizado });
    }

    [HttpGet("api/configuracoes/publica")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublica(CancellationToken ct = default)
    {
        var dict = new Dictionary<string, string>(await configuracoes.ObterPublicasAsync(_publicKeys, ct));

        // Ensure all public keys are present with defaults
        foreach (var d in _defaults.Where(d => _publicKeys.Contains(d.Chave)))
            dict.TryAdd(d.Chave, d.Valor);

        return DataOk(dict);
    }
}

public record PatchConfigItemRequest(string Chave, string? Valor);
public record PatchConfiguracoesRequest(PatchConfigItemRequest[] Items);
