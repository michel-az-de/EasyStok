using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.UseCases.ListarPlanos
{
    public sealed record PlanoResult(Guid Id, string Nome, string? Descricao, int LimiteLojas, int LimiteUsuarios, int LimiteProdutos, decimal PrecoMensal);

    public class ListarPlanosUseCase(IPlanoRepository planoRepository)
    {
        public async Task<IEnumerable<PlanoResult>> ExecuteAsync()
        {
            var planos = await planoRepository.GetAtivosAsync();
            return planos.Select(p => new PlanoResult(p.Id, p.Nome, p.Descricao, p.LimiteLojas, p.LimiteUsuarios, p.LimiteProdutos, p.PrecoMensal));
        }
    }
}
