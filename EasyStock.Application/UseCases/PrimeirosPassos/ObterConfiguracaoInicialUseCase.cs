using EasyStock.Domain.Services;

namespace EasyStock.Application.UseCases.PrimeirosPassos;

/// <summary>Prontidão de um passo essencial no contrato: <c>{ok, falta[]}</c>.</summary>
public sealed record ProntidaoDoPassoResult(bool Ok, IReadOnlyList<string> Falta);

/// <summary>Um passo do assistente como o console lê (#2070).</summary>
/// <param name="Parte"><c>essencial</c>, <c>ajustes</c> ou <c>revisao</c>.</param>
/// <param name="Situacao"><c>conferido</c>, <c>depois</c> ou nulo quando ninguém marcou.</param>
public sealed record PassoDaConfiguracaoInicialResult(
    string Passo, string Parte, int Ordem, string? Situacao, DateTime? Em, string? PorNome,
    bool Disponivel, string? Motivo, ProntidaoDoPassoResult? Prontidao);

/// <summary>Estado da configuração inicial da empresa do JWT (#2070).</summary>
/// <param name="AbrirSozinho">Dona, pendente e não adiada: o console abre o assistente ao entrar.</param>
/// <param name="PodeConcluir">Dona ativa ou suporte global: só eles adiam e concluem.</param>
public sealed record ConfiguracaoInicialResult(
    bool Pendente, bool AbrirSozinho, bool EhDona, bool PodeConcluir, DateTime? ConcluidaEm, DateTime? AdiadaEm,
    IReadOnlyList<PassoDaConfiguracaoInicialResult> Passos);

/// <summary>
/// <c>GET api/configuracao-inicial</c> (#2070): marcas dos passos, prontidão calculada a cada leitura e o que esta pessoa
/// pode fazer. Só lê.
/// </summary>
public sealed class ObterConfiguracaoInicialUseCase(
    IEmpresaRepository empresas,
    IConfiguracaoInicialRepository passos,
    IRetratoDaConfiguracaoInicialQuery retratos,
    AutoridadeNaConfiguracaoInicial autoridade,
    ICurrentUserAccessor usuario)
{
    public async Task<ConfiguracaoInicialResult> ExecuteAsync(CancellationToken ct = default)
    {
        var empresaId = usuario.EmpresaId;
        UseCaseGuards.EnsureEmpresaId(empresaId);

        var empresa = await empresas.GetByIdAsync(empresaId)
            ?? throw new UseCaseValidationException("Empresa não encontrada.");
        var marcados = (await passos.ListarPassosAsync(empresaId, ct)).ToDictionary(p => p.Passo, StringComparer.Ordinal);
        var retrato = await retratos.ObterAsync(empresaId, ct);
        var quem = await autoridade.AvaliarAsync(usuario.UsuarioId, empresaId, ct);

        var pendente = empresa.ConfiguracaoInicialConcluidaEm is null;
        return new ConfiguracaoInicialResult(
            Pendente: pendente,
            AbrirSozinho: quem.EhDona && pendente && empresa.ConfiguracaoInicialAdiadaEm is null,
            EhDona: quem.EhDona,
            PodeConcluir: quem.PodeDecidir,
            ConcluidaEm: empresa.ConfiguracaoInicialConcluidaEm,
            AdiadaEm: empresa.ConfiguracaoInicialAdiadaEm,
            Passos: ConfiguracaoInicial.Passos.Select(p => Passo(p, marcados.GetValueOrDefault(p.Id), retrato)).ToList());
    }

    private PassoDaConfiguracaoInicialResult Passo(PassoDoCatalogo passo, PassoMarcado? marcado, RetratoDaConfiguracaoInicial retrato)
    {
        var disponibilidade = DisponibilidadeDosPassos.Avaliar(passo.Id, usuario);
        var prontidao = ProntidaoDaConfiguracaoInicial.Avaliar(passo.Id, retrato);
        return new PassoDaConfiguracaoInicialResult(
            passo.Id,
            passo.Parte.ToString().ToLowerInvariant(),
            passo.Ordem,
            marcado is null ? null : SituacoesDoPasso.Texto(marcado.Situacao),
            marcado?.Em,
            marcado?.PorNome,
            disponibilidade.Disponivel,
            disponibilidade.Motivo,
            prontidao is null ? null : new ProntidaoDoPassoResult(prontidao.Ok, prontidao.Falta));
    }
}
