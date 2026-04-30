using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Cliente;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.UseCases.CriarCliente;

public sealed record CriarClienteCommand(
    [property: Required] Guid EmpresaId,
    [property: Required][property: MaxLength(150)] string Nome,
    [property: MaxLength(32)]  string? Apt = null,
    [property: MaxLength(255)] string? Endereco = null,
    [property: MaxLength(32)]  string? Telefone = null,
    [property: MaxLength(255)] string? Email = null,
    [property: MaxLength(30)]  string? Documento = null,
    string? Observacoes = null);

public class CriarClienteUseCase(
    IClienteRepository repo,
    IUnitOfWork uow,
    ILogger<CriarClienteUseCase> logger)
{
    public async Task<ClienteResult> ExecuteAsync(CriarClienteCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        if (!string.IsNullOrWhiteSpace(cmd.Email))
            EmailValidator.EnsureValid(cmd.Email, "Email do cliente");

        // Normalização: CPF/CNPJ só dígitos quando válido; senão preserva input
        // (tolera estrangeiros, dados legados).
        var docNormalizado = Cnpj.TryFrom(cmd.Documento)?.Value ?? cmd.Documento;
        var telNormalizado = Telefone.TryFrom(cmd.Telefone)?.Value ?? cmd.Telefone;

        var cliente = ClienteEntity.Criar(cmd.EmpresaId, cmd.Nome);
        cliente.AtualizarCadastro(
            cmd.Nome, cmd.Apt, cmd.Endereco,
            telNormalizado, cmd.Email, docNormalizado, cmd.Observacoes);

        await repo.AddAsync(cliente);
        await uow.CommitAsync();

        logger.LogInformation("Cliente {Id} criado para empresa {Empresa}.", cliente.Id, cliente.EmpresaId);
        return Map(cliente);
    }

    internal static ClienteResult Map(ClienteEntity c) => new(
        c.Id, c.EmpresaId, c.Nome, c.Apt, c.Endereco, c.Telefone, c.Email,
        c.Documento, c.Observacoes, c.OrderCount, c.LastOrderAt, c.Ativo,
        c.CriadoEm, c.AlteradoEm);
}
