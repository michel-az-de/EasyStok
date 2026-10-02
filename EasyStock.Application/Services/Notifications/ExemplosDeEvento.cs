using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Payload de exemplo por tipo do catálogo de plataforma (N13): fonte única do disparo de teste e do teste de
/// renderização dos templates. As chaves são o contrato fechado de cada tipo e os valores já vêm formatados em pt-BR.
/// Link de exemplo termina em <c>#teste</c> e nunca leva token real; o destinatário (<c>email</c>, <c>usuarioId</c>) é
/// trocado pelo do superadmin que dispara.
/// </summary>
public static class ExemplosDeEvento
{
    private static readonly IReadOnlyDictionary<TipoEventoNotificacao, IReadOnlyDictionary<string, object?>> Exemplos =
        new Dictionary<TipoEventoNotificacao, IReadOnlyDictionary<string, object?>>
        {
            [TipoEventoNotificacao.ResetSenha] = new Dictionary<string, object?>
            {
                ["nome"] = "Maria Exemplo",
                ["email"] = "maria@exemplo.invalid",
                ["usuarioId"] = "00000000-0000-0000-0000-000000000000",
                ["link_redefinicao"] = "https://app.easystok.com.br/redefinir-senha#teste",
                ["codigo"] = "000000",
                ["expira_em_minutos"] = 30,
            },
            [TipoEventoNotificacao.ConviteAcesso] = new Dictionary<string, object?>
            {
                ["nome"] = "Maria Exemplo",
                ["email"] = "maria@exemplo.invalid",
                ["usuarioId"] = "00000000-0000-0000-0000-000000000000",
                ["empresa"] = "Empresa Exemplo",
                ["link_convite"] = "https://app.easystok.com.br/convite#teste",
                ["expira_em_dias"] = 7,
            },
            [TipoEventoNotificacao.IncidenteSistema] = new Dictionary<string, object?>
            {
                ["componente"] = "Envio de e-mail",
                ["estado_texto"] = "instável",
                ["gravidade"] = "Média",
                ["desde"] = "02/10/2026 14:30",
                ["duracao"] = "12 minutos",
            },
            [TipoEventoNotificacao.PrazoEstourado] = new Dictionary<string, object?>
            {
                ["tipo_legivel"] = "Pedido sem início de preparo",
                ["referencia"] = "AB12CD34",
                ["prazo_texto"] = "02/10/2026 14:00",
                ["atraso_texto"] = "35 minutos",
            },
            [TipoEventoNotificacao.ContatoAlterado] = new Dictionary<string, object?>
            {
                ["nome"] = "Maria Exemplo",
                ["email"] = "maria@exemplo.invalid",
                ["usuarioId"] = "00000000-0000-0000-0000-000000000000",
                ["contato"] = "e-mail",
                ["novo_mascarado"] = "m***@exemplo.invalid",
                ["quando"] = "02/10/2026 14:30",
            },
            [TipoEventoNotificacao.ResumoDiario] = new Dictionary<string, object?>
            {
                ["data"] = "01/10/2026",
                ["entregues"] = 18,
                ["faturamento"] = "R$ 1.234,56",
                ["ticket_medio"] = "R$ 68,59",
                ["pendentes"] = 3,
                ["valor_pendentes"] = "R$ 210,00",
                ["caixa_texto"] = "fechado sem diferença",
                ["pix_texto"] = "4 Pix conferidos",
            },
        };

    /// <summary>Tipos que têm exemplo, isto é, os do catálogo de plataforma.</summary>
    public static IReadOnlyCollection<TipoEventoNotificacao> Tipos { get; } = Exemplos.Keys.ToList();

    public static bool TryObter(TipoEventoNotificacao tipo, out IReadOnlyDictionary<string, object?> exemplo)
    {
        if (Exemplos.TryGetValue(tipo, out var achado))
        {
            exemplo = achado;
            return true;
        }

        exemplo = new Dictionary<string, object?>();
        return false;
    }

    public static IReadOnlyDictionary<string, object?> Obter(TipoEventoNotificacao tipo) =>
        Exemplos.TryGetValue(tipo, out var exemplo)
            ? exemplo
            : throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo fora do catálogo de plataforma.");
}
