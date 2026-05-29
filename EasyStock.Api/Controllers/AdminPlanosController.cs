using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/planos")]
[Authorize(Policy = "SuperAdmin")]
public class AdminPlanosController(EasyStockDbContext db, AdminAuditService audit) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPlanos()
    {
        var planos = await db.Planos
            .OrderBy(p => p.PrecoMensal)
            .Select(p => new
            {
                p.Id,
                p.Nome,
                p.Descricao,
                p.LimiteLojas,
                p.LimiteUsuarios,
                p.LimiteProdutos,
                p.LimiteGeracoesIaMensais,
                p.PrecoMensal,
                p.Ativo,
                p.CriadoEm,
                totalTenants = db.AssinaturasEmpresa.Count(a => a.PlanoId == p.Id && a.Status == StatusAssinatura.Ativa)
            })
            .ToListAsync();

        return DataOk(planos);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePlano([FromBody] CreatePlanoRequest req)
    {
        var plano = new Plano
        {
            Id = Guid.NewGuid(),
            Nome = req.Nome,
            Descricao = req.Descricao,
            LimiteLojas = req.LimiteLojas,
            LimiteUsuarios = req.LimiteUsuarios,
            LimiteProdutos = req.LimiteProdutos,
            LimiteGeracoesIaMensais = req.LimiteGeracoesIaMensais,
            PrecoMensal = req.PrecoMensal,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };

        db.Planos.Add(plano);
        await db.CommitAsync();
        await audit.LogAsync("PlanoAdicionado", $"Nome={plano.Nome}");

        return DataCreated($"/api/admin/planos/{plano.Id}", new { plano.Id, plano.Nome });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> PatchPlano(Guid id, [FromBody] PatchPlanoAdminRequest req)
    {
        var plano = await db.Planos.FindAsync(id);
        if (plano is null) return DataNotFound("Plano não encontrado.");

        if (req.Nome is not null) plano.Nome = req.Nome;
        if (req.Descricao is not null) plano.Descricao = req.Descricao;
        if (req.LimiteLojas.HasValue) plano.LimiteLojas = req.LimiteLojas.Value;
        if (req.LimiteUsuarios.HasValue) plano.LimiteUsuarios = req.LimiteUsuarios.Value;
        if (req.LimiteProdutos.HasValue) plano.LimiteProdutos = req.LimiteProdutos.Value;
        if (req.LimiteGeracoesIaMensais.HasValue) plano.LimiteGeracoesIaMensais = req.LimiteGeracoesIaMensais.Value;
        if (req.PrecoMensal.HasValue) plano.PrecoMensal = req.PrecoMensal.Value;

        await db.CommitAsync();
        await audit.LogAsync("PlanoAtualizado", $"PlanoId={id}");
        return DataOk(new { plano.Id, plano.Nome });
    }

    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> TogglePlano(Guid id)
    {
        var plano = await db.Planos.FindAsync(id);
        if (plano is null) return DataNotFound("Plano não encontrado.");

        plano.Ativo = !plano.Ativo;
        await db.CommitAsync();
        await audit.LogAsync("PlanoToggle", $"PlanoId={id}, Ativo={plano.Ativo}");

        return DataOk(new { plano.Id, plano.Ativo });
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
