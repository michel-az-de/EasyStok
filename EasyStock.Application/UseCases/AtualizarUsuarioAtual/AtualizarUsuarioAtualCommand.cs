namespace EasyStock.Application.UseCases.AtualizarUsuarioAtual;

/// <summary>
/// Atualiza o perfil do usuário autenticado. Trocar o <paramref name="Email"/> exige <paramref name="SenhaAtual"/> e
/// só grava o endereço como pendente (N4): o e-mail da conta troca depois do clique no link enviado ao endereço novo.
/// <paramref name="BaseUrl"/> é a origem do front para o link, aceita só se estiver na allowlist.
/// </summary>
public sealed record AtualizarUsuarioAtualCommand(
    string? Nome,
    string? Email,
    string? TemaPreferido = null,
    string? SenhaAtual = null,
    string? BaseUrl = null) : ICommand;

/// <param name="EmailPendente">O endereço que espera confirmação, quando o pedido de troca foi aceito.</param>
public sealed record AtualizarUsuarioAtualResult(
    Guid Id, string Nome, string Email, string TemaPreferido, string? EmailPendente = null);
