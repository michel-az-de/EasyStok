using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.RegistrarMovimentoCaixa;

public sealed record RegistrarMovimentoCaixaCommand(
    [property: Required] Guid EmpresaId,
    [property: Required][property: MaxLength(20)] string Tipo,        // "entrada" | "saida"
    decimal Valor,
    string? Descricao = null,
    Guid? LojaId = null,
    [property: MaxLength(20)] string? Metodo = null,
    [property: MaxLength(60)] string? Categoria = null,
    [property: MaxLength(120)] string? Referencia = null,
    DateTime? DataMovimento = null,
    Guid? RegistradoPorUserId = null,
    [property: MaxLength(120)] string? RegistradoPorNome = null,
    [property: MaxLength(20)] string? Origem = "web");

public class RegistrarMovimentoCaixaUseCase(
    ICaixaRepository repo,
    IUnitOfWork uow,
    ILogger<RegistrarMovimentoCaixaUseCase> logger)
{
    public async Task<MovimentoCaixaResult> ExecuteAsync(RegistrarMovimentoCaixaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var tipo = (cmd.Tipo ?? "").Trim().ToLowerInvariant();
        if (tipo != "entrada" && tipo != "saida")
            throw new UseCaseValidationException("Tipo deve ser 'entrada' ou 'saida'.");

        if (cmd.Valor <= 0)
            throw new UseCaseValidationException("Valor deve ser maior que zero.");

        var dataMov = cmd.DataMovimento ?? DateTime.UtcNow;
        var data = DateOnly.FromDateTime(dataMov);

        // Bloquear lançamento em dia já fechado (preserva integridade do snapshot).
        var fechamento = await repo.GetFechamentoDoDiaAsync(cmd.EmpresaId, data, cmd.LojaId);
        if (fechamento != null)
            throw new UseCaseValidationException("Caixa do dia já foi fechado. Lance em outra data ou faça estorno.");

        var mov = MovimentoCaixa.Criar(cmd.EmpresaId, tipo, cmd.Valor, dataMov, cmd.LojaId);
        mov.Descricao = cmd.Descricao;
        mov.Metodo = cmd.Metodo;
        mov.Categoria = cmd.Categoria;
        mov.Referencia = cmd.Referencia;
        mov.RegistradoPorUserId = cmd.RegistradoPorUserId;
        mov.RegistradoPorNome = cmd.RegistradoPorNome;
        mov.Origem = cmd.Origem;

        await repo.AddMovimentoAsync(mov);
        await uow.CommitAsync();

        logger.LogInformation("Movimento de caixa {Id} ({Tipo} {Valor}) registrado.", mov.Id, tipo, cmd.Valor);
        return AbrirCaixaUseCase.Map(mov);
    }
}
