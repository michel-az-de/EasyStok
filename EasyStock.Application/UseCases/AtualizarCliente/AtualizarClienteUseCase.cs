using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Cliente;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarCliente;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;
using ClienteAlteracao = EasyStock.Domain.Entities.ClienteAlteracao;

namespace EasyStock.Application.UseCases.AtualizarCliente;

public sealed record AtualizarClienteCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid Id,
    [property: Required][property: MaxLength(150)] string Nome,
    [property: MaxLength(32)]  string? Apt = null,
    [property: MaxLength(255)] string? Endereco = null,
    [property: MaxLength(32)]  string? Telefone = null,
    [property: MaxLength(255)] string? Email = null,
    [property: MaxLength(30)]  string? Documento = null,
    string? Observacoes = null,
    Guid? AlteradoPorUserId = null,
    string? AlteradoPorNome = null,
    string? Origem = "web");

public class AtualizarClienteUseCase(
    IClienteRepository repo,
    IUnitOfWork uow,
    ILogger<AtualizarClienteUseCase> logger)
{
    public async Task<ClienteResult?> ExecuteAsync(AtualizarClienteCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var cliente = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id);
        if (cliente == null) return null;

        if (!string.IsNullOrWhiteSpace(cmd.Email))
            EmailValidator.EnsureValid(cmd.Email, "Email do cliente");

        var docNormalizado = Cnpj.TryFrom(cmd.Documento)?.Value ?? cmd.Documento;
        var telNormalizado = Telefone.TryFrom(cmd.Telefone)?.Value ?? cmd.Telefone;

        // Diff campo-a-campo pra audit log
        var diffs = BuildDiff(cliente, cmd, docNormalizado, telNormalizado);

        cliente.AtualizarCadastro(
            cmd.Nome, cmd.Apt, cmd.Endereco,
            telNormalizado, cmd.Email, docNormalizado, cmd.Observacoes);

        foreach (var (campo, antigo, novo) in diffs)
        {
            await repo.AddAlteracaoAsync(new ClienteAlteracao
            {
                Id = Guid.NewGuid(),
                ClienteId = cliente.Id,
                AlteradoPorUserId = cmd.AlteradoPorUserId,
                AlteradoPorNome = cmd.AlteradoPorNome,
                Campo = campo,
                ValorAntigo = antigo,
                ValorNovo = novo,
                AlteradoEm = DateTime.UtcNow,
                Origem = cmd.Origem
            });
        }

        await repo.UpdateAsync(cliente);
        await uow.CommitAsync();

        logger.LogInformation("Cliente {Id} atualizado ({Diffs} campos).", cliente.Id, diffs.Count);
        return CriarClienteUseCase.Map(cliente);
    }

    private static List<(string Campo, string? Antigo, string? Novo)> BuildDiff(
        ClienteEntity atual, AtualizarClienteCommand cmd, string? docNormalizado, string? telNormalizado)
    {
        var diffs = new List<(string, string?, string?)>();
        Compare("Nome",        atual.Nome,        cmd.Nome,           diffs);
        Compare("Apt",         atual.Apt,         cmd.Apt,            diffs);
        Compare("Endereco",    atual.Endereco,    cmd.Endereco,       diffs);
        Compare("Telefone",    atual.Telefone,    telNormalizado,     diffs);
        Compare("Email",       atual.Email,       cmd.Email,          diffs);
        Compare("Documento",   atual.Documento,   docNormalizado,     diffs);
        Compare("Observacoes", atual.Observacoes, cmd.Observacoes,    diffs);
        return diffs;
    }

    private static void Compare(string campo, string? antigo, string? novo, List<(string, string?, string?)> acc)
    {
        var a = string.IsNullOrWhiteSpace(antigo) ? null : antigo;
        var n = string.IsNullOrWhiteSpace(novo) ? null : novo;
        if (a != n) acc.Add((campo, a, n));
    }
}
