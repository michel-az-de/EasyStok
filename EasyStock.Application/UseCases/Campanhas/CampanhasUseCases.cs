using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;

namespace EasyStock.Application.UseCases.Campanhas;

public sealed record CampanhaResult(
    Guid Id,
    string Nome,
    string Mensagem,
    string? ImagemUrl,
    string? TemplateMeta,
    FiltroCampanha Filtro,
    IReadOnlyList<string> TagsRestricaoExcluidas,
    StatusCampanha Status,
    DateTime? DisparoEm,
    DateTime? EncerramentoEm,
    bool EnviarLembreteEncerramento,
    int? TamanhoOnda,
    int OndaAtual,
    DateTime CriadaEm,
    Guid CriadaPorUsuarioId)
{
    public static CampanhaResult De(Campanha c) => new(
        c.Id, c.Nome, c.Mensagem, c.ImagemUrl, c.TemplateMeta, c.Filtro, c.TagsRestricao, c.Status, c.DisparoEm,
        c.EncerramentoEm, c.EnviarLembreteEncerramento, c.TamanhoOnda, c.OndaAtual, c.CriadaEm, c.CriadaPorUsuarioId);
}

public sealed record CancelarCampanhaResult(CampanhaResult Campanha, int DestinatariosExcluidos);

/// <summary>Vira 404 na API: inexistente ou de outra empresa (não vaza existência).</summary>
public sealed class CampanhaNaoEncontradaException(Guid id)
    : Exception($"Campanha {id} não encontrada.");

/// <summary>
/// Conteúdo da campanha e, quando informado, o disparo. Sem <see cref="DisparoEm"/> a campanha fica
/// (ou volta a ser) rascunho.
/// </summary>
public sealed record SalvarCampanhaCommand(Guid EmpresaId, Guid UsuarioId, DadosCampanha Dados, DateTime? DisparoEm);

/// <summary>S28: cria a campanha; com disparo, já agenda. Regra violada sobe como <see cref="RegraDeDominioVioladaException"/>.</summary>
public sealed class CriarCampanhaUseCase(ICampanhaRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<CampanhaResult> ExecuteAsync(SalvarCampanhaCommand command, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var campanha = Campanha.Criar(command.EmpresaId, command.UsuarioId, command.Dados, agora);
        if (command.DisparoEm is { } disparo) campanha.Agendar(disparo, agora);

        await repository.AddAsync(campanha, ct);
        await unitOfWork.CommitAsync();
        return CampanhaResult.De(campanha);
    }
}

/// <summary>
/// S28: edita rascunho ou agendada. Sem disparo, desagenda antes de aplicar (rascunho aceita
/// conteúdo incompleto); com disparo, aplica e reagenda, o que confere tudo de novo.
/// </summary>
public sealed class AtualizarCampanhaUseCase(ICampanhaRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<CampanhaResult> ExecuteAsync(Guid campanhaId, SalvarCampanhaCommand command, CancellationToken ct = default)
    {
        var campanha = await repository.ObterAsync(command.EmpresaId, campanhaId, ct)
            ?? throw new CampanhaNaoEncontradaException(campanhaId);

        if (command.DisparoEm is null && campanha.Status == StatusCampanha.Agendada) campanha.Desagendar();
        campanha.Atualizar(command.Dados);
        if (command.DisparoEm is { } disparo) campanha.Agendar(disparo, relogio.GetUtcNow().UtcDateTime);

        await unitOfWork.CommitAsync();
        return CampanhaResult.De(campanha);
    }
}

public sealed class ObterCampanhaUseCase(ICampanhaRepository repository)
{
    public async Task<CampanhaResult> ExecuteAsync(Guid empresaId, Guid campanhaId, CancellationToken ct = default) =>
        CampanhaResult.De(await repository.ObterAsync(empresaId, campanhaId, ct)
            ?? throw new CampanhaNaoEncontradaException(campanhaId));
}

public sealed class ListarCampanhasUseCase(ICampanhaRepository repository)
{
    public const int LimitePadrao = 100;

    public async Task<IReadOnlyList<CampanhaResult>> ExecuteAsync(Guid empresaId, StatusCampanha? status, CancellationToken ct = default) =>
        (await repository.ListarAsync(empresaId, status, LimitePadrao, ct)).Select(CampanhaResult.De).ToList();
}

/// <summary>S28: cancela; pendentes viram excluídos com motivo "cancelada", enviados ficam como estão.</summary>
public sealed class CancelarCampanhaUseCase(ICampanhaRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<CancelarCampanhaResult> ExecuteAsync(Guid empresaId, Guid campanhaId, CancellationToken ct = default)
    {
        var campanha = await repository.ObterAsync(empresaId, campanhaId, ct)
            ?? throw new CampanhaNaoEncontradaException(campanhaId);

        var pendentes = await repository.ListarPendentesAsync(empresaId, campanhaId, ct);
        var excluidos = campanha.Cancelar(pendentes);

        await unitOfWork.CommitAsync();
        return new CancelarCampanhaResult(CampanhaResult.De(campanha), excluidos);
    }
}
