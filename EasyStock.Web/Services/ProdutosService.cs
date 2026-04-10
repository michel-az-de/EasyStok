using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Produtos;

namespace EasyStock.Web.Services;

public class ProdutosService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetEmpresaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<ProdutoResumo>>> ListarAsync(
        int page = 1, int limit = 20)
    {
        var qs = $"produtos?page={page}&pageSize={limit}";
        return api.GetAsync<List<ProdutoResumo>>(qs);
    }

    public Task<ApiResult<List<ProdutoResumo>>> BuscarAsync(string termo, int limite = 10) =>
        api.GetAsync<List<ProdutoResumo>>(
            $"produtos/search?termo={Uri.EscapeDataString(termo)}&limite={limite}");

    public Task<ApiResult<ProdutoDetalhe>> ObterAsync(string id) =>
        api.GetAsync<ProdutoDetalhe>($"produtos/{id}");

    public Task<ApiResult<List<CategoriaApi>>> ListarCategoriasAsync() =>
        api.GetAsync<List<CategoriaApi>>("categorias");

    public Task<ApiResult<CadastrarProdutoApiResult>> CriarAsync(ProdutoFormViewModel vm) =>
        api.PostAsync<CadastrarProdutoApiResult>("produtos", BuildProdutoPayload(vm, GetEmpresaId(), isCreate: true));

    public Task<ApiResult<object>> EditarAsync(string id, ProdutoFormViewModel vm) =>
        api.PatchAsync<object>($"produtos/{id}", BuildProdutoPayload(vm, GetEmpresaId(), isCreate: false, id));

    private static object BuildProdutoPayload(ProdutoFormViewModel vm, Guid empresaId, bool isCreate, string? produtoId = null)
    {
        var temEmbalagem = !string.IsNullOrWhiteSpace(vm.EmbalagemNome);
        var temDimensoes = vm.DimensoesPeso.HasValue || vm.DimensoesLargura.HasValue ||
                           vm.DimensoesAltura.HasValue || vm.DimensoesComprimento.HasValue;

        object? dimensoes = temDimensoes
            ? new
            {
                peso = vm.DimensoesPeso ?? 0m,
                largura = vm.DimensoesLargura ?? 0m,
                altura = vm.DimensoesAltura ?? 0m,
                comprimento = vm.DimensoesComprimento ?? 0m
            }
            : null;

        object[]? embalagens = temEmbalagem
            ?
            [
                new
                {
                    nome = vm.EmbalagemNome!.Trim(),
                    descricao = vm.EmbalagemDescricao?.Trim(),
                    dimensoes = (vm.EmbalagemPeso.HasValue || vm.EmbalagemLargura.HasValue ||
                                 vm.EmbalagemAltura.HasValue || vm.EmbalagemComprimento.HasValue)
                        ? new
                        {
                            peso = vm.EmbalagemPeso ?? 0m,
                            largura = vm.EmbalagemLargura ?? 0m,
                            altura = vm.EmbalagemAltura ?? 0m,
                            comprimento = vm.EmbalagemComprimento ?? 0m
                        }
                        : (object?)null,
                    padrao = true
                }
            ]
            : null;

        if (isCreate)
        {
            return new
            {
                empresaId,
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
                dimensoes,
                embalagens,
                variacoes = vm.Variacoes
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => new { nome = v.Trim(), ativa = true })
                    .ToArray()
            };
        }
        else
        {
            return new
            {
                empresaId,
                produtoId = Guid.TryParse(produtoId, out var pid) ? pid : Guid.Empty,
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
                dimensoes,
                status = vm.Status
            };
        }
    }

    public Task<ApiResult<bool>> ExcluirAsync(string id) =>
        api.DeleteAsync($"produtos/{id}");

    public Task<ApiResult<object>> HistoricoAsync(string id) =>
        api.GetAsync<object>($"produtos/{id}/historico");

    public async Task<ApiResult<object>> UploadFotoAsync(string id, IFormFile foto)
    {
        using var form = new MultipartFormDataContent();
        using var stream = foto.OpenReadStream();
        form.Add(new StreamContent(stream), "file", foto.FileName);
        return await api.PostMultipartAsync<object>($"produtos/{id}/fotos", form);
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
        api.DeleteAsync($"produtos/{id}/variacoes/{vid}");
}
