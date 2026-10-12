namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Projeção leve do que o validador de token precisa do usuário (#1352): se a conta está ativa e o corte
/// das sessões. Também é o valor guardado no cache por <see cref="UseCases.Common.CacheKeys.Sessao"/>.
/// </summary>
/// <param name="Ativo">Conta ativa (<c>Usuario.Ativo</c>), não o vínculo com uma empresa.</param>
/// <param name="SessoesValidasDesde">Corte das sessões; nulo quer dizer que nunca revogou.</param>
public sealed record SessaoDoUsuario(bool Ativo, DateTime? SessoesValidasDesde);
