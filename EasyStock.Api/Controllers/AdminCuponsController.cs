namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/cupons")]
[Authorize(Policy = "SuperAdmin")]
public class AdminCuponsController(ICupomAdminRepository cupons, AdminAuditService audit) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCupons(CancellationToken ct = default)
        => DataOk(await cupons.ListarAsync(ct));

    [HttpPost]
    public async Task<IActionResult> CreateCupom([FromBody] CreateCupomRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Codigo))
            return DataBadRequest("Código é obrigatório.");

        if (!Enum.TryParse<TipoDesconto>(req.TipoDesconto, out var tipo))
            return DataBadRequest("TipoDesconto inválido. Valores: Percentual, ValorFixo, MesesGratis");

        var codigo = req.Codigo.ToUpperInvariant();

        if (ValidarCodigoCupom(codigo) is { } erroCodigo)
            return DataBadRequest(erroCodigo);
        if (ValidarValorCupom(tipo, req.Valor) is { } erroValor)
            return DataBadRequest(erroValor);

        if (await cupons.ExisteCodigoAsync(codigo, ct))
            return Conflict(new { error = new { code = "CODIGO_DUPLICADO", message = "Já existe um cupom com este código." } });

        var resumo = await cupons.CriarAsync(
            new NovoCupom(codigo, tipo, req.Valor, req.LimiteUsos, req.ValidoAte, req.PlanoId), ct);
        await audit.LogAsync("CupomCriado", $"Codigo={resumo.Codigo}");

        return DataCreated($"/api/admin/cupons/{resumo.Id}", new { resumo.Id, resumo.Codigo });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> PatchCupom(Guid id, [FromBody] PatchCupomRequest req, CancellationToken ct = default)
    {
        if (req.Codigo is not null && ValidarCodigoCupom(req.Codigo.ToUpperInvariant()) is { } erroCodigo)
            return DataBadRequest(erroCodigo);
        if (req.Valor is not null)
        {
            TipoDesconto? tipoPatch = Enum.TryParse<TipoDesconto>(req.TipoDesconto, out var tp) ? tp : null;
            if (ValidarValorCupom(tipoPatch, req.Valor.Value) is { } erroValor)
                return DataBadRequest(erroValor);
        }

        var r = await cupons.AtualizarAsync(id,
            new PatchCupom(req.Codigo, req.TipoDesconto, req.Valor, req.LimiteUsos, req.ValidoAte, req.PlanoId), ct);

        if (r.Status == AtualizacaoCupomStatus.NaoEncontrado) return DataNotFound("Cupom não encontrado.");
        if (r.Status == AtualizacaoCupomStatus.TipoInvalido) return DataBadRequest("TipoDesconto inválido.");

        await audit.LogAsync("CupomAtualizado", $"CupomId={id}");
        return DataOk(new { r.Resumo!.Id, r.Resumo.Codigo });
    }

    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> ToggleCupom(Guid id, CancellationToken ct = default)
    {
        var resultado = await cupons.AlternarAtivoAsync(id, ct);
        if (resultado is null) return DataNotFound("Cupom não encontrado.");

        await audit.LogAsync("CupomToggle", $"CupomId={id}, Ativo={resultado.Ativo}");
        return DataOk(new { resultado.Id, resultado.Ativo });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCupom(Guid id, CancellationToken ct = default)
    {
        var r = await cupons.ExcluirAsync(id, ct);

        if (r.Status == ExclusaoCupomStatus.NaoEncontrado)
            return DataNotFound("Cupom não encontrado.");
        if (r.Status == ExclusaoCupomStatus.EmUso)
            return Conflict(new { error = new { code = "CUPOM_EM_USO", message = "Não é possível excluir um cupom que já foi utilizado." } });

        await audit.LogAsync("CupomExcluido", $"Codigo={r.Codigo}");
        return DataOk(new { id });
    }

    // INV-001 (#463): validacao na fronteira da API. Antes so havia checagem de
    // codigo-vazio + enum, entao chamada direta criava cupom com percentual > 100
    // ou codigo com caracteres que viram vetor em atributo Alpine no Admin.
    private static string? ValidarCodigoCupom(string codigoUpper)
    {
        if (codigoUpper.Length is < 3 or > 50)
            return "Codigo do cupom deve ter entre 3 e 50 caracteres.";
        foreach (var c in codigoUpper)
            if (c is not (>= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'))
                return "Codigo do cupom so pode conter letras, numeros, hifen e underscore.";
        return null;
    }

    private static string? ValidarValorCupom(TipoDesconto? tipo, decimal valor)
    {
        if (valor <= 0)
            return "Valor do desconto deve ser maior que zero.";
        if (tipo == TipoDesconto.Percentual && valor > 100)
            return "Desconto percentual nao pode passar de 100%.";
        return null;
    }
}

public record CreateCupomRequest(
    string Codigo,
    string TipoDesconto,
    decimal Valor,
    int? LimiteUsos,
    DateTime? ValidoAte,
    Guid? PlanoId);

public record PatchCupomRequest(
    string? Codigo,
    string? TipoDesconto,
    decimal? Valor,
    int? LimiteUsos,
    DateTime? ValidoAte,
    Guid? PlanoId);
