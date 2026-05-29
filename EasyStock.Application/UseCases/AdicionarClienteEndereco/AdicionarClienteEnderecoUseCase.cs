namespace EasyStock.Application.UseCases.AdicionarClienteEndereco;

public sealed record AdicionarClienteEnderecoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ClienteId,
    [property: MaxLength(20)]  string? Tipo = null,
    [property: MaxLength(255)] string? Logradouro = null,
    [property: MaxLength(20)]  string? Numero = null,
    [property: MaxLength(120)] string? Complemento = null,
    [property: MaxLength(120)] string? Bairro = null,
    [property: MaxLength(120)] string? Cidade = null,
    [property: MaxLength(2)]   string? Estado = null,
    [property: MaxLength(16)]  string? Cep = null,
    [property: MaxLength(60)]  string? Pais = null,
    [property: MaxLength(255)] string? Referencia = null,
    bool Padrao = false);

public class AdicionarClienteEnderecoUseCase(
    IClienteRepository repo,
    IUnitOfWork uow,
    ILogger<AdicionarClienteEnderecoUseCase> logger)
{
    public async Task<Guid?> ExecuteAsync(AdicionarClienteEnderecoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ClienteId, "ClienteId");

        var cliente = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ClienteId);
        if (cliente == null) return null;

        var agora = DateTime.UtcNow;
        var endereco = new ClienteEndereco
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            Tipo = cmd.Tipo,
            Logradouro = cmd.Logradouro,
            Numero = cmd.Numero,
            Complemento = cmd.Complemento,
            Bairro = cmd.Bairro,
            Cidade = cmd.Cidade,
            Estado = cmd.Estado,
            Cep = cmd.Cep,
            Pais = cmd.Pais,
            Referencia = cmd.Referencia,
            Padrao = cmd.Padrao,
            CriadoEm = agora,
            AlteradoEm = agora
        };

        await repo.AddEnderecoAsync(endereco);
        await uow.CommitAsync();

        logger.LogInformation("Endereco {Id} adicionado ao cliente {ClienteId}.", endereco.Id, cliente.Id);
        return endereco.Id;
    }
}
