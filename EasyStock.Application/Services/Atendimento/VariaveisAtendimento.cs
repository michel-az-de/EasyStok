using System.Globalization;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Valores das variáveis de <see cref="ModeloTextoAtendimento"/> para uma conversa (S42). O que a conversa
/// não tem fica nulo, e o render recusa: nunca sai <c>{pedido}</c> literal para o cliente.
/// <list type="bullet">
/// <item><c>{nome}</c>: primeiro nome do cliente vinculado, senão o nome do perfil do contato.</item>
/// <item><c>{pedido}</c>: os 8 primeiros caracteres do id do pedido (o mesmo código curto do resto do sistema).</item>
/// <item><c>{faixa}</c>: horário agendado do pedido, no fuso de São Paulo.</item>
/// </list>
/// </summary>
public sealed class VariaveisAtendimento(IClienteRepository clientes, IPedidoRepository pedidos)
{
    /// <summary>Brasil sem horário de verão desde 2019: offset fixo, sem depender do banco de fusos do SO.</summary>
    private static readonly TimeSpan FusoSaoPaulo = TimeSpan.FromHours(-3);

    public async Task<IReadOnlyDictionary<string, string?>> ResolverAsync(
        Guid empresaId, Conversa conversa, Guid? clienteId = null, Guid? pedidoId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conversa);

        var idCliente = clienteId ?? conversa.ClienteId;
        var cliente = idCliente is { } c ? await clientes.GetByIdAsync(empresaId, c) : null;
        var nome = PrimeiroNome(cliente?.Nome) ?? PrimeiroNome(conversa.ContatoNome);

        var idPedido = pedidoId ?? conversa.PedidoEmAndamentoId;
        string? faixa = null;
        if (idPedido is { } p)
        {
            var pedido = await pedidos.GetByIdAsync(empresaId, p);
            if (pedido?.AgendadoParaEm is { } agendado)
                faixa = DateTime.SpecifyKind(agendado, DateTimeKind.Utc).Add(FusoSaoPaulo)
                    .ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture);
        }

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["nome"] = nome,
            ["pedido"] = idPedido is { } id ? CodigoCurto(id) : null,
            ["faixa"] = faixa,
        };
    }

    public static string CodigoCurto(Guid pedidoId) => pedidoId.ToString("N")[..8].ToUpperInvariant();

    private static string? PrimeiroNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome) || nome == IdentificarClientePorTelefoneUseCase.NomePadraoLead) return null;
        return nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }
}
