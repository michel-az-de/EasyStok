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
    bool Ativo);

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
        c.WebhookVerificadoEm, c.UltimaMensagemRecebidaEm, c.Ativo);
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
