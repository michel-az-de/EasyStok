namespace EasyStock.Application.UseCases.Storefront.Aprovacao;

/// <summary>Input do <see cref="AprovarPedidoStorefrontUseCase"/>.</summary>
/// <param name="PedidoId">PK do agregado <c>pedido</c>.</param>
/// <param name="EmpresaId">Tenant da Babá (vindo do <c>ICurrentUserAccessor</c>).</param>
/// <param name="UsuarioId">User.Identity do operador Babá — audit trail.</param>
/// <param name="UsuarioNome">Nome amigável (opcional) para resposta + log.</param>
/// <param name="Observacoes">Texto livre interno (max 500). Não vai pro cliente.</param>
public sealed record AprovarPedidoStorefrontInput(
    Guid PedidoId,
    Guid EmpresaId,
    Guid UsuarioId,
    string? UsuarioNome = null,
    string? Observacoes = null);
