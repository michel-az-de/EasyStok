namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface ICupomRepository
    {
        /// <summary>Busca cupom pelo codigo (case-insensitive — implementacao normaliza).</summary>
        Task<Cupom?> GetByCodigoAsync(string codigo);
    }
}
