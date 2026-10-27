namespace EasyStock.Application.UseCases.PrimeirosPassos;

/// <summary>
/// <c>POST api/configuracao-inicial/adiar</c> (#2070): "continuar depois". Só a dona ativa ou o suporte global; a
/// Administração recebe <see cref="AutoridadeNaConfiguracaoInicial.CodigoHierarquia"/>.
/// </summary>
public sealed class AdiarConfiguracaoInicialUseCase(
    IEmpresaRepository empresas,
    AutoridadeNaConfiguracaoInicial autoridade,
    ICurrentUserAccessor usuario,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var empresa = await EmpresaDeQuemDecide.ObterAsync(empresas, autoridade, usuario, "adiar", ct);
        empresa.AdiarConfiguracaoInicial(relogio.GetUtcNow().UtcDateTime);
        await unitOfWork.CommitAsync();
    }
}

/// <summary>
/// <c>POST api/configuracao-inicial/concluir</c> (#2070): conclui mesmo com passos "depois". Só a dona ativa ou o
/// suporte global; concluir de novo não muda a primeira conclusão.
/// </summary>
public sealed class ConcluirConfiguracaoInicialUseCase(
    IEmpresaRepository empresas,
    AutoridadeNaConfiguracaoInicial autoridade,
    ICurrentUserAccessor usuario,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var empresa = await EmpresaDeQuemDecide.ObterAsync(empresas, autoridade, usuario, "concluir", ct);
        empresa.ConcluirConfiguracaoInicial(usuario.UsuarioId, relogio.GetUtcNow().UtcDateTime);
        await unitOfWork.CommitAsync();
    }
}

internal static class EmpresaDeQuemDecide
{
    /// <summary>
    /// Empresa do JWT, rastreada, depois de conferir quem decide. Sem <c>UpdateAsync</c>: o commit grava só as colunas
    /// que o método de domínio mudou, e uma alteração paralela de outro campo da empresa não é sobrescrita.
    /// </summary>
    public static async Task<Empresa> ObterAsync(
        IEmpresaRepository empresas, AutoridadeNaConfiguracaoInicial autoridade, ICurrentUserAccessor usuario,
        string acao, CancellationToken ct)
    {
        UseCaseGuards.EnsureEmpresaId(usuario.EmpresaId);
        await autoridade.ExigirQuemDecideAsync(usuario.UsuarioId, usuario.EmpresaId, acao, ct);
        return await empresas.GetByIdAsync(usuario.EmpresaId)
            ?? throw new UseCaseValidationException("Empresa não encontrada.");
    }
}
