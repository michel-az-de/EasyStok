namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/planos")]
[Authorize(Policy = "SuperAdmin")]
public class AdminPlanosController(IPlanoAdminRepository planos, AdminAuditService audit) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPlanos(CancellationToken ct = default)
        => DataOk(await planos.ListarComTenantsAsync(ct));

    [HttpPost]
    public async Task<IActionResult> CreatePlano([FromBody] CreatePlanoRequest req, CancellationToken ct = default)
    {
        var resumo = await planos.CriarAsync(
            new NovoPlano(req.Nome, req.Descricao, req.LimiteLojas, req.LimiteUsuarios,
                req.LimiteProdutos, req.LimiteGeracoesIaMensais, req.PrecoMensal), ct);

        await audit.LogAsync("PlanoAdicionado", $"Nome={resumo.Nome}");

        return DataCreated($"/api/admin/planos/{resumo.Id}", new { resumo.Id, resumo.Nome });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> PatchPlano(Guid id, [FromBody] PatchPlanoAdminRequest req, CancellationToken ct = default)
    {
        var resumo = await planos.AtualizarAsync(id,
            new PatchPlano(req.Nome, req.Descricao, req.LimiteLojas, req.LimiteUsuarios,
                req.LimiteProdutos, req.LimiteGeracoesIaMensais, req.PrecoMensal), ct);

        if (resumo is null) return DataNotFound("Plano não encontrado.");

        await audit.LogAsync("PlanoAtualizado", $"PlanoId={id}");
        return DataOk(new { resumo.Id, resumo.Nome });
    }

    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> TogglePlano(Guid id, CancellationToken ct = default)
    {
        var resultado = await planos.AlternarAtivoAsync(id, ct);

        if (resultado is null) return DataNotFound("Plano não encontrado.");

        await audit.LogAsync("PlanoToggle", $"PlanoId={id}, Ativo={resultado.Ativo}");

        return DataOk(new { resultado.Id, resultado.Ativo });
    }
}

public record CreatePlanoRequest(
    string Nome,
    string? Descricao,
    int LimiteLojas,
    int LimiteUsuarios,
    int LimiteProdutos,
    int LimiteGeracoesIaMensais,
    decimal PrecoMensal);

public record PatchPlanoAdminRequest(
    string? Nome,
    string? Descricao,
    int? LimiteLojas,
    int? LimiteUsuarios,
    int? LimiteProdutos,
    int? LimiteGeracoesIaMensais,
    decimal? PrecoMensal);
