using EasyStock.Application.UseCases.AdicionarClienteEndereco;

namespace EasyStock.Application.UseCases.Atendimento.Endereco;

public sealed record ConfirmarEnderecoClienteCommand(Guid EmpresaId, Guid ClienteId, EnderecoNormalizado Endereco);

/// <summary>
/// S14: o cliente confirmou o endereço na conversa. Vira o <see cref="ClienteEndereco"/> padrão (criado pelo
/// <see cref="AdicionarClienteEnderecoUseCase"/>, ou o já salvo com mesmo CEP, logradouro e número) e os
/// campos primários do <see cref="Cliente"/> passam a refleti-lo. Devolve o id do endereço, ou nulo quando o
/// cliente não existe na empresa.
/// </summary>
public sealed class ConfirmarEnderecoClienteUseCase(
    IClienteRepository clienteRepository,
    IUnitOfWork unitOfWork,
    AdicionarClienteEnderecoUseCase adicionarEndereco)
{
    public async Task<Guid?> ExecuteAsync(ConfirmarEnderecoClienteCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(command.ClienteId, "ClienteId");

        var cliente = await clienteRepository.GetByIdWithDetailsAsync(command.EmpresaId, command.ClienteId);
        if (cliente is null) return null;

        var e = command.Endereco;
        var existente = cliente.Enderecos.FirstOrDefault(x =>
            Igual(x.Cep, e.Cep) && Igual(x.Logradouro, e.Logradouro) && Igual(x.Numero, e.Numero));

        foreach (var outro in cliente.Enderecos.Where(x => x != existente))
            outro.Padrao = false;

        cliente.Endereco = string.IsNullOrWhiteSpace(e.Numero) ? e.Logradouro : $"{e.Logradouro}, {e.Numero}";
        cliente.Cep = e.Cep;
        cliente.Bairro = e.Bairro;
        cliente.Cidade = e.Cidade;
        cliente.Complemento = e.Complemento;
        await clienteRepository.UpdateAsync(cliente);

        if (existente is not null)
        {
            existente.Padrao = true;
            existente.Complemento = e.Complemento ?? existente.Complemento;
            existente.Referencia = e.Referencia ?? existente.Referencia;
            existente.AlteradoEm = DateTime.UtcNow;
            await unitOfWork.CommitAsync();
            return existente.Id;
        }

        // Commita junto a atualização do cliente acima (mesmo unit of work).
        return await adicionarEndereco.ExecuteAsync(new AdicionarClienteEnderecoCommand(
            command.EmpresaId, cliente.Id, Tipo: "entrega", Logradouro: e.Logradouro, Numero: e.Numero,
            Complemento: e.Complemento, Bairro: e.Bairro, Cidade: e.Cidade, Estado: e.Uf, Cep: e.Cep,
            Referencia: e.Referencia, Padrao: true));
    }

    private static bool Igual(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
