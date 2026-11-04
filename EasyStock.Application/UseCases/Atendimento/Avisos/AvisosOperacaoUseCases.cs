using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Avisos;

/// <summary>A tela Avisos (#2096): as preferências da loja e o SLA, que é o tempo do cliente esperando.</summary>
public sealed record AvisosOperacaoResult(
    bool ClienteSemResposta,
    bool PedidoAtrasado,
    bool ImpressaoTravada,
    bool CaixaEsquecido,
    int ProntoSemEntregadorMinutos,
    int EncomendaProximaHoras,
    int EncomendaSemConfirmacaoHoras,
    int EstoqueVencendoDias,
    int EstoqueParadoDias,
    bool ReclamacaoCliente,
    bool SuspeitaAtendimento,
    int SlaRespostaMinutos)
{
    public static AvisosOperacaoResult De(ConfiguracaoAtendimento c) => new(
        c.Avisos.ClienteSemResposta, c.Avisos.PedidoAtrasado, c.Avisos.ImpressaoTravada, c.Avisos.CaixaEsquecido,
        c.Avisos.ProntoSemEntregadorMinutos, c.Avisos.EncomendaProximaHoras, c.Avisos.EncomendaSemConfirmacaoHoras,
        c.Avisos.EstoqueVencendoDias, c.Avisos.EstoqueParadoDias, c.Avisos.ReclamacaoCliente, c.Avisos.SuspeitaAtendimento,
        c.SlaRespostaMinutos);
}

public sealed class ObterAvisosOperacaoUseCase(IConfiguracaoAtendimentoRepository repository)
{
    public async Task<AvisosOperacaoResult> ExecuteAsync(Guid empresaId)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        return AvisosOperacaoResult.De(await repository.GetOrDefaultAsync(empresaId));
    }
}

/// <summary>Grava o documento inteiro: a tela manda todos os campos. Fora da faixa vira 400 com a frase do domínio.</summary>
public sealed class SalvarAvisosOperacaoUseCase(IConfiguracaoAtendimentoRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<AvisosOperacaoResult> ExecuteAsync(Guid empresaId, AvisosOperacao avisos)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        ArgumentNullException.ThrowIfNull(avisos);

        var configuracao = await repository.GetByEmpresaIdAsync(empresaId);
        ObterConfiguracaoAtendimentoUseCase.ValidarEmpresa(configuracao, empresaId);
        var nova = configuracao is null;
        configuracao ??= ConfiguracaoAtendimento.CriarPadrao(empresaId);

        try
        {
            configuracao.DefinirAvisos(avisos);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        if (nova) await repository.AddAsync(configuracao);
        else await repository.UpdateAsync(configuracao);
        await unitOfWork.CommitAsync();
        return AvisosOperacaoResult.De(configuracao);
    }
}
