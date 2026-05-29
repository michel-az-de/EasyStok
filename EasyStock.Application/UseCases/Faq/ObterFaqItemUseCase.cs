using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Application.UseCases.Faq
{
    public sealed record ObterFaqItemQuery(
        string CategoriaSlug,
        string ItemSlug,
        string? Ip,
        string? Termo,
        string? Origem,
        bool RegistrarVisualizacao = true);

    public sealed class ObterFaqItemUseCase(IFaqRepository faqRepo, IUnitOfWork uow)
    {
        public async Task<FaqItemDetalheDto> ExecuteAsync(ObterFaqItemQuery query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query.CategoriaSlug))
                throw new UseCaseValidationException("Categoria invalida.");
            if (string.IsNullOrWhiteSpace(query.ItemSlug))
                throw new UseCaseValidationException("Item invalido.");

            var item = await faqRepo.ObterPorSlugAsync(query.CategoriaSlug, query.ItemSlug, ct);
            if (item is null)
                throw new UseCaseValidationException("Item de FAQ nao encontrado.");

            if (query.RegistrarVisualizacao)
            {
                var ipHash = HashIp(query.Ip);
                var visualizacao = FaqVisualizacao.Criar(item.Id, ipHash, query.Termo, query.Origem);
                await faqRepo.RegistrarVisualizacaoAsync(visualizacao, ct);
                await faqRepo.IncrementarContadoresAsync(item.Id, deltaVisualizacao: 1, deltaUtil: 0, deltaNaoUtil: 0, ct);
                await uow.CommitAsync();
            }

            return new FaqItemDetalheDto(
                item.Id,
                item.CategoriaId,
                item.Categoria?.Nome ?? string.Empty,
                item.Categoria?.Slug ?? string.Empty,
                item.Titulo,
                item.Slug,
                item.Conteudo,
                item.Tags,
                item.Status,
                item.PublicadoEm,
                item.AtualizadoEm,
                item.Visualizacoes + (query.RegistrarVisualizacao ? 1 : 0),
                item.UtilCount,
                item.NaoUtilCount);
        }

        internal static string HashIp(string? ip)
        {
            var src = string.IsNullOrWhiteSpace(ip) ? "anon" : ip;
            var bytes = Encoding.UTF8.GetBytes(src + "|easystok-faq-salt");
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
