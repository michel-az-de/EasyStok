using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Produtos;

namespace EasyStock.Web.Services;

public class ProdutosService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<PagedResult<ProdutoResumo>>> ListarAsync(int page = 1, int limit = 20) =>
        api.GetAsync<PagedResult<ProdutoResumo>>($"produtos?empresaId={GetEmpresaId()}&page={page}&pageSize={limit}");

    public Task<ApiResult<List<ProdutoResumo>>> BuscarAsync(string termo, int limite = 10) =>
        api.GetAsync<List<ProdutoResumo>>(
            $"produtos/search?empresaId={GetEmpresaId()}&termo={Uri.EscapeDataString(termo)}&limite={limite}");

    public Task<ApiResult<ProdutoDetalhe>> ObterAsync(string id) =>
        api.GetAsync<ProdutoDetalhe>($"produtos/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<CategoriaApi>>> ListarCategoriasAsync() =>
        api.GetAsync<List<CategoriaApi>>($"categorias?empresaId={GetEmpresaId()}");

    public Task<ApiResult<CadastrarProdutoApiResult>> CriarAsync(ProdutoFormViewModel vm) =>
        api.PostAsync<CadastrarProdutoApiResult>("produtos", new
        {
            empresaId = GetEmpresaId(),
            categoriaId = vm.CategoriaId,
            subcategoriaId = vm.SubcategoriaId,
            nome = vm.Nome,
            descricaoBase = vm.DescricaoBase,
            marca = vm.Marca,
            tipo = vm.Tipo,
            skuBase = vm.SkuBase,
            codigoBarras = vm.CodigoBarras,
            controlaValidade = vm.ControlaValidade,
            custoReferencia = vm.CustoReferencia,
            precoReferencia = vm.PrecoReferencia,
            margemEstimada = vm.MargemEstimada,
            dimensoes = HasDimensoes(vm) ? new
            {
                peso = vm.DimensoesPeso ?? 0m,
                largura = vm.DimensoesLargura ?? 0m,
                altura = vm.DimensoesAltura ?? 0m,
                comprimento = vm.DimensoesComprimento ?? 0m
            } : null,
            caracteristicas = (vm.Caracteristicas ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c.Nome))
                .Select((c, i) => new
                {
                    nome = c.Nome.Trim(),
                    descricao = c.Descricao,
                    quantidadeReferencia = c.QuantidadeReferencia,
                    variacaoPadrao = c.VariacaoPadrao,
                    ordemExibicao = i
                }).ToArray(),
            embalagens = (vm.Embalagens ?? [])
                .Where(e => !string.IsNullOrWhiteSpace(e.Nome))
                .Select(e => new
                {
                    nome = e.Nome.Trim(),
                    descricao = e.Descricao,
                    dimensoes = HasEmbalagemDimensoes(e) ? new
                    {
                        peso = e.Peso ?? 0m,
                        largura = e.Largura ?? 0m,
                        altura = e.Altura ?? 0m,
                        comprimento = e.Comprimento ?? 0m
                    } : (object?)null,
                    padrao = e.Padrao
                }).ToArray(),
            variacoes = (vm.VariacoesRich ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v.Nome))
                .Select(v => new
                {
                    nome = v.Nome.Trim(),
                    cor = v.Cor,
                    tamanho = v.Tamanho,
                    descricaoComercial = v.DescricaoComercial,
                    sku = v.Sku,
                    codigoBarras = v.CodigoBarras,
                    ativa = v.Ativa
                }).ToArray()
        });

    public Task<ApiResult<object>> EditarAsync(string id, ProdutoFormViewModel vm) =>
        api.PatchAsync<object>($"produtos/{id}", new
        {
            empresaId = GetEmpresaId(),
            produtoId = Guid.TryParse(id, out var pid) ? pid : Guid.Empty,
            categoriaId = vm.CategoriaId,
            subcategoriaId = vm.SubcategoriaId,
            nome = vm.Nome,
            descricaoBase = vm.DescricaoBase,
            marca = vm.Marca,
            tipo = vm.Tipo,
            skuBase = vm.SkuBase,
            codigoBarras = vm.CodigoBarras,
            controlaValidade = vm.ControlaValidade,
            custoReferencia = vm.CustoReferencia,
            precoReferencia = vm.PrecoReferencia,
            margemEstimada = vm.MargemEstimada,
            status = vm.Status,
            dimensoes = HasDimensoes(vm) ? new
            {
                peso = vm.DimensoesPeso ?? 0m,
                largura = vm.DimensoesLargura ?? 0m,
                altura = vm.DimensoesAltura ?? 0m,
                comprimento = vm.DimensoesComprimento ?? 0m
            } : null,
            caracteristicas = (vm.Caracteristicas ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c.Nome))
                .Select((c, i) => new
                {
                    nome = c.Nome.Trim(),
                    descricao = c.Descricao,
                    quantidadeReferencia = c.QuantidadeReferencia,
                    variacaoPadrao = c.VariacaoPadrao,
                    ordemExibicao = i
                }).ToArray(),
            embalagens = (vm.Embalagens ?? [])
                .Where(e => !string.IsNullOrWhiteSpace(e.Nome))
                .Select(e => new
                {
                    nome = e.Nome.Trim(),
                    descricao = e.Descricao,
                    dimensoes = HasEmbalagemDimensoes(e) ? new
                    {
                        peso = e.Peso ?? 0m,
                        largura = e.Largura ?? 0m,
                        altura = e.Altura ?? 0m,
                        comprimento = e.Comprimento ?? 0m
                    } : (object?)null,
                    padrao = e.Padrao
                }).ToArray(),
            variacoes = (vm.VariacoesRich ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v.Nome))
                .Select(v => new
                {
                    nome = v.Nome.Trim(),
                    cor = v.Cor,
                    tamanho = v.Tamanho,
                    descricaoComercial = v.DescricaoComercial,
                    sku = v.Sku,
                    codigoBarras = v.CodigoBarras,
                    ativa = v.Ativa
                }).ToArray()
        });

    public Task<ApiResult<object>> AtualizarPrecoAsync(string id, decimal preco) =>
        api.PatchAsync<object>($"produtos/{id}", new
        {
            empresaId = GetEmpresaId(),
            produtoId = Guid.TryParse(id, out var pid) ? pid : Guid.Empty,
            precoReferencia = preco
        });

    public Task<ApiResult<bool>> ExcluirAsync(string id) =>
        api.DeleteAsync($"produtos/{id}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> HistoricoAsync(string id) =>
        api.GetAsync<object>($"produtos/{id}/historico?empresaId={GetEmpresaId()}");

    public async Task<ApiResult<object>> UploadFotoAsync(string id, IFormFile foto)
    {
        using var form = new MultipartFormDataContent();
        using var stream = foto.OpenReadStream();
        var sc = new StreamContent(stream);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(foto.ContentType ?? "application/octet-stream");
        form.Add(sc, "file", foto.FileName);
        return await api.PostMultipartAsync<object>($"produtos/{id}/fotos?empresaId={GetEmpresaId()}", form);
    }

    public Task<ApiResult<object>> AdicionarVariacaoAsync(string id, string nome, string? sku = null) =>
        api.PostAsync<object>($"produtos/{id}/variacoes", new
        {
            empresaId = GetEmpresaId(),
            produtoId = Guid.TryParse(id, out var pid) ? pid : Guid.Empty,
            nome,
            sku,
            ativa = true
        });

    public Task<ApiResult<bool>> RemoverVariacaoAsync(string id, string vid) =>
        api.DeleteAsync($"produtos/{id}/variacoes/{vid}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<object>> RestaurarAsync(string id) =>
        api.PostAsync<object>($"produtos/{id}/restaurar?empresaId={GetEmpresaId()}", new { });

    public Task<ApiResult<object>> RestaurarVariacaoAsync(string id, string vid) =>
        api.PostAsync<object>($"produtos/{id}/variacoes/{vid}/restaurar?empresaId={GetEmpresaId()}", new { });

    public Task<ApiResult<bool>> RemoverFotoAsync(string produtoId, string fotoId) =>
        api.DeleteAsync($"produtos/{produtoId}/fotos/{fotoId}?empresaId={GetEmpresaId()}");

    public Task<ApiResult<List<string>>> ListarMarcasAsync(string? q = null) =>
        api.GetAsync<List<string>>($"produtos/marcas?empresaId={GetEmpresaId()}{(string.IsNullOrWhiteSpace(q) ? "" : $"&q={Uri.EscapeDataString(q)}")}");


    private static bool HasDimensoes(ProdutoFormViewModel vm) =>
        vm.DimensoesPeso.HasValue || vm.DimensoesLargura.HasValue ||
        vm.DimensoesAltura.HasValue || vm.DimensoesComprimento.HasValue;

    private static bool HasEmbalagemDimensoes(EmbalagemFormItem e) =>
        e.Peso.HasValue || e.Largura.HasValue || e.Altura.HasValue || e.Comprimento.HasValue;
}
