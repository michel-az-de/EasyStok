using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.EstornarMovimentoCaixa;

public sealed record EstornarMovimentoCaixaCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid Id,
    string? Motivo = null,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null);

public class EstornarMovimentoCaixaUseCase(
    ICaixaRepository repo,
    IUnitOfWork uow,
    ILogger<EstornarMovimentoCaixaUseCase> logger)
{
    public async Task<MovimentoCaixaResult?> ExecuteAsync(EstornarMovimentoCaixaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        var mov = await repo.GetMovimentoAsync(cmd.EmpresaId, cmd.Id);
        if (mov == null) return null;
        if (mov.EstornadoEm != null) return AbrirCaixaUseCase.Map(mov);

        // Bloquear estorno se o dia já foi fechado.
        var data = DateOnly.FromDateTime(mov.DataMovimento);
        var fechamento = await repo.GetFechamentoDoDiaAsync(mov.EmpresaId, data, mov.LojaId);
        if (fechamento != null)
            throw new UseCaseValidationException("Não é possível estornar movimento de dia já fechado.");

        mov.Estornar(cmd.UsuarioId, cmd.UsuarioNome, cmd.Motivo);
        await repo.UpdateMovimentoAsync(mov);
        await uow.CommitAsync();

        logger.LogInformation("Movimento {Id} estornado (motivo={Motivo}).", mov.Id, cmd.Motivo ?? "—");
        return AbrirCaixaUseCase.Map(mov);
    }
}
