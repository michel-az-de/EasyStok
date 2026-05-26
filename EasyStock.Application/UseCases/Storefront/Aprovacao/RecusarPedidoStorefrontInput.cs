namespace EasyStock.Application.UseCases.Storefront.Aprovacao;

/// <summary>Input do <see cref="RecusarPedidoStorefrontUseCase"/>.</summary>
/// <param name="PedidoId">PK do agregado.</param>
/// <param name="EmpresaId">Tenant da Babá.</param>
/// <param name="UsuarioId">Operador — audit trail.</param>
/// <param name="Motivo">Categoria canônica (parse no controller).</param>
/// <param name="MensagemCliente">Texto livre (max 280) que vai no WhatsApp ao cliente.</param>
/// <param name="UsuarioNome">Nome amigável para resposta + log.</param>
public sealed record RecusarPedidoStorefrontInput(
    Guid PedidoId,
    Guid EmpresaId,
    Guid UsuarioId,
    MotivoRecusa Motivo,
    string? MensagemCliente = null,
    string? UsuarioNome = null);
