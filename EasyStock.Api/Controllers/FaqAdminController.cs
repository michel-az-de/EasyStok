using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Faq.Admin;

namespace EasyStock.Api.Controllers
{
    /// <summary>
    /// Gestao admin do FAQ — protegido por permissao GerenciarFaq.
    /// </summary>
    [ApiController]
    [Route("api/admin/faq")]
    [Authorize]
    public class FaqAdminController(
        ListarFaqAdminUseCase listarUseCase,
        CriarFaqCategoriaUseCase criarCategoriaUseCase,
        CriarFaqItemUseCase criarItemUseCase,
        AtualizarFaqItemUseCase atualizarUseCase,
        PublicarFaqItemUseCase publicarUseCase,
        ArquivarFaqItemUseCase arquivarUseCase,
        IFaqAdminRepository repo,
        ICurrentUserAccessor currentUser) : EasyStockControllerBase
    {
        [HttpGet("categorias")]
        public async Task<IActionResult> ListarCategorias(CancellationToken ct)
        {
            if (!currentUser.TemPermissao(Permissao.GerenciarFaq)) return Forbid();
            var categorias = await repo.ListarCategoriasAsync(ct);
            return DataOk(categorias.Select(c => new
            {
                c.Id,
                c.Nome,
                c.Slug,
                c.Descricao,
                c.Icone,
                c.Ordem,
                c.Publica,
                c.AtualizadoEm
            }));
        }

        [HttpPost("categorias")]
        public async Task<IActionResult> CriarCategoria([FromBody] CriarFaqCategoriaRequest req, CancellationToken ct)
        {
            if (!currentUser.TemPermissao(Permissao.GerenciarFaq)) return Forbid();
            try
            {
                var result = await criarCategoriaUseCase.ExecuteAsync(
                    new CriarFaqCategoriaCommand(req.Nome, req.Slug, req.Descricao, req.Icone, req.Ordem),
                    ct);
                return DataCreated($"/api/admin/faq/categorias/{result.CategoriaId}", result);
            }
            catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        }

        [HttpGet("itens")]
        public async Task<IActionResult> ListarItens(
            [FromQuery] FaqStatus? status = null,
            [FromQuery] Guid? categoriaId = null,
            [FromQuery] string? busca = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            if (!currentUser.TemPermissao(Permissao.GerenciarFaq)) return Forbid();
            var result = await listarUseCase.ExecuteAsync(
                new ListarFaqAdminQuery(status, categoriaId, busca, page, pageSize), ct);
            return DataOk(result);
        }

        [HttpPost("itens")]
        public async Task<IActionResult> CriarItem([FromBody] CriarFaqItemRequest req, CancellationToken ct)
        {
            if (!currentUser.TemPermissao(Permissao.GerenciarFaq)) return Forbid();
            try
            {
                var result = await criarItemUseCase.ExecuteAsync(
                    new CriarFaqItemCommand(
                        req.CategoriaId,
                        req.Titulo,
                        req.Slug,
                        req.Conteudo,
                        req.ConteudoBusca,
                        req.Tags,
                        req.Ordem,
                        currentUser.IsAuthenticated ? currentUser.UsuarioId : null),
                    ct);
                return DataCreated($"/api/admin/faq/itens/{result.ItemId}", result);
            }
            catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        }

        [HttpPut("itens/{id:guid}")]
        public async Task<IActionResult> AtualizarItem(Guid id, [FromBody] AtualizarFaqItemRequest req, CancellationToken ct)
        {
            if (!currentUser.TemPermissao(Permissao.GerenciarFaq)) return Forbid();
            try
            {
                await atualizarUseCase.ExecuteAsync(
                    new AtualizarFaqItemCommand(id, req.Titulo, req.Conteudo, req.ConteudoBusca, req.Tags, req.Ordem),
                    ct);
                return NoContent();
            }
            catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        }

        [HttpPost("itens/{id:guid}/publicar")]
        public async Task<IActionResult> Publicar(Guid id, CancellationToken ct)
        {
            if (!currentUser.TemPermissao(Permissao.GerenciarFaq)) return Forbid();
            try
            {
                await publicarUseCase.ExecuteAsync(new PublicarFaqItemCommand(id), ct);
                return NoContent();
            }
            catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        }

        [HttpPost("itens/{id:guid}/arquivar")]
        public async Task<IActionResult> Arquivar(Guid id, CancellationToken ct)
        {
            if (!currentUser.TemPermissao(Permissao.GerenciarFaq)) return Forbid();
            try
            {
                await arquivarUseCase.ExecuteAsync(new ArquivarFaqItemCommand(id), ct);
                return NoContent();
            }
            catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        }

        public sealed record CriarFaqCategoriaRequest(string Nome, string Slug, string? Descricao, string? Icone, int Ordem);
        public sealed record CriarFaqItemRequest(
            Guid CategoriaId,
            string Titulo,
            string Slug,
            string Conteudo,
            string? ConteudoBusca,
            string[]? Tags,
            int Ordem);
        public sealed record AtualizarFaqItemRequest(
            string Titulo,
            string Conteudo,
            string? ConteudoBusca,
            string[]? Tags,
            int Ordem);
    }
}
