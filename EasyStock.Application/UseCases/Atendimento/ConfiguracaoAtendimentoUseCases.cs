using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento;

public sealed record ConfiguracaoAtendimentoResult(
    Guid EmpresaId,
    string Tom,
    NivelSugestaoAtendimento NivelSugestao,
    string SaudacaoPrimeiroContato,
    string SaudacaoRetorno,
    string FraseEspera,
    string MensagemForaArea,
    int RespiroMinutos,
    int TempoPreparoPadraoMinutos,
    DateTime? WebhookVerificadoEm,
    DateTime? UltimaMensagemRecebidaEm,
    bool Ativo,
    string? ModeloRetomadaNome = null,
    string ModeloRetomadaIdioma = ConfiguracaoAtendimento.IdiomaModeloPadrao);

public sealed record ObterConfiguracaoAtendimentoQuery(Guid EmpresaId);

public sealed class ObterConfiguracaoAtendimentoUseCase(IConfiguracaoAtendimentoRepository repository)
{
    public async Task<ConfiguracaoAtendimentoResult> ExecuteAsync(ObterConfiguracaoAtendimentoQuery query)
    {
        var config = await repository.GetOrDefaultAsync(query.EmpresaId);
        return ToResult(config);
    }

    internal static ConfiguracaoAtendimentoResult ToResult(ConfiguracaoAtendimento c) => new(
        c.EmpresaId, c.Tom, c.NivelSugestao, c.SaudacaoPrimeiroContato, c.SaudacaoRetorno,
        c.FraseEspera, c.MensagemForaArea, c.RespiroMinutos, c.TempoPreparoPadraoMinutos,
        c.WebhookVerificadoEm, c.UltimaMensagemRecebidaEm, c.Ativo, c.ModeloRetomadaNome, c.ModeloRetomadaIdioma);
}

public sealed record AtualizarConfiguracaoAtendimentoCommand(
    Guid EmpresaId,
    string? Tom,
    NivelSugestaoAtendimento? NivelSugestao,
    string? SaudacaoPrimeiroContato,
    string? SaudacaoRetorno,
    string? FraseEspera,
    string? MensagemForaArea,
    int? RespiroMinutos,
    int? TempoPreparoPadraoMinutos,
    bool? Ativo);

public sealed class AtualizarConfiguracaoAtendimentoUseCase(
    IConfiguracaoAtendimentoRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<ConfiguracaoAtendimentoResult> ExecuteAsync(AtualizarConfiguracaoAtendimentoCommand command)
    {
        var configuracao = await repository.GetByEmpresaIdAsync(command.EmpresaId);
        var nova = configuracao is null;
        configuracao ??= ConfiguracaoAtendimento.CriarPadrao(command.EmpresaId);

        try
        {
            configuracao.Atualizar(
                command.Tom,
                command.NivelSugestao,
                command.SaudacaoPrimeiroContato,
                command.SaudacaoRetorno,
                command.FraseEspera,
                command.MensagemForaArea,
                command.RespiroMinutos,
                command.TempoPreparoPadraoMinutos,
                command.Ativo);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        if (nova) await repository.AddAsync(configuracao);
        else await repository.UpdateAsync(configuracao);

        await unitOfWork.CommitAsync();

        return ObterConfiguracaoAtendimentoUseCase.ToResult(configuracao);
    }
}

/// <summary>
/// S58 (#1391): define (ou limpa, com nome vazio) o modelo aprovado que reabre a conversa fora da janela de 24 h.
/// O nome tem de existir aprovado na Meta, com uma variável no corpo (o primeiro nome do cliente).
/// </summary>
public sealed class DefinirModeloRetomadaUseCase(IConfiguracaoAtendimentoRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<ConfiguracaoAtendimentoResult> ExecuteAsync(Guid empresaId, string? nome, string? idioma)
    {
        var configuracao = await repository.GetByEmpresaIdAsync(empresaId);
        var nova = configuracao is null;
        configuracao ??= ConfiguracaoAtendimento.CriarPadrao(empresaId);

        try
        {
            configuracao.DefinirModeloRetomada(nome, idioma);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        if (nova) await repository.AddAsync(configuracao);
        else await repository.UpdateAsync(configuracao);
        await unitOfWork.CommitAsync();

        return ObterConfiguracaoAtendimentoUseCase.ToResult(configuracao);
    }
}
