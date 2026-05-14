using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.Validators;
using EasyStock.Domain.ValueObjects;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;

namespace EasyStock.Application.UseCases.Etiquetas;

public sealed record SalvarFichaTecnicaResult(Guid ProdutoId, string AtributosJson);

public class SalvarFichaTecnicaUseCase(
    IProdutoRepository produtoRepo,
    IAuditLogRepository auditRepo,
    IUnitOfWork uow)
{
    private static readonly ProdutoFichaTecnicaValidator Validator = new();

    public async Task<SalvarFichaTecnicaResult?> ExecuteAsync(
        ProdutoFichaTecnicaCommand cmd,
        Guid? operadorId, string? ip, string? userAgent)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var produto = await produtoRepo.GetByIdAsync(cmd.EmpresaId, cmd.ProdutoId);
        if (produto == null) return null;

        var validationResult = await Validator.ValidateAsync(cmd);
        if (!validationResult.IsValid)
            throw new UseCaseValidationException(string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));

        var ficha = new ProdutoFichaTecnica
        {
            PorcaoG          = cmd.PorcaoG,
            Kcal             = cmd.Kcal,
            CarbsG           = cmd.CarbsG,
            ProteinaG        = cmd.ProteinaG,
            GorduraG         = cmd.GorduraG,
            GorduraSaturadaG = cmd.GorduraSaturadaG,
            FibrasG          = cmd.FibrasG,
            SodioMg          = cmd.SodioMg,
            ModoPreparo      = cmd.ModoPreparo,
            Ingredientes     = cmd.Ingredientes ?? [],
            Alergenos        = cmd.Alergenos ?? [],
            AlergenosOutros  = cmd.AlergenosOutros,
        };

        produto.AtributosJson = ficha.ToJson();
        await produtoRepo.UpdateAsync(produto);

        if (operadorId.HasValue)
        {
            await auditRepo.AddAsync(AuditLogEntity.Criar(
                operadorId.Value, "produto-ficha-tecnica.alterada", true,
                $"ProdutoId: {produto.Id}", ip, userAgent));
        }

        await uow.CommitAsync();
        return new(produto.Id, produto.AtributosJson);
    }
}
