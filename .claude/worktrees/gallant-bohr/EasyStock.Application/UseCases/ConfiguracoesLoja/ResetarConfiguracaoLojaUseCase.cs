using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.ConfiguracoesLoja;

public sealed record ResetarConfiguracaoLojaCommand(Guid EmpresaId, Guid LojaId);

public class ResetarConfiguracaoLojaUseCase(
    ILojaRepository lojaRepository,
    IConfiguracaoLojaRepository configuracaoRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ConfiguracaoLojaResult> ExecuteAsync(ResetarConfiguracaoLojaCommand command)
    {
        var loja = await lojaRepository.GetByIdAsync(command.EmpresaId, command.LojaId)
            ?? throw new UseCaseValidationException("Loja nao encontrada.");

        var configuracao = await configuracaoRepository.GetByLojaIdAsync(loja.Id);
        var nova = configuracao is null;
        configuracao ??= Domain.Entities.ConfiguracaoLoja.CriarPadrao(loja.Id);
        configuracao.ResetarPadrao();

        if (nova) await configuracaoRepository.AddAsync(configuracao);
        else await configuracaoRepository.UpdateAsync(configuracao);

        await unitOfWork.CommitAsync();

        return new ConfiguracaoLojaResult(
            configuracao.LojaId,
            configuracao.DiasAlertaValidade,
            configuracao.DiasAlertaParado,
            configuracao.QuantidadeMinimaPadrao,
            configuracao.NotificarEstoqueCritico,
            configuracao.NotificarValidade,
            configuracao.NotificarParado,
            configuracao.NotificarReposicao,
            configuracao.FifoAtivo,
            configuracao.Moeda,
            configuracao.Timezone);
    }
}
