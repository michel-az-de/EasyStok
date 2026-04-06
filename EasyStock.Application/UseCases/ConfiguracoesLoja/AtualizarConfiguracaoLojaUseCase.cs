using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ConfiguracoesLoja;

public sealed record AtualizarConfiguracaoLojaCommand(
    Guid EmpresaId,
    Guid LojaId,
    int? DiasAlertaValidade,
    int? DiasAlertaParado,
    int? QuantidadeMinimaPadrao,
    bool? NotificarEstoqueCritico,
    bool? NotificarValidade,
    bool? NotificarParado,
    bool? NotificarReposicao,
    bool? FifoAtivo,
    string? Moeda,
    string? Timezone);

public class AtualizarConfiguracaoLojaUseCase(
    ILojaRepository lojaRepository,
    IConfiguracaoLojaRepository configuracaoRepository,
    IUnitOfWork unitOfWork,
    ILogger<AtualizarConfiguracaoLojaUseCase> logger)
{
    public async Task<ConfiguracaoLojaResult> ExecuteAsync(AtualizarConfiguracaoLojaCommand command)
    {
        logger.LogInformation("Atualizando configuracao da loja {LojaId} na empresa {EmpresaId}.", command.LojaId, command.EmpresaId);

        var loja = await lojaRepository.GetByIdAsync(command.EmpresaId, command.LojaId)
            ?? throw new UseCaseValidationException("Loja nao encontrada.");

        var configuracao = await configuracaoRepository.GetByLojaIdAsync(loja.Id);
        var nova = configuracao is null;
        configuracao ??= Domain.Entities.ConfiguracaoLoja.CriarPadrao(loja.Id);

        configuracao.Atualizar(
            command.DiasAlertaValidade,
            command.DiasAlertaParado,
            command.QuantidadeMinimaPadrao,
            command.NotificarEstoqueCritico,
            command.NotificarValidade,
            command.NotificarParado,
            command.NotificarReposicao,
            command.FifoAtivo,
            command.Moeda,
            command.Timezone);

        if (nova) await configuracaoRepository.AddAsync(configuracao);
        else await configuracaoRepository.UpdateAsync(configuracao);

        await unitOfWork.CommitAsync();

        logger.LogInformation("Configuracao da loja {LojaId} {Acao} com sucesso.", command.LojaId, nova ? "criada" : "atualizada");

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
