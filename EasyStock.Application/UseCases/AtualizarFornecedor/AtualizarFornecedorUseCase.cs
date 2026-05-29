using EasyStock.Domain.ValueObjects;
using FornecedorEntity = EasyStock.Domain.Entities.Fornecedor;

namespace EasyStock.Application.UseCases.AtualizarFornecedor;

public sealed record AtualizarFornecedorCommand(
    Guid FornecedorId,
    Guid EmpresaId,
    string Nome,
    string? Documento,
    string? Email,
    string? Telefone,
    string? Contato,
    string? Categoria,
    string? Tipo,
    int? LeadTimeEstimadoDias,
    string? SiteUrl,
    string? PedidoMinimo,
    string? FretePadrao,
    string? Observacoes,
    Guid? AlteradoPorUserId = null,
    string? AlteradoPorNome = null,
    string? Origem = "web");

public class AtualizarFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IUnitOfWork unitOfWork,
    ILogger<AtualizarFornecedorUseCase> logger)
{
    public async Task ExecuteAsync(AtualizarFornecedorCommand command)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        if (string.IsNullOrWhiteSpace(command.Nome))
            throw new UseCaseValidationException("Nome do fornecedor é obrigatório.");
        if (!string.IsNullOrWhiteSpace(command.Email))
            EmailValidator.EnsureValid(command.Email, "Email do fornecedor");

        var fornecedor = await fornecedorRepository.GetByIdAsync(command.EmpresaId, command.FornecedorId);
        if (fornecedor is null)
            throw new UseCaseValidationException("Fornecedor nao encontrado.");

        // Normalização via VOs: se o formato for válido, armazena só os dígitos;
        // se for inválido, mantém o input original (tolerância para dados legados).
        var documentoNormalizado = Cnpj.TryFrom(command.Documento)?.Value ?? command.Documento;
        var telefoneNormalizado  = Telefone.TryFrom(command.Telefone)?.Value ?? command.Telefone;

        // Onda P4 — diff campo-a-campo pra audit log.
        var diffs = BuildDiff(fornecedor, command, documentoNormalizado, telefoneNormalizado);

        fornecedor.AtualizarCadastro(
            command.Nome,
            documentoNormalizado,
            command.Email,
            telefoneNormalizado,
            command.Contato,
            command.Categoria,
            command.Tipo,
            command.LeadTimeEstimadoDias,
            command.SiteUrl,
            command.PedidoMinimo,
            command.FretePadrao,
            command.Observacoes);

        foreach (var (campo, antigo, novo) in diffs)
        {
            await fornecedorRepository.AddAlteracaoAsync(new FornecedorAlteracao
            {
                Id = Guid.NewGuid(),
                FornecedorId = fornecedor.Id,
                AlteradoPorUserId = command.AlteradoPorUserId,
                AlteradoPorNome = command.AlteradoPorNome,
                Campo = campo,
                ValorAntigo = antigo,
                ValorNovo = novo,
                AlteradoEm = DateTime.UtcNow,
                Origem = command.Origem
            });
        }

        await fornecedorRepository.UpdateAsync(fornecedor);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Fornecedor {FornecedorId} atualizado ({Diffs} campos).", fornecedor.Id, diffs.Count);
    }

    private static List<(string Campo, string? Antigo, string? Novo)> BuildDiff(
        FornecedorEntity atual, AtualizarFornecedorCommand cmd,
        string? docNormalizado, string? telNormalizado)
    {
        var diffs = new List<(string, string?, string?)>();
        Compare("Nome",                atual.Nome,                  cmd.Nome,                                       diffs);
        Compare("Documento",           atual.Documento,             docNormalizado,                                 diffs);
        Compare("Email",               atual.Email,                 cmd.Email,                                      diffs);
        Compare("Telefone",            atual.Telefone,              telNormalizado,                                 diffs);
        Compare("Contato",             atual.Contato,               cmd.Contato,                                    diffs);
        Compare("Categoria",           atual.Categoria,             cmd.Categoria,                                  diffs);
        Compare("Tipo",                atual.Tipo,                  cmd.Tipo,                                       diffs);
        Compare("LeadTimeEstimadoDias",atual.LeadTimeEstimadoDias?.ToString(), cmd.LeadTimeEstimadoDias?.ToString(),diffs);
        Compare("SiteUrl",             atual.SiteUrl,               cmd.SiteUrl,                                    diffs);
        Compare("PedidoMinimo",        atual.PedidoMinimo,          cmd.PedidoMinimo,                               diffs);
        Compare("FretePadrao",         atual.FretePadrao,           cmd.FretePadrao,                                diffs);
        Compare("Observacoes",         atual.Observacoes,           cmd.Observacoes,                                diffs);
        return diffs;
    }

    private static void Compare(string campo, string? antigo, string? novo, List<(string, string?, string?)> acc)
    {
        var a = string.IsNullOrWhiteSpace(antigo) ? null : antigo;
        var n = string.IsNullOrWhiteSpace(novo) ? null : novo;
        if (a != n) acc.Add((campo, a, n));
    }
}
