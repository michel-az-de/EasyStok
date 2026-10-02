using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Sobrescritas dos prazos de validade (seção <c>Notifications:Quarentena</c>): em <c>Prazos</c>, o nome do
/// <see cref="TipoEventoNotificacao"/> e o prazo em minutos (ex.: <c>Notifications:Quarentena:Prazos:ResetSenha=15</c>).
/// </summary>
public sealed class QuarentenaNotificacaoOptions
{
    public const string Section = "Notifications:Quarentena";

    /// <summary>Minutos por tipo de evento. Valor menor ou igual a zero e tipo desconhecido são ignorados.</summary>
    public Dictionary<string, int> Prazos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Prazo de validade de cada tipo de evento (N1, quarentena): backlog que passou do prazo não se envia, vira
/// <c>Expirado</c>. No evento o prazo conta de <c>OcorridoEm</c>; no outbox (só <c>Pendente</c> sem tentativa), de
/// <c>ProximaTentativaEm</c>. Os valores iniciais vêm da spec N1 e podem ser sobrescritos por configuração:
/// <list type="table">
/// <item><term>Segurança (<c>ResetSenha</c>, <c>ConfirmacaoEmail</c>)</term><description>30 min</description></item>
/// <item><term>Aviso do pedido e pós-venda ao cliente (S13)</term><description>2 h</description></item>
/// <item><term>Campanha, atendimento interno</term><description>1 h</description></item>
/// <item><term>Demais</term><description>24 h</description></item>
/// </list>
/// </summary>
public sealed class PoliticaValidadeNotificacao
{
    /// <summary>Prazo dos tipos sem regra própria.</summary>
    public static readonly TimeSpan PrazoPadrao = TimeSpan.FromHours(24);

    private static readonly IReadOnlyDictionary<TipoEventoNotificacao, TimeSpan> Iniciais =
        new Dictionary<TipoEventoNotificacao, TimeSpan>
        {
            [TipoEventoNotificacao.ResetSenha] = TimeSpan.FromMinutes(30),
            [TipoEventoNotificacao.ConfirmacaoEmail] = TimeSpan.FromMinutes(30),

            [TipoEventoNotificacao.PedidoPagoConfirmado] = TimeSpan.FromHours(2),
            [TipoEventoNotificacao.PedidoEmPreparo] = TimeSpan.FromHours(2),
            [TipoEventoNotificacao.PedidoSaiuParaEntrega] = TimeSpan.FromHours(2),
            [TipoEventoNotificacao.PedidoEntregue] = TimeSpan.FromHours(2),
            [TipoEventoNotificacao.AvaliacaoSolicitada] = TimeSpan.FromHours(2),
            [TipoEventoNotificacao.ReembolsoEfetuado] = TimeSpan.FromHours(2),

            [TipoEventoNotificacao.CampanhaMarketing] = TimeSpan.FromHours(1),
            [TipoEventoNotificacao.CampanhaLembreteEncerramento] = TimeSpan.FromHours(1),

            [TipoEventoNotificacao.ConversaEscalada] = TimeSpan.FromHours(1),
            [TipoEventoNotificacao.LembreteVencido] = TimeSpan.FromHours(1),
        };

    private readonly Dictionary<TipoEventoNotificacao, TimeSpan> _prazos;

    /// <summary>Política com os prazos iniciais, sem sobrescrita.</summary>
    public PoliticaValidadeNotificacao() : this(Options.Create(new QuarentenaNotificacaoOptions()))
    {
    }

    public PoliticaValidadeNotificacao(IOptions<QuarentenaNotificacaoOptions> options)
    {
        _prazos = Enum.GetValues<TipoEventoNotificacao>()
            .ToDictionary(tipo => tipo, tipo => Iniciais.GetValueOrDefault(tipo, PrazoPadrao));

        foreach (var (nome, minutos) in options.Value.Prazos)
        {
            if (minutos > 0 && Enum.TryParse<TipoEventoNotificacao>(nome, ignoreCase: true, out var tipo) && Enum.IsDefined(tipo))
                _prazos[tipo] = TimeSpan.FromMinutes(minutos);
        }
    }

    public TimeSpan PrazoDe(TipoEventoNotificacao tipo) => _prazos.GetValueOrDefault(tipo, PrazoPadrao);

    /// <summary>
    /// Os tipos agrupados por prazo, do mais curto ao mais longo: o repositório faz uma consulta por grupo, com o
    /// prazo no <c>WHERE</c>, em vez de trazer o backlog inteiro para decidir tipo a tipo.
    /// </summary>
    public IReadOnlyList<(TimeSpan Prazo, IReadOnlyList<TipoEventoNotificacao> Tipos)> PorPrazo() =>
        _prazos
            .GroupBy(par => par.Value, par => par.Key)
            .OrderBy(grupo => grupo.Key)
            .Select(grupo => (grupo.Key, (IReadOnlyList<TipoEventoNotificacao>)grupo.OrderBy(t => t).ToList()))
            .ToList();

    /// <summary>
    /// O payload do evento carrega um segredo (token ou código)? Ao expirar, o payload sai junto: o evento nunca chegou a
    /// uma mensagem que o apagasse (N2).
    /// </summary>
    public static bool CarregaSegredo(TipoEventoNotificacao tipo) =>
        tipo is TipoEventoNotificacao.ResetSenha or TipoEventoNotificacao.ConfirmacaoEmail;
}
