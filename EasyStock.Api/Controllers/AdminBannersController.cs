using EasyStock.Application.UseCases.Banners;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities.Banners;
using Npgsql;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Console de cadastro dos banners de plataforma (#869). Cross-tenant — restrito ao
/// SuperAdmin. O app Web consome os endpoints públicos em <see cref="BannersController"/>.
/// </summary>
[ApiController]
[Route("api/admin/banners")]
[Authorize(Policy = "SuperAdmin")]
public class AdminBannersController(
    CriarBannerUseCase criarUseCase,
    AtualizarBannerUseCase atualizarUseCase,
    AtivarBannerUseCase ativarUseCase,
    DesativarBannerUseCase desativarUseCase,
    DeletarBannerUseCase deletarUseCase,
    ListarBannersAdminUseCase listarUseCase,
    ObterBannerUseCase obterUseCase,
    ConsultarRecebimentoBannerUseCase recebimentoUseCase,
    RevelarEmailRecebimentoUseCase revelarEmailUseCase,
    GerenciarUploadsUseCase uploadsUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] bool? ativo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var r = await listarUseCase.ExecuteAsync(new ListarBannersAdminQuery(ativo, page, pageSize), ct);
        return DataPaged(r.Itens, r.Total, r.Page, r.PageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        try { return DataOk(await obterUseCase.ExecuteAsync(new ObterBannerQuery(id), ct)); }
        catch (BannerNaoEncontradoException) { return DataNotFound("Banner não encontrado."); }
    }

    /// <summary>Console de recebimento (#875): resumo + série diária + log paginado (e-mail mascarado).</summary>
    [HttpGet("{id:guid}/recebimento")]
    public async Task<IActionResult> Recebimento(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? tipo = null,
        [FromQuery] string? busca = null,
        CancellationToken ct = default)
    {
        try
        {
            var r = await recebimentoUseCase.ExecuteAsync(
                new ConsultarRecebimentoBannerQuery(id, page, pageSize, tipo, busca), ct);
            return DataOk(r);
        }
        catch (BannerNaoEncontradoException) { return DataNotFound("Banner não encontrado."); }
    }

    /// <summary>Revela o e-mail completo de um usuário no console (LGPD: acesso auditado no use case).</summary>
    [HttpGet("{id:guid}/recebimento/{usuarioId:guid}/email")]
    public async Task<IActionResult> RevelarEmail(Guid id, Guid usuarioId, CancellationToken ct)
    {
        var email = await revelarEmailUseCase.ExecuteAsync(usuarioId, currentUser.UsuarioId, ct);
        return email is null ? DataNotFound("Usuário não encontrado.") : DataOk(new { email });
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] BannerRequest req, CancellationToken ct)
    {
        try
        {
            var criadoPor = currentUser.IsAuthenticated ? currentUser.UsuarioId : (Guid?)null;
            var r = await criarUseCase.ExecuteAsync(new CriarBannerCommand(ToConteudo(req), criadoPor), ct);
            return DataCreated($"/api/admin/banners/{r.BannerId}", r);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] BannerRequest req, CancellationToken ct)
    {
        try
        {
            await atualizarUseCase.ExecuteAsync(new AtualizarBannerCommand(id, ToConteudo(req)), ct);
            return NoContent();
        }
        catch (BannerNaoEncontradoException) { return DataNotFound("Banner não encontrado."); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/ativar")]
    public async Task<IActionResult> Ativar(Guid id, CancellationToken ct)
    {
        try { await ativarUseCase.ExecuteAsync(new AtivarBannerCommand(id), ct); return NoContent(); }
        catch (BannerNaoEncontradoException) { return DataNotFound("Banner não encontrado."); }
    }

    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken ct)
    {
        try { await desativarUseCase.ExecuteAsync(new DesativarBannerCommand(id), ct); return NoContent(); }
        catch (BannerNaoEncontradoException) { return DataNotFound("Banner não encontrado."); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deletar(Guid id, CancellationToken ct)
    {
        try
        {
            await deletarUseCase.ExecuteAsync(new DeletarBannerCommand(id), ct);
            return NoContent();
        }
        catch (BannerNaoEncontradoException) { return DataNotFound("Banner não encontrado."); }
        catch (BannerComConfirmacoesException ex) { return DataConflict(ex.Message); }
        // Corrida: confirmação inserida entre o pré-check e o commit → FK 23503 → mesmo 409.
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23503" })
        {
            return DataConflict("Banner com confirmações registradas não pode ser excluído. Desative-o.");
        }
    }

    [HttpPost("imagem")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> UploadImagem(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return DataBadRequest("Arquivo não informado ou vazio.");
        if (file.Length > 6 * 1024 * 1024) return DataBadRequest("A imagem não pode ser maior que 6 MB.");

        try
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var r = await uploadsUseCase.UploadImagemBannerAsync(file.FileName, file.ContentType, ms.ToArray(), ct);
            return DataOk(new { storageKey = r.StorageKey, url = r.Url });
        }
        // Upload inválido → 400. UseCaseValidationException (tamanho/extensão) e
        // InvalidOperationException (MIME fora da allowlist / magic-byte não bate) são
        // ambos "arquivo ruim"; sem isto, o handler global mandaria o magic-byte para 409.
        catch (Exception ex) when (ex is UseCaseValidationException or InvalidOperationException)
        {
            return DataBadRequest(ex.Message);
        }
    }

    private static BannerConteudo ToConteudo(BannerRequest r) => new()
    {
        TituloInterno = r.TituloInterno ?? string.Empty,
        Tipo = string.Equals(r.Tipo, "mensagem", StringComparison.OrdinalIgnoreCase)
            ? BannerTipo.Mensagem : BannerTipo.Imagem,
        Corpo = r.Corpo,
        ImagemStorageKey = r.ImagemStorageKey,
        ImagemUrl = r.ImagemUrl,
        LinkAtivo = r.LinkAtivo,
        LinkUrl = r.LinkUrl,
        NovaAba = r.NovaAba,
        TooltipAtivo = r.TooltipAtivo,
        TooltipTexto = r.TooltipTexto,
        TamanhoModo = string.Equals(r.TamanhoModo, "manual", StringComparison.OrdinalIgnoreCase)
            ? BannerTamanhoModo.Manual : BannerTamanhoModo.HerdadoDaImagem,
        LarguraPx = r.LarguraPx,
        AlturaPx = r.AlturaPx,
        VisualizacaoUnica = r.VisualizacaoUnica,
        ExigeConfirmacao = r.ExigeConfirmacao,
        NotificarAoPublicar = r.NotificarAoPublicar,
        Ativo = r.Ativo,
        InicioEm = r.InicioEm,
        FimEm = r.FimEm,
        Prioridade = r.Prioridade
    };

    /// <summary>Contrato de cadastro/edição. Tipo = "imagem"|"mensagem"; TamanhoModo = "herdado"|"manual".</summary>
    public sealed record BannerRequest(
        string? TituloInterno,
        string? Tipo,
        string? Corpo,
        string? ImagemStorageKey,
        string? ImagemUrl,
        bool LinkAtivo,
        string? LinkUrl,
        bool NovaAba,
        bool TooltipAtivo,
        string? TooltipTexto,
        string? TamanhoModo,
        int? LarguraPx,
        int? AlturaPx,
        bool VisualizacaoUnica,
        bool ExigeConfirmacao,
        bool NotificarAoPublicar,
        bool Ativo,
        DateTime? InicioEm,
        DateTime? FimEm,
        int Prioridade);
}
