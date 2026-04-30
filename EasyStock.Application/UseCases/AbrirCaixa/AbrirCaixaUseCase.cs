using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AbrirCaixa;

public sealed record AbrirCaixaCommand(
    [property: Required] Guid EmpresaId,
    decimal SaldoInicial = 0m,
    Guid? LojaId = null,
    DateTime? DataMovimento = null,
    Guid? RegistradoPorUserId = null,
    [property: MaxLength(120)] string? RegistradoPorNome = null,
    [property: MaxLength(20)] string? Origem = "web",
    string? Observacoes = null);

public class AbrirCaixaUseCase(
    ICaixaRepository repo,
    IUnitOfWork uow,
    ILogger<AbrirCaixaUseCase> logger)
{
    public async Task<MovimentoCaixaResult> ExecuteAsync(AbrirCaixaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.SaldoInicial < 0)
            throw new UseCaseValidationException("Saldo inicial não pode ser negativo.");

        var dataMov = cmd.DataMovimento ?? DateTime.UtcNow;
        var data = DateOnly.FromDateTime(dataMov);

        // Não permitir abrir caixa do mesmo dia se já foi fechado.
        var fechamento = await repo.GetFechamentoDoDiaAsync(cmd.EmpresaId, data, cmd.LojaId);
        if (fechamento != null)
            throw new UseCaseValidationException("Caixa do dia já foi fechado. Não é possível reabrir.");

        var mov = MovimentoCaixa.Criar(cmd.EmpresaId, "abertura", cmd.SaldoInicial, dataMov, cmd.LojaId);
        mov.Descricao = cmd.Observacoes;
        mov.RegistradoPorUserId = cmd.RegistradoPorUserId;
        mov.RegistradoPorNome = cmd.RegistradoPorNome;
        mov.Origem = cmd.Origem;

        await repo.AddMovimentoAsync(mov);
        await uow.CommitAsync();

        logger.LogInformation("Caixa aberto para {Data} (saldo inicial={Saldo}).", data, cmd.SaldoInicial);
        return Map(mov);
    }

    internal static MovimentoCaixaResult Map(MovimentoCaixa m) => new(
        m.Id, m.EmpresaId, m.LojaId, m.Tipo, m.Valor, m.Descricao,
        m.Metodo, m.Categoria, m.Referencia, m.DataMovimento,
        m.RegistradoPorUserId, m.RegistradoPorNome, m.Origem,
        m.EstornadoEm, m.EstornadoPorUserId, m.EstornadoPorNome, m.MotivoEstorno,
        m.CriadoEm, m.Ativo);
}
