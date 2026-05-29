namespace EasyStock.Application.UseCases.AtualizarLoja;

public sealed record AtualizarLojaCommand(
    Guid LojaId,
    [property: Required] Guid EmpresaId,
    [property: Required][property: MaxLength(150)] string Nome,
    [property: MaxLength(500)] string? Descricao,
    [property: MaxLength(30)] string? Documento,
    [property: MaxLength(300)] string? Endereco,
    [property: MaxLength(20)] string? Telefone);

public class AtualizarLojaUseCase(
    ILojaRepository lojaRepository,
    IUnitOfWork unitOfWork,
    ILogger<AtualizarLojaUseCase> logger)
{
    public async Task ExecuteAsync(AtualizarLojaCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Nome))
            throw new UseCaseValidationException("Nome da loja é obrigatório.");

        var loja = await lojaRepository.GetByIdAsync(command.LojaId);
        if (loja is null || loja.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Loja nao encontrada.");

        loja.Nome = command.Nome.Trim();
        loja.Descricao = command.Descricao?.Trim();
        loja.Documento = command.Documento?.Trim();
        loja.Endereco = command.Endereco?.Trim();
        loja.Telefone = command.Telefone?.Trim();
        loja.AlteradoEm = DateTime.UtcNow;

        await lojaRepository.UpdateAsync(loja);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Loja {LojaId} atualizada.", loja.Id);
    }
}
