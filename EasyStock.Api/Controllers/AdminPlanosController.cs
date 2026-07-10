using EasyStock.Api.Validation;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/planos")]
[Authorize(Policy = "SuperAdmin")]
public class AdminPlanosController(IPlanoAdminRepository planos, IEmpresaRepository empresas, AdminAuditService audit) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPlanos(CancellationToken ct = default)
        => DataOk(await planos.ListarComTenantsAsync(ct));

    [HttpPost]
    public async Task<IActionResult> CreatePlano([FromBody] CreatePlanoRequest req, CancellationToken ct = default)
    {
        if (PlanoValidacao.ValidarNome(req.Nome) is { } erroNome)
            return DataBadRequest(erroNome);
        if (PlanoValidacao.ValidarLimite(req.LimiteLojas, "Limite de lojas") is { } erroLojas)
            return DataBadRequest(erroLojas);
        if (PlanoValidacao.ValidarLimite(req.LimiteUsuarios, "Limite de usuarios") is { } erroUsuarios)
            return DataBadRequest(erroUsuarios);
        if (PlanoValidacao.ValidarLimite(req.LimiteProdutos, "Limite de produtos") is { } erroProdutos)
            return DataBadRequest(erroProdutos);
        if (PlanoValidacao.ValidarLimite(req.LimiteGeracoesIaMensais, "Limite de IA/mes") is { } erroIa)
            return DataBadRequest(erroIa);
        if (PlanoValidacao.ValidarPreco(req.PrecoMensal) is { } erroPreco)
            return DataBadRequest(erroPreco);
        // ADM-07 (#639): guardrail de plano gratuito (R$0 nao pode ter ilimitado/limites altos).
        if (PlanoValidacao.ValidarPlanoGratuito(req.PrecoMensal, req.LimiteLojas, req.LimiteUsuarios,
                req.LimiteProdutos, req.LimiteGeracoesIaMensais) is { } erroGratuito)
            return DataBadRequest(erroGratuito);
        // #743: catalogo global nao deve ter nome de cliente.
        if (PlanoValidacao.ValidarNomeNaoColideComTenant(
                req.Nome, (await empresas.GetAllAsync()).Select(e => e.Nome)) is { } erroColisao)
            return DataBadRequest(erroColisao);
        // #891: dois planos "Starter" ficam indistinguiveis no seletor de "Trocar plano".
        // Espelha o CODIGO_DUPLICADO do cupom; o backstop de banco e uq_planos_nome_lower.
        if (await planos.ExisteNomeAsync(req.Nome, ct: ct))
            return Conflict(new { error = new { code = "NOME_DUPLICADO", message = "Já existe um plano com este nome." } });

        var resumo = await planos.CriarAsync(
            new NovoPlano(req.Nome, req.Descricao, req.LimiteLojas, req.LimiteUsuarios,
                req.LimiteProdutos, req.LimiteGeracoesIaMensais, req.PrecoMensal), ct);

        await audit.LogAsync("PlanoAdicionado", $"Nome={resumo.Nome}");

        return DataCreated($"/api/admin/planos/{resumo.Id}", new { resumo.Id, resumo.Nome });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> PatchPlano(Guid id, [FromBody] PatchPlanoAdminRequest req, CancellationToken ct = default)
    {
        if (req.Nome is not null && PlanoValidacao.ValidarNome(req.Nome) is { } erroNome)
            return DataBadRequest(erroNome);
        if (req.LimiteLojas is { } limLojas && PlanoValidacao.ValidarLimite(limLojas, "Limite de lojas") is { } erroLojas)
            return DataBadRequest(erroLojas);
        if (req.LimiteUsuarios is { } limUsuarios && PlanoValidacao.ValidarLimite(limUsuarios, "Limite de usuarios") is { } erroUsuarios)
            return DataBadRequest(erroUsuarios);
        if (req.LimiteProdutos is { } limProdutos && PlanoValidacao.ValidarLimite(limProdutos, "Limite de produtos") is { } erroProdutos)
            return DataBadRequest(erroProdutos);
        if (req.LimiteGeracoesIaMensais is { } limIa && PlanoValidacao.ValidarLimite(limIa, "Limite de IA/mes") is { } erroIa)
            return DataBadRequest(erroIa);
        if (req.PrecoMensal is { } preco && PlanoValidacao.ValidarPreco(preco) is { } erroPreco)
            return DataBadRequest(erroPreco);
        // ADM-07 (#639): valida o guardrail de gratuito quando o patch traz preco + os 4 limites
        // (a edicao pelo Admin sempre envia o conjunto completo). Patch parcial sem todos = ignora.
        if (req.PrecoMensal is { } precoG && req.LimiteLojas is { } lojasG && req.LimiteUsuarios is { } usuariosG
            && req.LimiteProdutos is { } produtosG && req.LimiteGeracoesIaMensais is { } iaG
            && PlanoValidacao.ValidarPlanoGratuito(precoG, lojasG, usuariosG, produtosG, iaG) is { } erroGratuito)
            return DataBadRequest(erroGratuito);
        // #743: nome nao pode conter nome de cliente (so consulta tenants se o nome esta mudando).
        if (req.Nome is not null
            && PlanoValidacao.ValidarNomeNaoColideComTenant(
                req.Nome, (await empresas.GetAllAsync()).Select(e => e.Nome)) is { } erroColisao)
            return DataBadRequest(erroColisao);
        // #891: renomear para um nome ja usado tambem colide. ignorarId = o proprio plano, senao
        // salvar sem mudar o nome (o Admin sempre reenvia o conjunto completo) daria 409 contra si.
        if (req.Nome is not null && await planos.ExisteNomeAsync(req.Nome, id, ct))
            return Conflict(new { error = new { code = "NOME_DUPLICADO", message = "Já existe um plano com este nome." } });

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
