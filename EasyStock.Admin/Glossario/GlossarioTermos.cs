// Fonte única de termos de negócio do EasyStock (catálogo do glossário).
// Consumida pela TagHelper <es-help> e pela Central de Ajuda (/Ajuda).
// Cada Ancora == Slug, usada como #ancora no link da página /Ajuda.
// Definições derivadas do domínio (entidades/enums); manter alinhado ao código.

namespace EasyStock.Admin.Glossario;

/// <summary>
/// Verbete imutável do glossário de negócio.
/// </summary>
/// <param name="Slug">Chave estável e única (kebab-case), usada no dicionário e como âncora.</param>
/// <param name="Termo">Rótulo exibível do termo.</param>
/// <param name="Curto">Definição curta (1 linha) para tooltip/popover.</param>
/// <param name="Longo">Definição longa para a Central de Ajuda.</param>
/// <param name="Ancora">Âncora (#ancora) na página /Ajuda; por convenção igual ao Slug.</param>
public sealed record TermoGlossario(string Slug, string Termo, string Curto, string Longo, string Ancora);

/// <summary>
/// Catálogo estático e imutável dos termos de negócio do EasyStock.
/// Chave do dicionário = Slug do verbete.
/// </summary>
public static class GlossarioTermos
{
    /// <summary>Verbetes do glossário, indexados por slug.</summary>
    public static IReadOnlyDictionary<string, TermoGlossario> Termos { get; } =
        new Dictionary<string, TermoGlossario>
        {
            ["cliente-tenant-empresa"] = new TermoGlossario(
                Slug: "cliente-tenant-empresa",
                Termo: "Cliente / Empresa / Tenant",
                Curto: "Três rótulos para a mesma entidade: a empresa que assina a plataforma.",
                Longo: "São rótulos da mesma entidade de negócio, identificada por EmpresaId. " +
                       "Na UI pública aparece como \"Cliente\"; internamente as entidades usam \"Empresa\" " +
                       "(entidade raiz Empresa); no backend admin aparece como \"Tenant\" (rotas api/admin/tenants). " +
                       "O isolamento multilocatário usa EmpresaId como chave nos filtros globais de consulta.",
                Ancora: "cliente-tenant-empresa"),

            ["assinatura-plano-status"] = new TermoGlossario(
                Slug: "assinatura-plano-status",
                Termo: "Assinatura",
                Curto: "Contrato recorrente que liga uma Empresa a um Plano, com cobranças mensais.",
                Longo: "A assinatura (AssinaturaEmpresa) liga uma Empresa a um Plano, com data de início, " +
                       "data de fim e status. O status pode ser Ativa, Suspensa (inadimplência/dunning), " +
                       "Cancelada (encerramento definitivo) ou Expirada (trial vencido sem plano pago). " +
                       "Trial é período à parte, não um status. MRR e ARR são calculados sobre assinaturas ativas.",
                Ancora: "assinatura-plano-status"),

            ["mrr-arr-churn"] = new TermoGlossario(
                Slug: "mrr-arr-churn",
                Termo: "MRR / ARR / Churn",
                Curto: "Métricas de receita recorrente (mensal e anual) e taxa de cancelamento.",
                Longo: "MRR é a soma dos planos mensais das assinaturas ativas no mês. ARR é o MRR multiplicado " +
                       "por 12. Churn Rate é a razão entre assinaturas canceladas e ativas no início do período. " +
                       "O relatório agrega por competência (yyyy-MM): assinaturas ativas, novas, canceladas, " +
                       "suspensas, MRR, ARR, churn, receita realizada (faturas pagas) e ticket médio.",
                Ancora: "mrr-arr-churn"),

            ["plano"] = new TermoGlossario(
                Slug: "plano",
                Termo: "Plano",
                Curto: "Produto da plataforma, com preço mensal e limites de uso.",
                Longo: "O Plano define preço mensal e limites de lojas, usuários, produtos e gerações de IA " +
                       "mensais. O valor -1 (SemLimite) indica recurso ilimitado. Usado nas assinaturas e na " +
                       "configuração de SLA por plano.",
                Ancora: "plano"),

            ["cupom"] = new TermoGlossario(
                Slug: "cupom",
                Termo: "Cupom",
                Curto: "Código promocional de desconto ou crédito aplicado a assinaturas.",
                Longo: "O cupom tem código (em maiúsculas), tipo de desconto (percentual, valor fixo ou meses " +
                       "grátis), valor, limite de usos, validade e, opcionalmente, um plano ao qual fica restrito. " +
                       "Ao aplicar, a assinatura guarda um snapshot do código e do desconto aplicado.",
                Ancora: "cupom"),

            ["fatura"] = new TermoGlossario(
                Slug: "fatura",
                Termo: "Fatura",
                Curto: "Documento de cobrança central, agnóstico à origem (assinatura, pedido ou avulsa).",
                Longo: "A fatura é o agregado central de cobrança e a fonte de verdade do valor devido. " +
                       "Tem número (por empresa e ano), origem (Assinatura, Pedido ou Avulsa), referência da origem " +
                       "e status (Rascunho, Emitida, Paga, Parcialmente paga, Vencida ou Cancelada). " +
                       "Os dados do faturado e do emissor são gravados como snapshot. Pagamentos são registrados " +
                       "e atualizam o status. A fatura avulsa é emitida manualmente pelo admin, sem referência de origem.",
                Ancora: "fatura"),

            ["cobranca-assinatura"] = new TermoGlossario(
                Slug: "cobranca-assinatura",
                Termo: "Cobrança (legado)",
                Curto: "Snapshot de cobrança do gateway (Pix/Boleto); convive com a Fatura até ser descontinuada.",
                Longo: "A cobrança de assinatura é a versão anterior do fluxo (Pix/Boleto manual) e convive com a " +
                       "Fatura durante a transição. Quando gerada, cria uma Fatura com origem Assinatura apontando " +
                       "para a assinatura, e guarda de volta o vínculo com essa fatura. A Fatura é a fonte de verdade " +
                       "do valor; a Cobrança é o snapshot do gateway.",
                Ancora: "cobranca-assinatura"),

            ["sla-nivel-atendimento"] = new TermoGlossario(
                Slug: "sla-nivel-atendimento",
                Termo: "SLA / Nível de atendimento",
                Curto: "Prazos de resposta e resolução por prioridade, com escalação entre níveis N1 a N4.",
                Longo: "O SLA define minutos de resposta e de resolução por prioridade, podendo valer globalmente, " +
                       "por plano ou por empresa, e considerar apenas horário comercial. Os níveis N1, N2, N3 e N4 " +
                       "são degraus de escalação do suporte (não são prioridades). O ticket registra prazos, primeira " +
                       "resposta, resolução e se os SLAs foram violados, com alertas em 50% e 80% do prazo.",
                Ancora: "sla-nivel-atendimento"),

            ["ticket-helpdesk"] = new TermoGlossario(
                Slug: "ticket-helpdesk",
                Termo: "Chamado",
                Curto: "Chamado de suporte (helpdesk), criado manualmente ou por automação.",
                Longo: "O chamado é o pedido de suporte (helpdesk; a entidade interna chama-se Ticket). Status: Aberto, " +
                       "Em atendimento, Aguardando cliente, Resolvido ou Fechado. Prioridade: Baixa, Normal, Alta ou " +
                       "Crítica. Categoria: Bug, Dúvida, Financeiro, Solicitação, Incidente, BugFixDev ou Outro. Pode " +
                       "estar relacionado a uma fatura ou pedido, escalar entre níveis N1 a N4 e coletar CSAT (nota de 1 a 5).",
                Ancora: "ticket-helpdesk"),

            ["trial"] = new TermoGlossario(
                Slug: "trial",
                Termo: "Teste grátis (trial)",
                Curto: "Período de acesso gratuito antes do pagamento da assinatura.",
                Longo: "O teste grátis (trial) é o período de uso gratuito antes de um plano pago. A assinatura guarda " +
                       "a data de fim do trial e considera-o ativo enquanto essa data for futura. Quando o trial vence " +
                       "sem plano pago, a assinatura passa de Ativa para Expirada e o acesso é bloqueado. É distinto de " +
                       "Suspensão, que é o dunning de um plano pago.",
                Ancora: "trial"),

            ["storefront"] = new TermoGlossario(
                Slug: "storefront",
                Termo: "Loja virtual (storefront)",
                Curto: "Loja de e-commerce pública da Empresa, com cardápio, pedidos online e entrega.",
                Longo: "A loja virtual (storefront) é a vitrine pública, uma por Empresa, com slug único global (usado " +
                       "para resolver a loja na URL). Inclui título público, domínio customizado, cor primária, WhatsApp " +
                       "de pedidos, pedido mínimo e regras de frete (inclusive por raio). Por segurança, nasce inativa, " +
                       "sem NFC-e automática e com modelo fiscal manual. Para ativar exige pelo menos um item de cardápio " +
                       "visível, credencial de pagamento e uma zona de frete.",
                Ancora: "storefront"),

            ["cardapio-item"] = new TermoGlossario(
                Slug: "cardapio-item",
                Termo: "Cardápio / Item",
                Curto: "Item vendável da loja virtual, no modo avulso (sem ERP) ou vinculado a um Produto.",
                Longo: "O item de cardápio é exibido na loja virtual em dois modos coexistentes: avulso (sem produto " +
                       "do ERP, com nome público obrigatório) e vinculado (herda nome e preço de referência de um Produto). " +
                       "Por segurança nasce não-visível: o tenant publica manualmente. \"Disponível\" indica estoque. " +
                       "A ordem de exibição é numérica (permite inserir entre dois itens). Pode ser item guarda-chuva com " +
                       "variações selecionáveis.",
                Ancora: "cardapio-item"),

            ["dispositivo-mobile"] = new TermoGlossario(
                Slug: "dispositivo-mobile",
                Termo: "Dispositivo (mobile PWA)",
                Curto: "Aparelho pareado à empresa/loja, autenticado por API Key.",
                Longo: "O dispositivo é o aplicativo móvel pareado a uma empresa/loja. O fluxo gera um código de " +
                       "pareamento de 6 dígitos com validade; o app envia o código e o identificador do aparelho, o " +
                       "servidor valida e gera uma API Key (guardada como hash SHA-256). O app envia essa chave em cada " +
                       "requisição. A revogação marca o dispositivo como revogado, preservando o histórico de auditoria.",
                Ancora: "dispositivo-mobile"),

            ["nfce-csc"] = new TermoGlossario(
                Slug: "nfce-csc",
                Termo: "NFC-e / CSC",
                Curto: "Nota fiscal eletrônica de consumidor; CSC é o código de segurança do contribuinte (SEFAZ).",
                Longo: "A NFC-e é o documento fiscal eletrônico do varejo, emitido pela loja virtual ou pelo ERP. " +
                       "A configuração fiscal por empresa reúne regime tributário, inscrições, endereço, ambiente " +
                       "(Sandbox ou Produção), provedor, série e próximo número da NFC-e, certificado digital e o CSC. " +
                       "O CSC (Código de Segurança do Contribuinte) é fornecido pela SEFAZ e usado no QR Code (não é " +
                       "gerado internamente). Por segurança a emissão nasce desabilitada até a configuração estar completa.",
                Ancora: "nfce-csc"),

            ["cliente-erp"] = new TermoGlossario(
                Slug: "cliente-erp",
                Termo: "Comprador (cliente do ERP)",
                Curto: "Pessoa ou empresa compradora no ERP — diferente de Empresa (tenant).",
                Longo: "O comprador (cliente do ERP) é quem compra: pessoa ou empresa, distinto da Empresa (tenant). " +
                       "Guarda nome, contato, documento e dados de entrega, além de campos da loja virtual (endereço, " +
                       "hash do telefone para login por OTP, consentimento de marketing conforme a LGPD) e métricas de " +
                       "pedidos. Mantém coleções de endereços, telefones, documentos e auditoria de alterações.",
                Ancora: "cliente-erp"),

            ["configuracao-fiscal"] = new TermoGlossario(
                Slug: "configuracao-fiscal",
                Termo: "Configuração fiscal",
                Curto: "Dados obrigatórios para emitir notas: regime, inscrições, endereço, certificado e SEFAZ.",
                Longo: "A configuração fiscal reúne, por empresa, os dados necessários para emitir notas: regime " +
                       "tributário, inscrições, endereço (snapshot), ambiente (Sandbox para desenvolvimento, Produção " +
                       "para homologado) e certificado. É mantida separada do agregado Empresa para não pesar nas " +
                       "consultas quentes (estoque, pedidos). A flag \"Habilitada\" controla se a emissão real está liberada.",
                Ancora: "configuracao-fiscal"),

            ["impersonacao"] = new TermoGlossario(
                Slug: "impersonacao",
                Termo: "Acessar como (impersonação)",
                Curto: "Admin acessa a conta de uma Empresa para diagnóstico e suporte, com auditoria.",
                Longo: "O \"acessar como\" (impersonação) permite que o admin acesse a conta de uma Empresa específica " +
                       "para diagnóstico e suporte. Cada sessão é auditada com o admin que iniciou, a empresa, o IP e os " +
                       "horários de início e fim. O fluxo solicita um token de impersonação e faz o handoff para a aplicação web.",
                Ancora: "impersonacao"),

            ["onboarding"] = new TermoGlossario(
                Slug: "onboarding",
                Termo: "Onboarding",
                Curto: "Marcação de que a empresa concluiu a configuração inicial obrigatória.",
                Longo: "O onboarding indica que a empresa concluiu a configuração inicial obrigatória. A Empresa guarda " +
                       "se o onboarding está completo e quando foi concluído. Não confundir com seed/preparação de dados " +
                       "de teste em sandbox (ver \"Popular ambiente\", termo informal).",
                Ancora: "onboarding")

            // TODO: "popular-ambiente" — termo NÃO confirmado no código (sem entidade/enum).
            // Provável jargão verbal do time para seed/preparar dados de teste em sandbox.
            // Adicionar verbete quando houver definição canônica.
        };

    /// <summary>
    /// Tenta obter um verbete pelo slug.
    /// </summary>
    /// <param name="slug">Slug (chave) do termo.</param>
    /// <param name="termo">Verbete encontrado, ou null se não existir.</param>
    /// <returns>true se o slug existe no catálogo; caso contrário, false.</returns>
    public static bool TryGet(string slug, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TermoGlossario? termo) =>
        Termos.TryGetValue(slug, out termo);
}
