using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Loja;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using LojaEntity = EasyStock.Domain.Entities.Loja;

namespace EasyStock.Application.UseCases.CriarLoja;

public sealed record CriarLojaCommand(
    [property: Required] Guid EmpresaId,
    [property: Required][property: MaxLength(150)] string Nome,
    [property: MaxLength(500)] string? Descricao,
    [property: MaxLength(30)] string? Documento,
    [property: MaxLength(300)] string? Endereco,
    [property: MaxLength(20)] string? Telefone);

public class CriarLojaUseCase(
    ILojaRepository lojaRepository,
    IAssinaturaEmpresaRepository assinaturaRepository,
    IUnitOfWork unitOfWork,
    ILogger<CriarLojaUseCase> logger)
{
    public async Task<LojaResult> ExecuteAsync(CriarLojaCommand command)
    {
        var assinatura = await assinaturaRepository.GetAtivaAsync(command.EmpresaId);

        if (assinatura is not null && !assinatura.Plano!.LojasSaoIlimitadas)
        {
            var count = await lojaRepository.CountByEmpresaAsync(command.EmpresaId);
            if (count >= assinatura.Plano.LimiteLojas)
                throw new PlanoLimiteAtingidoException("lojas");
        }

        var loja = LojaEntity.Criar(command.EmpresaId, command.Nome.Trim());
        loja.Descricao = command.Descricao?.Trim();
        loja.Documento = command.Documento?.Trim();
        loja.Endereco = command.Endereco?.Trim();
        loja.Telefone = command.Telefone?.Trim();

        await lojaRepository.AddAsync(loja);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Loja {LojaId} criada para empresa {EmpresaId}.", loja.Id, loja.EmpresaId);

        return new LojaResult(loja.Id, loja.EmpresaId, loja.Nome, loja.Ativa);
    }
}
