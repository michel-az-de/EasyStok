using EasyStock.Application.UseCases.ClienteCrm;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Cliente.Dossie;

/// <summary>
/// Dossiê do cliente (S25): uma projeção só para o painel lateral da conversa, a lista de clientes do
/// console e o resumo do agente (<see cref="Services.Atendimento.ResumoDossieParaAgente"/>).
/// Tem notas internas: é do console autenticado, nunca do storefront.
/// </summary>
public sealed record DossieClienteDto(
    DossieClienteDados Cliente,
    IReadOnlyList<ClienteEnderecoResult> Enderecos,
    IReadOnlyList<ClienteTagResult> Tags,
    IReadOnlyList<ClienteNotaResult> Notas,
    IReadOnlyList<PedidoResumoCliente> UltimosPedidos,
    ItemFavoritoDossie? ItemFavorito,
    DateTime? UltimaCompraEm,
    int TotalPedidos,
    bool Bloqueado,
    PreferenciasClienteResult? Preferencias,
    IReadOnlyList<ClienteMesmoDomicilio> Domicilio,
    IReadOnlyList<ConversaRecenteDossie> ConversasRecentes,
    PedidoResumoCliente? PedidoEmAndamento,
    ContatoInformadoDossie? ContatoInformado = null);

/// <summary>
/// Dados primários. Sem cliente vinculado (conversa de lead), <c>Id</c> é nulo e nome e telefone vêm do
/// perfil do canal.
/// </summary>
public sealed record DossieClienteDados(
    Guid? Id, string? Nome, string? Telefone, string? Email, string? Observacoes, string? MotivoBloqueio);

/// <summary>Item que aparece em mais pedidos (desempate: maior quantidade somada).</summary>
public sealed record ItemFavoritoDossie(string Nome, int Pedidos, decimal Quantidade);

public sealed record ConversaRecenteDossie(
    Guid Id, CanalConversa Canal, SituacaoConversa Situacao, DateTime IniciadaEm, DateTime UltimaMensagemEm);

/// <summary>
/// #1430: o que o visitante do chat do site escreveu no formulário antes de conversar. Só no dossiê de lead
/// (sem cliente vinculado) e separado de <see cref="DossieClienteDados"/>: não é cadastro até a loja confirmar.
/// </summary>
public sealed record ContatoInformadoDossie(string? Nome, string Telefone, string? Email, DateTime InformadoEm);
