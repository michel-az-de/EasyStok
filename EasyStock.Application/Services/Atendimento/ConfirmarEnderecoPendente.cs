using EasyStock.Application.UseCases.Atendimento.Endereco;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

public sealed record ConfirmacaoEnderecoResult(Guid? EnderecoId, EnderecoNormalizado? Endereco, string? Erro);

/// <summary>
/// S14: confirma o endereço que <c>validar_endereco</c> deixou pendente na conversa. Usado pela ferramenta
/// <c>confirmar_endereco</c> e pelo botão <c>acao:confirmar_endereco</c>. Não commita a conversa: quem chama
/// é dono do turno.
/// </summary>
public sealed class ConfirmarEnderecoPendente(ConfirmarEnderecoClienteUseCase confirmarEndereco)
{
    public const string ErroSemCliente = "conversa_sem_cliente";
    public const string ErroSemEnderecoPendente = "sem_endereco_validado";
    public const string ErroClienteNaoEncontrado = "cliente_nao_encontrado";

    public async Task<ConfirmacaoEnderecoResult> ExecutarAsync(Guid empresaId, Conversa conversa, CancellationToken ct = default)
    {
        if (conversa.ClienteId is not { } clienteId)
            return new(null, null, ErroSemCliente);

        var pendente = ContextoConversaJson.Ler<EnderecoNormalizado>(conversa, ContextoConversaJson.EnderecoPendente);
        if (pendente is null)
            return new(null, null, ErroSemEnderecoPendente);

        var id = await confirmarEndereco.ExecuteAsync(new ConfirmarEnderecoClienteCommand(empresaId, clienteId, pendente), ct);
        if (id is null)
            return new(null, null, ErroClienteNaoEncontrado);

        ContextoConversaJson.Gravar<EnderecoNormalizado>(conversa, ContextoConversaJson.EnderecoPendente, null);
        return new(id, pendente, null);
    }
}
