namespace EasyStock.Application.UseCases.ClienteCrm;

public sealed record ClienteTagResult(string Tag, string Origem, DateTime CriadoEm)
{
    public static ClienteTagResult De(ClienteTag tag) => new(tag.Tag, tag.Origem, tag.CriadoEm);
}

public sealed record TagsClienteResult(IReadOnlyList<ClienteTagResult> Tags, IReadOnlyList<string> Sugeridas);

/// <summary>Projeção da nota interna. Só o console a recebe (nunca storefront nem ferramenta do agente).</summary>
public sealed record ClienteNotaResult(Guid Id, string Texto, string Autor, Guid? PedidoId, Guid? MensagemId, DateTime CriadoEm)
{
    public static ClienteNotaResult De(ClienteNota nota) =>
        new(nota.Id, nota.Texto, nota.Autor, nota.PedidoId, nota.MensagemId, nota.CriadoEm);
}

public sealed record BloqueioClienteResult(bool Bloqueado, DateTime? BloqueadoEm, string? MotivoBloqueio)
{
    public static BloqueioClienteResult De(EasyStock.Domain.Entities.Cliente cliente) =>
        new(cliente.Bloqueado, cliente.BloqueadoEm, cliente.MotivoBloqueio);
}

public sealed record PreferenciasClienteResult(bool AvisosStatusAtivos, bool ConsentiuMarketing, DateTime? ConsentimentoEm)
{
    public static PreferenciasClienteResult De(EasyStock.Domain.Entities.Cliente cliente) =>
        new(cliente.AvisosStatusAtivos, cliente.ConsentiuMarketing, cliente.ConsentimentoEm);
}

public sealed class ClienteCrmNaoEncontradoException(Guid clienteId)
    : Exception($"Cliente {clienteId} não encontrado nesta empresa.");

/// <summary>Vira 409 na API; no domínio a repetição é no-op (<see cref="Cliente.AdicionarTag"/>).</summary>
public sealed class ClienteTagDuplicadaException(string tag)
    : Exception($"O cliente já tem a tag '{tag}'.");

/// <summary>Vira 400 na API: nota não pode apontar para pedido de outro cliente (nem inexistente).</summary>
public sealed class NotaPedidoDeOutroClienteException(Guid pedidoId)
    : Exception($"Pedido {pedidoId} não é deste cliente.");
