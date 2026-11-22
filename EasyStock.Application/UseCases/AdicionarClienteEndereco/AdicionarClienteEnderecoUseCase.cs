using EasyStock.Domain.ValueObjects;

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

        var cliente = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.ClienteId);
        if (cliente == null) return null;

        // #2776: um Padrão só; o novo tira o antigo, como a confirmação na conversa já fazia.
        if (cmd.Padrao)
            foreach (var outro in cliente.Enderecos.Where(e => e.Padrao)) outro.Padrao = false;

        var agora = DateTime.UtcNow;
        // #3186: o mesmo CEP (com ou sem máscara), rua e número é o mesmo endereço; sem repetir na lista. O complemento
        // vem como chegou (pode ser apagado); o resto nulo preserva o salvo. Sem logradouro não há o que comparar.
        var igual = string.IsNullOrWhiteSpace(cmd.Logradouro) ? null : cliente.Enderecos.FirstOrDefault(e =>
            EnderecoBr.NormalizarCep(e.Cep) == EnderecoBr.NormalizarCep(cmd.Cep)
            && Igual(e.Logradouro, cmd.Logradouro) && Igual(e.Numero, cmd.Numero));
        if (igual is not null)
        {
            igual.Complemento = cmd.Complemento;
            igual.Bairro = cmd.Bairro ?? igual.Bairro;
            igual.Cidade = cmd.Cidade ?? igual.Cidade;
            igual.Estado = cmd.Estado ?? igual.Estado;
            igual.Referencia = cmd.Referencia ?? igual.Referencia;
            igual.Padrao = igual.Padrao || cmd.Padrao;
            igual.AlteradoEm = agora;
            cliente.AlteradoEm = agora;
            await repo.UpdateAsync(cliente);
            await uow.CommitAsync();
            return igual.Id;
        }

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
        cliente.AlteradoEm = agora;
        await repo.UpdateAsync(cliente);
        await uow.CommitAsync();

        logger.LogInformation("Endereco {Id} adicionado ao cliente {ClienteId}.", endereco.Id, cliente.Id);
        return endereco.Id;
    }

    private static bool Igual(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
