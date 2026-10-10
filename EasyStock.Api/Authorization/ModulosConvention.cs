using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace EasyStock.Api.Authorization;

// Matriz única, aplicada além das policies de nível e das permissões finas já existentes.
// Exceções por ação evitam liberar pedidos/conversas só para ler o catálogo compartilhado.
public sealed class ModulosConvention : IApplicationModelConvention
{
    private static readonly Dictionary<string, Modulo[]> Mapa = CriarMapa();
    private static readonly HashSet<string> Transversais =
    [
        "Auth", "Notificacao", "Uploads", "PreferenciaMenu", "PwaPush", "FeatureFlags",
        "Diagnostico", "DiagnosticoInfra", "DiagnosticoLogs", "Consentimentos", "ContatoUsuario",
        "AdminTenants", "AdminNotificacoes", "WebhookGateway", "WebhookPix",
        "Internal.NotificacoesJobs", "Webhooks.WebhookMensageriaMeta", "Webhooks.WebhookWhatsApp",
        "Webhooks.WebhookWhatsAppPlataforma",
        "Storefront.Agendamento", "Storefront.Auth", "Storefront.Avaliacao", "Storefront.CardapioConversa",
        "Storefront.ChatSite", "Storefront.ChatSiteMidia", "Storefront.Checkout", "Storefront.FotosCardapioPublicas",
        "Storefront.Frete", "Storefront.Menu", "Storefront.PedidosCliente",
        // O backup PWA usa o contrato próprio de dispositivo/loja, fora do shell do Console.
        "Mobile.DeviceBackup", "Mobile.DevicePairing", "Mobile.MobileBatches", "Mobile.MobileCalculadora",
        "Mobile.MobileCash", "Mobile.MobileClients", "Mobile.MobileDiagnostics", "Mobile.MobileDiagTrace",
        "Mobile.MobileEstoque", "Mobile.MobileOrders", "Mobile.MobileProducts", "Mobile.MobileQuickReports",
        "Mobile.MobileVendas", "Mobile.MobileVersion", "Mobile.Operation", "Mobile.Sync"
    ];
    private static readonly HashSet<string> Bastidor =
    [
        "Financeiro", "ContasAPagar", "ContasAReceber", "CategoriasFinanceiras", "CentrosCusto",
        "Fornecedor", "Analytics", "Reports", "Inteligencia", "InteligenciaLojas", "EntityAudit"
    ];

    public static string Chave(Type tipo) => tipo.FullName!
        .Replace("EasyStock.Api.Mobile.Controllers.", "Mobile.")
        .Replace("EasyStock.Api.Controllers.", "")[..^"Controller".Length];

    public static bool Classificado(Type tipo) =>
        Mapa.ContainsKey(Chave(tipo)) || Transversais.Contains(Chave(tipo)) || Bastidor.Contains(Chave(tipo));

    public static Modulo[]? ModulosDe(Type tipo, string acao)
    {
        var chave = Chave(tipo);
        if (chave == "AtendimentoComanda" && acao == "Cardapio")
            return [Modulo.Cardapio, Modulo.Producao, Modulo.Atendimento, Modulo.Cozinha];
        if (chave == "Kds" && acao == "GetPedidos") return [Modulo.Cozinha, Modulo.Entregas];
        return Mapa.GetValueOrDefault(chave);
    }

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            var tipo = controller.ControllerType.AsType();
            if (!Classificado(tipo))
                throw new InvalidOperationException($"Controller sem classificação de módulo: {tipo.FullName}");
            foreach (var action in controller.Actions)
            {
                var modulos = ModulosDe(tipo, action.ActionMethod.Name);
                if (modulos is null) continue;
                var requisito = new ModuloRequirement(modulos)
                {
                    AceitaBridge = Chave(tipo) is "Impressao" or "PedidoImpresso"
                };
                action.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser().AddRequirements(requisito).Build()));
            }
        }
    }

    private static Dictionary<string, Modulo[]> CriarMapa()
    {
        var mapa = new Dictionary<string, Modulo[]>();
        void Associar(Modulo modulo, params string[] controllers)
        {
            foreach (var controller in controllers) mapa.Add(controller, [modulo]);
        }
        Associar(Modulo.Cardapio, "Produto", "Categoria", "IaAutoPreenchimento", "AtendimentoItensCardapio",
            "AtendimentoSecoesCardapio", "Storefront.TenantVitrineCardapio");
        Associar(Modulo.Producao, "Producao", "ProdutoComposicao", "Lotes", "ItemEstoque", "Movimentacao",
            "EstoqueDesacertos", "ListasCompras", "EtiquetaTemplates", "AtendimentoProducao");
        Associar(Modulo.Atendimento, "AtendimentoAtendentes", "AtendimentoCaderno", "AtendimentoAssistente",
            "AtendimentoComanda", "AtendimentoConfiguracao", "AtendimentoConsentimentos", "AtendimentoConversas",
            "AtendimentoAutomacoes", "AtendimentoEsteira", "AtendimentoExpediente", "AtendimentoMensagensProgramadas",
            "AtendimentoLembretes", "AtendimentoRespostasProntas", "Clientes", "ClientesCrm", "ClienteInteresses",
            "Ocorrencias", "Pedidos", "PedidosCobranca");
        Associar(Modulo.Cozinha, "Kds");
        Associar(Modulo.Caixa, "Caixa", "Venda");
        Associar(Modulo.Campanhas, "Campanhas", "CampanhasSugestoes");
        Associar(Modulo.Configuracoes, "Usuario", "Configuracoes", "Loja", "NotificacoesConfiguracao",
            "IntegracoesWhatsApp", "Storefront.StorefrontConfiguracao");
        Associar(Modulo.Entregas, "AtendimentoEntregadores", "AtendimentoViagens", "AtendimentoRelatoriosEntrega",
            "Storefront.TenantVitrineEntrega");
        mapa.Add("Storefront.AprovacaoPedido", [Modulo.Atendimento, Modulo.Entregas]);
        mapa.Add("PedidoJanela", [Modulo.Atendimento, Modulo.Entregas]);
        mapa.Add("AtendimentoCardapioDoDia", [Modulo.Cardapio, Modulo.Producao, Modulo.Atendimento]);
        mapa.Add("OperacaoEventos", [Modulo.Atendimento, Modulo.Cozinha, Modulo.Entregas]);
        mapa.Add("Impressao", [Modulo.Atendimento, Modulo.Cozinha, Modulo.Entregas]);
        mapa.Add("PedidoImpresso", [Modulo.Atendimento, Modulo.Cozinha, Modulo.Entregas]);
        return mapa;
    }
}
