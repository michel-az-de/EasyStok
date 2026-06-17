using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data;

/// <summary>
/// Semeia registros globais (EmpresaId = null) de templates e configurações de canal
/// que o sistema de notificações precisa para funcionar fora da caixa. Idempotente.
/// </summary>
public static class NotificacoesGlobaisSeed
{
    public static async Task ExecutarAsync(EasyStockDbContext context, ILogger logger)
    {
        var seeded = false;
        seeded |= await SeedConfiguracoesCanal(context, logger);
        seeded |= await SeedTemplates(context, logger);
        seeded |= await SeedRotinas(context, logger);

        if (seeded)
            await context.SaveChangesAsync();
    }

    private static async Task<bool> SeedConfiguracoesCanal(EasyStockDbContext context, ILogger logger)
    {
        // HasQueryFilter global (EmpresaId == CurrentTenantId || IsSuperAdmin) zera essa
        // leitura durante o seed (CurrentTenantId=Guid.Empty e null != Guid.Empty), entao
        // o seed enxergava lista vazia e duplicava as configs a cada startup. Idempotencia
        // exige IgnoreQueryFilters para ler o que ja existe globalmente.
        var existentes = await context.NotifConfiguracoesCanal
            .IgnoreQueryFilters()
            .Where(c => c.EmpresaId == null)
            .Select(c => c.Canal)
            .ToListAsync();

        var canais = new[] { CanalNotificacao.Email, CanalNotificacao.Sms, CanalNotificacao.WhatsApp, CanalNotificacao.InApp };
        var adicionados = false;

        foreach (var canal in canais)
        {
            if (existentes.Contains(canal)) continue;
            context.NotifConfiguracoesCanal.Add(ConfiguracaoCanal.Criar(canal, "stub"));
            logger.LogInformation("NotificacoesGlobaisSeed: ConfiguracaoCanal global stub adicionada para {Canal}", canal);
            adicionados = true;
        }

        return adicionados;
    }

    private static async Task<bool> SeedTemplates(EasyStockDbContext context, ILogger logger)
    {
        // Ver comentario em SeedConfiguracoesCanal — HasQueryFilter global zera a leitura
        // de globais (EmpresaId IS NULL) durante seed; IgnoreQueryFilters restaura.
        var existentes = await context.NotifTemplates
            .IgnoreQueryFilters()
            .Where(t => t.EmpresaId == null)
            .Select(t => t.Codigo)
            .ToListAsync();

        var templates = BuildDefaultTemplates();
        var adicionados = false;

        foreach (var t in templates)
        {
            if (existentes.Contains(t.Codigo)) continue;
            t.Aprovar("system");
            t.Ativar();
            context.NotifTemplates.Add(t);
            logger.LogInformation("NotificacoesGlobaisSeed: Template global adicionado: {Codigo}", t.Codigo);
            adicionados = true;
        }

        return adicionados;
    }

    private static async Task<bool> SeedRotinas(EasyStockDbContext context, ILogger logger)
    {
        // Ver comentario em SeedConfiguracoesCanal — HasQueryFilter global zera a leitura
        // de globais (EmpresaId IS NULL) durante seed; IgnoreQueryFilters restaura.
        var existentes = await context.NotifRotinas
            .IgnoreQueryFilters()
            .Where(r => r.EmpresaId == null)
            .Select(r => r.Codigo)
            .ToListAsync();

        var rotinas = BuildDefaultRotinas();
        var adicionados = false;

        foreach (var r in rotinas)
        {
            if (existentes.Contains(r.Codigo)) continue;
            r.Ativar("system");
            context.NotifRotinas.Add(r);
            logger.LogInformation("NotificacoesGlobaisSeed: Rotina global adicionada: {Codigo}", r.Codigo);
            adicionados = true;
        }

        return adicionados;
    }

    private static IEnumerable<TemplateNotificacao> BuildDefaultTemplates()
    {
        yield return TemplateNotificacao.Criar(
            codigo: "assinatura_expirando_email_v1",
            nome: "Renovação de Assinatura — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.AssinaturaExpirando,
            assuntoTemplate: "Renovação da sua assinatura EasyStock",
            corpoTemplate: EmailTemplateLoader.LoadBody("assinatura_expirando_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "assinatura_expirada_dunning_email_v1",
            nome: "Dunning — Pagamento Pendente — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.AssinaturaExpirada,
            assuntoTemplate: "EasyStock — Pagamento pendente (aviso {{ numero_lembrete }}/3)",
            corpoTemplate: EmailTemplateLoader.LoadBody("assinatura_expirada_dunning_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "alerta_estoque_critico_email_v1",
            nome: "Alerta de Estoque Crítico — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.AlertaEstoqueCritico,
            assuntoTemplate: "EasyStock — Alerta de estoque crítico: {{ produto_nome }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("alerta_estoque_critico_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "produto_vencendo_email_v1",
            nome: "Produto Vencendo — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ProdutoVencendo,
            assuntoTemplate: "EasyStock — Produto vencendo em {{ dias_restantes }} dia(s): {{ produto_nome }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("produto_vencendo_email_v1"));

        // ===== Templates do modulo Helpdesk =====
        yield return TemplateNotificacao.Criar(
            codigo: "ticket_criado_inapp_v1",
            nome: "Ticket Criado — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.TicketCriado,
            assuntoTemplate: "Novo ticket: {{ titulo }}",
            corpoTemplate: "Empresa {{ empresaNome }} abriu o ticket \"{{ titulo }}\" (prioridade {{ prioridade }}, nivel {{ nivel }}).");

        yield return TemplateNotificacao.Criar(
            codigo: "ticket_respondido_admin_inapp_v1",
            nome: "Sua solicitacao foi respondida — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.TicketRespondidoAdmin,
            assuntoTemplate: "Resposta no ticket: {{ titulo }}",
            corpoTemplate: "Recebemos uma resposta no seu ticket \"{{ titulo }}\". Acesse o painel para visualizar.");

        yield return TemplateNotificacao.Criar(
            codigo: "ticket_respondido_admin_email_v1",
            nome: "Sua solicitacao foi respondida — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.TicketRespondidoAdmin,
            assuntoTemplate: "EasyStock — Resposta no ticket: {{ titulo }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("ticket_respondido_admin_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "ticket_status_alterado_inapp_v1",
            nome: "Status do ticket alterado — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.TicketStatusAlterado,
            assuntoTemplate: "Status alterado: {{ titulo }}",
            corpoTemplate: "Status do ticket mudou de {{ statusAntes }} para {{ statusDepois }}.");

        yield return TemplateNotificacao.Criar(
            codigo: "ticket_atribuido_inapp_v1",
            nome: "Ticket atribuido a voce — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.TicketAtribuido,
            assuntoTemplate: "Ticket atribuido: {{ titulo }}",
            corpoTemplate: "Voce foi designado para atender o ticket \"{{ titulo }}\".");

        yield return TemplateNotificacao.Criar(
            codigo: "ticket_encaminhado_inapp_v1",
            nome: "Ticket encaminhado para o seu nivel — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.TicketEncaminhadoNivel,
            assuntoTemplate: "Ticket encaminhado: {{ titulo }}",
            corpoTemplate: "Ticket \"{{ titulo }}\" foi encaminhado de {{ nivelOrigem }} para {{ nivelDestino }}. Motivo: {{ motivo }}.");

        yield return TemplateNotificacao.Criar(
            codigo: "sla_proximo_vencer_inapp_v1",
            nome: "SLA proximo de vencer — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.SlaProximoVencer,
            assuntoTemplate: "SLA proximo: {{ titulo }}",
            corpoTemplate: "O ticket \"{{ titulo }}\" esta a {{ percentual }}% do prazo. Tempo restante: {{ minutosRestantes }} min.");

        yield return TemplateNotificacao.Criar(
            codigo: "sla_violado_inapp_v1",
            nome: "SLA violado — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.SlaViolado,
            assuntoTemplate: "SLA VIOLADO: {{ titulo }}",
            corpoTemplate: "O ticket \"{{ titulo }}\" estourou o prazo de {{ tipoSla }}. Acao imediata necessaria.");

        yield return TemplateNotificacao.Criar(
            codigo: "sla_violado_email_v1",
            nome: "SLA violado — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.SlaViolado,
            assuntoTemplate: "EasyStock — SLA violado no ticket {{ titulo }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("sla_violado_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "bug_fix_criado_inapp_v1",
            nome: "Bug-fix encaminhado — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.BugFixCriado,
            assuntoTemplate: "Bug encaminhado: {{ titulo }}",
            corpoTemplate: "Novo bug-fix \"{{ titulo }}\" (severidade {{ severidade }}, componente {{ componente }}) foi encaminhado para o time de desenvolvimento.");

        yield return TemplateNotificacao.Criar(
            codigo: "bug_fix_criado_email_v1",
            nome: "Bug-fix encaminhado — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.BugFixCriado,
            assuntoTemplate: "EasyStock — Bug encaminhado: {{ titulo }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("bug_fix_criado_email_v1"));

        // ===== Auth =====
        yield return TemplateNotificacao.Criar(
            codigo: "reset_senha_email_v1",
            nome: "Reset de Senha — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ResetSenha,
            assuntoTemplate: "EasyStok — Redefinicao de senha solicitada",
            corpoTemplate: EmailTemplateLoader.LoadBody("reset_senha_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "confirmacao_email_email_v1",
            nome: "Confirmacao de Email — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ConfirmacaoEmail,
            assuntoTemplate: "EasyStok — Confirme seu email",
            corpoTemplate: EmailTemplateLoader.LoadBody("confirmacao_email_email_v1"));

        // ===== Operacional faltante =====
        yield return TemplateNotificacao.Criar(
            codigo: "produto_vencido_email_v1",
            nome: "Produto Vencido — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ProdutoVencido,
            assuntoTemplate: "EasyStok — Produto VENCIDO: {{ produto_nome }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("produto_vencido_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "produto_vencido_inapp_v1",
            nome: "Produto Vencido — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.ProdutoVencido,
            assuntoTemplate: "Produto vencido: {{ produto_nome }}",
            corpoTemplate: "Lote {{ lote_numero }} de {{ produto_nome }} venceu em {{ data_vencimento }} ({{ quantidade }} un).");

        yield return TemplateNotificacao.Criar(
            codigo: "tarefa_pendente_inapp_v1",
            nome: "Tarefa Pendente — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.TarefaPendente,
            assuntoTemplate: "Tarefa pendente: {{ tarefa_titulo }}",
            corpoTemplate: "A tarefa \"{{ tarefa_titulo }}\" esta pendente. Prazo: {{ prazo }}. Responsavel: {{ responsavel_nome }}.");

        // ===== Helpdesk faltante =====
        yield return TemplateNotificacao.Criar(
            codigo: "ticket_criado_email_v1",
            nome: "Ticket Criado — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.TicketCriado,
            assuntoTemplate: "EasyStok — Novo ticket: {{ titulo }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("ticket_criado_email_v1"));

        // ===== Templates do modulo de Relatorios =====
        yield return TemplateNotificacao.Criar(
            codigo: "relatorio_pronto_inapp_v1",
            nome: "Relatorio Pronto — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.RelatorioPronto,
            assuntoTemplate: "{{ reportLabel }} esta pronto",
            corpoTemplate: "{{ reportLabel }} esta pronto para download.");

        yield return TemplateNotificacao.Criar(
            codigo: "relatorio_pronto_email_v1",
            nome: "Relatorio Pronto — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.RelatorioPronto,
            assuntoTemplate: "Seu relatorio esta pronto: {{ reportLabel }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("relatorio_pronto_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "relatorio_falhou_inapp_v1",
            nome: "Relatorio Falhou — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.RelatorioFalhou,
            assuntoTemplate: "Nao conseguimos gerar: {{ reportLabel }}",
            corpoTemplate: "Nao conseguimos gerar {{ reportLabel }}. {{ errorMensagem }}");

        yield return TemplateNotificacao.Criar(
            codigo: "relatorio_falhou_email_v1",
            nome: "Relatorio Falhou — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.RelatorioFalhou,
            assuntoTemplate: "Nao conseguimos gerar: {{ reportLabel }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("relatorio_falhou_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "ticket_respondido_cliente_inapp_v1",
            nome: "Resposta do cliente — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.TicketRespondidoCliente,
            assuntoTemplate: "Cliente respondeu: {{ titulo }}",
            corpoTemplate: "{{ clienteNome }} da {{ empresaNome }} respondeu o ticket \"{{ titulo }}\".");

        yield return TemplateNotificacao.Criar(
            codigo: "ticket_atribuido_email_v1",
            nome: "Ticket atribuido — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.TicketAtribuido,
            assuntoTemplate: "EasyStok — Ticket atribuido a voce: {{ titulo }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("ticket_atribuido_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "sla_proximo_vencer_email_v1",
            nome: "SLA proximo de vencer — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.SlaProximoVencer,
            assuntoTemplate: "EasyStok — SLA proximo de vencer: {{ titulo }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("sla_proximo_vencer_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "convite_csat_email_v1",
            nome: "Convite CSAT — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ConviteCsat,
            assuntoTemplate: "EasyStok — Como avaliaria o atendimento?",
            corpoTemplate: EmailTemplateLoader.LoadBody("convite_csat_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "convite_csat_inapp_v1",
            nome: "Convite CSAT — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.ConviteCsat,
            assuntoTemplate: "Como foi o atendimento?",
            corpoTemplate: "Avalie o atendimento do ticket \"{{ titulo_ticket }}\".");

        // ===== Financeiro F5 =====
        yield return TemplateNotificacao.Criar(
            codigo: "fatura_criada_email_v1",
            nome: "Fatura Criada — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.FaturaCriada,
            assuntoTemplate: "EasyStok — Fatura {{ numero_fatura }} disponivel",
            corpoTemplate: EmailTemplateLoader.LoadBody("fatura_criada_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "fatura_criada_inapp_v1",
            nome: "Fatura Criada — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.FaturaCriada,
            assuntoTemplate: "Nova fatura: {{ numero_fatura }}",
            corpoTemplate: "Fatura {{ numero_fatura }} ({{ valor }}) vence em {{ vencimento }}.");

        yield return TemplateNotificacao.Criar(
            codigo: "fatura_vencendo_email_v1",
            nome: "Fatura Vencendo — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.FaturaVencendo,
            assuntoTemplate: "EasyStok — Fatura {{ numero_fatura }} vence em {{ dias_restantes }} dia(s)",
            corpoTemplate: EmailTemplateLoader.LoadBody("fatura_vencendo_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "fatura_vencendo_inapp_v1",
            nome: "Fatura Vencendo — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.FaturaVencendo,
            assuntoTemplate: "Fatura vence em {{ dias_restantes }} dias",
            corpoTemplate: "Fatura {{ numero_fatura }} ({{ valor }}) vence em {{ dias_restantes }} dia(s).");

        yield return TemplateNotificacao.Criar(
            codigo: "fatura_paga_email_v1",
            nome: "Fatura Paga — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.FaturaPaga,
            assuntoTemplate: "EasyStok — Pagamento da fatura {{ numero_fatura }} confirmado",
            corpoTemplate: EmailTemplateLoader.LoadBody("fatura_paga_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "fatura_vencida_email_v1",
            nome: "Fatura Vencida — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.FaturaVencida,
            assuntoTemplate: "EasyStok — Fatura {{ numero_fatura }} em atraso",
            corpoTemplate: EmailTemplateLoader.LoadBody("fatura_vencida_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "pagamento_confirmado_inapp_v1",
            nome: "Pagamento Confirmado — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.PagamentoConfirmado,
            assuntoTemplate: "Pagamento confirmado",
            corpoTemplate: "Recebemos seu pagamento de {{ valor }} via {{ metodo }} em {{ data_pagamento }}.");

        yield return TemplateNotificacao.Criar(
            codigo: "pagamento_falhou_email_v1",
            nome: "Pagamento Falhou — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.PagamentoFalhou,
            assuntoTemplate: "EasyStok — Falha no pagamento",
            corpoTemplate: EmailTemplateLoader.LoadBody("pagamento_falhou_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "broadcast_super_admin_inapp_v1",
            nome: "Broadcast SuperAdmin — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.BroadcastSuperAdmin,
            assuntoTemplate: "{{ titulo }}",
            corpoTemplate: "{{ mensagem }}");

        yield return TemplateNotificacao.Criar(
            codigo: "relatorio_expirado_inapp_v1",
            nome: "Relatorio Expirado — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.RelatorioExpirado,
            assuntoTemplate: "Arquivo removido: {{ reportLabel }}",
            corpoTemplate: "O arquivo do relatorio {{ reportLabel }} foi removido apos 30 dias. Gere novamente para baixar.");

        // ===== Templates F5 — Agendamento de Pedidos =====
        yield return TemplateNotificacao.Criar(
            codigo: "pedido_agendado_hoje_inapp_v1",
            nome: "Pedido agendado hoje — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.PedidoAgendadoHoje,
            assuntoTemplate: "Pedido agendado para hoje",
            corpoTemplate: "Pedido de {{ clienteNome }} agendado para hoje ({{ scheduledFor }}).");

        yield return TemplateNotificacao.Criar(
            codigo: "pedido_agendado_1h_inapp_v1",
            nome: "Pedido agendado em 1 hora — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.PedidoAgendadoEm1Hora,
            assuntoTemplate: "Pedido em 1 hora",
            corpoTemplate: "Pedido de {{ clienteNome }} em 1 hora ({{ scheduledFor }}).");

        yield return TemplateNotificacao.Criar(
            codigo: "pedido_agendado_10min_inapp_v1",
            nome: "Pedido agendado em 10 minutos — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.PedidoAgendadoEm10Minutos,
            assuntoTemplate: "Pedido em 10 minutos",
            corpoTemplate: "Pedido de {{ clienteNome }} em 10 minutos — prepare-se ({{ scheduledFor }}).");

        // ===== Caixa esquecido aberto (#641) =====
        yield return TemplateNotificacao.Criar(
            codigo: "caixa_esquecido_aberto_inapp_v1",
            nome: "Caixa Esquecido Aberto — In-App",
            canal: CanalNotificacao.InApp,
            tipoEvento: TipoEventoNotificacao.CaixaAbertoEsquecido,
            assuntoTemplate: "Caixa aberto desde {{ data_abertura }}",
            corpoTemplate: "O caixa segue aberto desde {{ data_abertura }} (saldo de abertura {{ valor_abertura }}). Feche-o para nao acumular vendas no dia errado.");
    }

    private static IEnumerable<RotinaNotificacao> BuildDefaultRotinas()
    {
        var rotinaCobranca = RotinaNotificacao.Criar(
            codigo: "assinatura_expirando_global",
            nome: "Aviso de Vencimento de Assinatura",
            tipoEvento: TipoEventoNotificacao.AssinaturaExpirando,
            triggerTipo: TriggerTipoRotina.Evento,
            templateCodigo: "assinatura_expirando_email_v1",
            categoria: CategoriaConteudoNotificacao.Transacional);
        rotinaCobranca.DefinirFallback("[\"Email\"]", "system");
        yield return rotinaCobranca;

        var rotinaDunning = RotinaNotificacao.Criar(
            codigo: "assinatura_expirada_dunning_global",
            nome: "Dunning — Pagamento Pendente",
            tipoEvento: TipoEventoNotificacao.AssinaturaExpirada,
            triggerTipo: TriggerTipoRotina.Evento,
            templateCodigo: "assinatura_expirada_dunning_email_v1",
            categoria: CategoriaConteudoNotificacao.Transacional);
        rotinaDunning.DefinirFallback("[\"Email\"]", "system");
        yield return rotinaDunning;

        var rotinaEstoque = RotinaNotificacao.Criar(
            codigo: "alerta_estoque_critico_global",
            nome: "Alerta de Estoque Crítico",
            tipoEvento: TipoEventoNotificacao.AlertaEstoqueCritico,
            triggerTipo: TriggerTipoRotina.Evento,
            templateCodigo: "alerta_estoque_critico_email_v1",
            categoria: CategoriaConteudoNotificacao.Operacional);
        rotinaEstoque.DefinirFallback("[\"Email\",\"InApp\"]", "system");
        yield return rotinaEstoque;

        var rotinaProdutoVencendo = RotinaNotificacao.Criar(
            codigo: "produto_vencendo_global",
            nome: "Produto Próximo ao Vencimento",
            tipoEvento: TipoEventoNotificacao.ProdutoVencendo,
            triggerTipo: TriggerTipoRotina.Evento,
            templateCodigo: "produto_vencendo_email_v1",
            categoria: CategoriaConteudoNotificacao.Operacional);
        rotinaProdutoVencendo.DefinirFallback("[\"Email\",\"InApp\"]", "system");
        yield return rotinaProdutoVencendo;

        // ===== Rotinas do modulo Helpdesk =====
        yield return MakeRotina("ticket_criado_global", "Ticket Criado",
            TipoEventoNotificacao.TicketCriado, "ticket_criado_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\",\"Email\"]");

        yield return MakeRotina("ticket_respondido_admin_global", "Resposta do Atendente para o Cliente",
            TipoEventoNotificacao.TicketRespondidoAdmin, "ticket_respondido_admin_inapp_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"InApp\",\"Email\"]");

        yield return MakeRotina("ticket_status_alterado_global", "Status do Ticket Alterado",
            TipoEventoNotificacao.TicketStatusAlterado, "ticket_status_alterado_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

        yield return MakeRotina("ticket_atribuido_global", "Ticket Atribuido",
            TipoEventoNotificacao.TicketAtribuido, "ticket_atribuido_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

        yield return MakeRotina("ticket_encaminhado_global", "Ticket Encaminhado entre Niveis",
            TipoEventoNotificacao.TicketEncaminhadoNivel, "ticket_encaminhado_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

        yield return MakeRotina("sla_proximo_vencer_global", "SLA Proximo de Vencer",
            TipoEventoNotificacao.SlaProximoVencer, "sla_proximo_vencer_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

        yield return MakeRotina("sla_violado_global", "SLA Violado",
            TipoEventoNotificacao.SlaViolado, "sla_violado_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\",\"Email\"]");

        yield return MakeRotina("bug_fix_criado_global", "Bug-fix Encaminhado para Desenvolvimento",
            TipoEventoNotificacao.BugFixCriado, "bug_fix_criado_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\",\"Email\"]");

        // ===== Auth =====
        yield return MakeRotina("reset_senha_global", "Reset de Senha",
            TipoEventoNotificacao.ResetSenha, "reset_senha_email_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"Email\"]");

        yield return MakeRotina("confirmacao_email_global", "Confirmacao de Email",
            TipoEventoNotificacao.ConfirmacaoEmail, "confirmacao_email_email_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"Email\"]");

        // ===== Operacional faltante =====
        yield return MakeRotina("produto_vencido_global", "Produto Vencido",
            TipoEventoNotificacao.ProdutoVencido, "produto_vencido_email_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"Email\",\"InApp\"]");

        yield return MakeRotina("tarefa_pendente_global", "Tarefa Pendente",
            TipoEventoNotificacao.TarefaPendente, "tarefa_pendente_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

        // ===== Helpdesk faltante =====
        yield return MakeRotina("ticket_respondido_cliente_global", "Resposta do Cliente",
            TipoEventoNotificacao.TicketRespondidoCliente, "ticket_respondido_cliente_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\",\"Email\"]");

        yield return MakeRotina("convite_csat_global", "Convite CSAT pos fechamento",
            TipoEventoNotificacao.ConviteCsat, "convite_csat_email_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"Email\",\"InApp\"]");

        // ===== Financeiro F5 =====
        yield return MakeRotina("fatura_criada_global", "Fatura Criada",
            TipoEventoNotificacao.FaturaCriada, "fatura_criada_email_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"Email\",\"InApp\"]");

        yield return MakeRotina("fatura_vencendo_global", "Fatura Vencendo",
            TipoEventoNotificacao.FaturaVencendo, "fatura_vencendo_email_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"Email\",\"InApp\"]");

        yield return MakeRotina("fatura_paga_global", "Fatura Paga",
            TipoEventoNotificacao.FaturaPaga, "fatura_paga_email_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"Email\"]");

        yield return MakeRotina("fatura_vencida_global", "Fatura Vencida",
            TipoEventoNotificacao.FaturaVencida, "fatura_vencida_email_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"Email\"]");

        yield return MakeRotina("pagamento_confirmado_global", "Pagamento Confirmado",
            TipoEventoNotificacao.PagamentoConfirmado, "pagamento_confirmado_inapp_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"InApp\",\"Email\"]");

        yield return MakeRotina("pagamento_falhou_global", "Pagamento Falhou",
            TipoEventoNotificacao.PagamentoFalhou, "pagamento_falhou_email_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"Email\"]");

        // ===== Broadcast =====
        yield return MakeRotina("broadcast_super_admin_global", "Broadcast Super Admin",
            TipoEventoNotificacao.BroadcastSuperAdmin, "broadcast_super_admin_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

        // ===== Rotinas do modulo de Relatorios =====
        yield return MakeRotina("relatorio_pronto_global", "Relatorio Pronto",
            TipoEventoNotificacao.RelatorioPronto, "relatorio_pronto_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\",\"Email\"]");

        yield return MakeRotina("relatorio_falhou_global", "Relatorio Falhou",
            TipoEventoNotificacao.RelatorioFalhou, "relatorio_falhou_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\",\"Email\"]");

        yield return MakeRotina("relatorio_expirado_global", "Relatorio Expirado",
            TipoEventoNotificacao.RelatorioExpirado, "relatorio_expirado_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

        // ===== Rotinas F5 — Agendamento de Pedidos =====
        yield return MakeRotina("pedido_agendado_hoje_global", "Pedido agendado hoje",
            TipoEventoNotificacao.PedidoAgendadoHoje, "pedido_agendado_hoje_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

        yield return MakeRotina("pedido_agendado_1h_global", "Pedido agendado em 1 hora",
            TipoEventoNotificacao.PedidoAgendadoEm1Hora, "pedido_agendado_1h_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

        yield return MakeRotina("pedido_agendado_10min_global", "Pedido agendado em 10 minutos",
            TipoEventoNotificacao.PedidoAgendadoEm10Minutos, "pedido_agendado_10min_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

        // ===== Caixa esquecido aberto (#641) =====
        yield return MakeRotina("caixa_esquecido_aberto_global", "Caixa Esquecido Aberto",
            TipoEventoNotificacao.CaixaAbertoEsquecido, "caixa_esquecido_aberto_inapp_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");
    }

    private static RotinaNotificacao MakeRotina(
        string codigo, string nome,
        TipoEventoNotificacao evento, string templateCodigo,
        CategoriaConteudoNotificacao categoria, string fallbackJson)
    {
        var r = RotinaNotificacao.Criar(
            codigo: codigo, nome: nome, tipoEvento: evento,
            triggerTipo: TriggerTipoRotina.Evento,
            templateCodigo: templateCodigo, categoria: categoria);
        r.DefinirFallback(fallbackJson, "system");
        return r;
    }
}
