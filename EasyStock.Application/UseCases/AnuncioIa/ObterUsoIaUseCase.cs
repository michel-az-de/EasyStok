namespace EasyStock.Application.UseCases.AnuncioIa
{
    public sealed record ObterUsoIaQuery(Guid EmpresaId);

    public sealed record UsoIaResult(
        Guid EmpresaId,
        int Ano,
        int Mes,
        int TotalGeracoes,
        int TotalTokens,
        int? LimiteMensal,
        bool Ilimitado);

    public class ObterUsoIaUseCase(
        IUsoIaRepository usoIaRepository,
        IAssinaturaEmpresaRepository assinaturaRepository)
    {
        public async Task<UsoIaResult> ExecuteAsync(ObterUsoIaQuery query)
        {
            UseCaseGuards.EnsureEmpresaId(query.EmpresaId);

            var agora = DateTime.UtcNow;
            var uso = await usoIaRepository.GetAsync(query.EmpresaId, agora.Year, agora.Month);

            var assinatura = await assinaturaRepository.GetAtivaAsync(query.EmpresaId);
            var plano = assinatura?.Plano;

            int? limite = null;
            var ilimitado = true;

            if (plano != null && !plano.GeracoesIaSaoIlimitadas)
            {
                limite = plano.LimiteGeracoesIaMensais;
                ilimitado = false;
            }

            return new UsoIaResult(
                query.EmpresaId,
                agora.Year,
                agora.Month,
                uso?.TotalGeracoes ?? 0,
                uso?.TotalTokens ?? 0,
                limite,
                ilimitado);
        }
    }
}
