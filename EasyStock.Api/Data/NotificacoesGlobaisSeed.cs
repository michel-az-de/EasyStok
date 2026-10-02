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
    /// <summary>Autor das linhas que o seed escreve; o que tem outro autor foi mexido por pessoa e o seed não sobrescreve (N13).</summary>
    public const string AutorSistema = "system";

    /// <param name="templates">Catálogo de templates; omitido, é o <see cref="BuildDefaultTemplates"/>. Existe para o teste subir a versão.</param>
    /// <param name="rotinas">Catálogo de rotinas; omitido, é o <see cref="BuildDefaultRotinas"/>.</param>
    public static async Task ExecutarAsync(
        EasyStockDbContext context,
        ILogger logger,
        IReadOnlyCollection<TemplateNotificacao>? templates = null,
        IReadOnlyCollection<RotinaNotificacao>? rotinas = null)
    {
        var seeded = false;
        seeded |= await SeedConfiguracoesCanal(context, logger);
        seeded |= await SeedTemplates(context, logger, templates ?? BuildDefaultTemplates().ToList());
        seeded |= await SeedRotinas(context, logger, rotinas ?? BuildDefaultRotinas().ToList());

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

        // Push (S07, S43): so as rotinas de ConversaEscalada e LembreteVencido o usam; sem VAPID configurado o WebPushCanal falha limpo.
        var canais = new[] { CanalNotificacao.Email, CanalNotificacao.Sms, CanalNotificacao.WhatsApp, CanalNotificacao.InApp, CanalNotificacao.Push };
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

    /// <summary>
    /// Templates globais versionados (N13). Por <c>Codigo</c>: ausente, insere aprovado e ativo; presente com a última
    /// linha do sistema e <c>Versao</c> menor que a do catálogo, desativa a vigente e insere a linha nova (o histórico
    /// fica); presente e editado por pessoa, não mexe e avisa. A segunda execução não escreve nada.
    /// </summary>
    private static async Task<bool> SeedTemplates(
        EasyStockDbContext context, ILogger logger, IReadOnlyCollection<TemplateNotificacao> catalogo)
    {
        // Ver comentario em SeedConfiguracoesCanal — HasQueryFilter global zera a leitura
        // de globais (EmpresaId IS NULL) durante seed; IgnoreQueryFilters restaura.
        var existentes = (await context.NotifTemplates
                .IgnoreQueryFilters()
                .Where(t => t.EmpresaId == null)
                .ToListAsync())
            .GroupBy(t => t.Codigo)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.Versao).ToList());

        var alterou = false;

        foreach (var t in catalogo)
        {
            if (!existentes.TryGetValue(t.Codigo, out var linhas))
            {
                Publicar(t);
                logger.LogInformation("NotificacoesGlobaisSeed: Template global adicionado: {Codigo}", t.Codigo);
                alterou = true;
                continue;
            }

            var ultima = linhas[0];
            if (ultima.AtualizadoPor != AutorSistema)
            {
                logger.LogWarning(
                    "NotificacoesGlobaisSeed: Template global {Codigo} foi editado por {Autor}; o catálogo não sobrescreve",
                    t.Codigo, ultima.AtualizadoPor);
                continue;
            }

            if (ultima.Versao >= t.Versao) continue;

            foreach (var vigente in linhas.Where(l => l.Ativo)) vigente.Desativar();
            Publicar(t);
            logger.LogInformation(
                "NotificacoesGlobaisSeed: Template global {Codigo} subiu da versão {De} para {Para}",
                t.Codigo, ultima.Versao, t.Versao);
            alterou = true;
        }

        return alterou;

        void Publicar(TemplateNotificacao t)
        {
            t.Aprovar(AutorSistema);
            t.Ativar();
            context.NotifTemplates.Add(t);
        }
    }

    /// <summary>
    /// Rotinas globais (N13). Sem coluna de versão: compara o conteúdo com o catálogo e atualiza no lugar quando a
    /// rotina é do sistema (<c>AtualizadaPor = "system"</c>). Rotina mexida por pessoa, inclusive desligada, fica como
    /// está, com aviso. Rotina nova nasce <c>Ativa</c> conforme o catálogo.
    /// </summary>
    private static async Task<bool> SeedRotinas(
        EasyStockDbContext context, ILogger logger, IReadOnlyCollection<RotinaNotificacao> catalogo)
    {
        // Ver comentario em SeedConfiguracoesCanal — HasQueryFilter global zera a leitura
        // de globais (EmpresaId IS NULL) durante seed; IgnoreQueryFilters restaura.
        var existentes = (await context.NotifRotinas
                .IgnoreQueryFilters()
                .Where(r => r.EmpresaId == null)
                .ToListAsync())
            .ToDictionary(r => r.Codigo);

        var alterou = false;

        foreach (var r in catalogo)
        {
            if (!existentes.TryGetValue(r.Codigo, out var atual))
            {
                context.NotifRotinas.Add(r);
                logger.LogInformation("NotificacoesGlobaisSeed: Rotina global adicionada: {Codigo}", r.Codigo);
                alterou = true;
                continue;
            }

            if (atual.EquivaleAoCatalogo(r)) continue;

            if (!atual.EhDoSistema)
            {
                logger.LogWarning(
                    "NotificacoesGlobaisSeed: Rotina global {Codigo} foi alterada por {Autor}; o catálogo não sobrescreve",
                    r.Codigo, atual.AtualizadaPor);
                continue;
            }

            atual.AplicarCatalogo(r);
            logger.LogInformation("NotificacoesGlobaisSeed: Rotina global {Codigo} atualizada pelo catálogo", r.Codigo);
            alterou = true;
        }

        return alterou;
    }

    public static IEnumerable<TemplateNotificacao> BuildDefaultTemplates()
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

        // ===== Atendimento por WhatsApp (S07): o agente escalou a conversa para a dona. Push para todas
        // as subscriptions da empresa (payload sem usuarioId => destinatario "empresa:{id}"). =====
        yield return TemplateNotificacao.Criar(
            codigo: "conversa_escalada_push_v1",
            nome: "Conversa Escalada — Push",
            canal: CanalNotificacao.Push,
            tipoEvento: TipoEventoNotificacao.ConversaEscalada,
            assuntoTemplate: "{{ cliente }} precisa de você",
            corpoTemplate: "{{ cliente }} precisa de você: {{ motivo }}");

        // ===== Lembretes da dona (S43): Push para quem e o lembrete (payload com usuarioId) ou para a
        // empresa toda (sem usuarioId). Interno: nada sai para o cliente. =====
        yield return TemplateNotificacao.Criar(
            codigo: "lembrete_vencido_push_v1",
            nome: "Lembrete da Dona — Push",
            canal: CanalNotificacao.Push,
            tipoEvento: TipoEventoNotificacao.LembreteVencido,
            assuntoTemplate: "Lembrete",
            corpoTemplate: "{{ texto }}");

        // ===== Campanhas (S30): carregam a mensagem da onda e o lembrete do encerramento pelo WhatsApp. Sem
        // rotina: o EnfileiradorMensagensCampanha escreve direto no outbox (categoria Marketing) e monta os
        // metadados da Meta (campanha_generica / campanha_lembrete ou o template da propria campanha). =====
        yield return TemplateNotificacao.Criar(
            codigo: "campanha_marketing_whatsapp_v1",
            nome: "Campanha — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.CampanhaMarketing,
            assuntoTemplate: "",
            corpoTemplate: "{{ mensagem }}\n\nPara não receber mais, responda SAIR.");

        yield return TemplateNotificacao.Criar(
            codigo: "campanha_lembrete_whatsapp_v1",
            nome: "Campanha Lembrete de Encerramento — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.CampanhaLembreteEncerramento,
            assuntoTemplate: "",
            corpoTemplate: "{{ nome }}, a campanha {{ campanha }} está terminando. Ainda dá tempo de pedir!\n\nPara não receber mais, responda SAIR.");

        // ===== Avisos de status do pedido ao cliente pelo WhatsApp (S13). Corpo = texto dentro da janela de
        // 24 h; MetadadosJson = template aprovado na Meta (onda 0.3) e parametros, usados fora da janela. =====
        yield return ComMetadados(TemplateNotificacao.Criar(
            codigo: "pedido_pago_whatsapp_v1",
            nome: "Pedido Pago — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.PedidoPagoConfirmado,
            assuntoTemplate: "",
            corpoTemplate: "Oi, {{ nome }}! Recebemos seu pagamento. Pedido nº {{ numero }} confirmado, previsão de entrega: {{ previsao }}. Obrigada pela preferência!"),
            """{"template":"pedido_pago","idioma":"pt_BR","param1":"{{ nome }}","param2":"{{ numero }}","param3":"{{ previsao }}"}""");

        yield return ComMetadados(TemplateNotificacao.Criar(
            codigo: "pedido_em_preparo_whatsapp_v1",
            nome: "Pedido Em Preparo — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.PedidoEmPreparo,
            assuntoTemplate: "",
            corpoTemplate: "{{ nome }}, seu pedido nº {{ numero }} já está em preparo na nossa cozinha. Previsão de entrega: {{ previsao }}."),
            """{"template":"pedido_em_preparo","idioma":"pt_BR","param1":"{{ nome }}","param2":"{{ numero }}","param3":"{{ previsao }}"}""");

        yield return ComMetadados(TemplateNotificacao.Criar(
            codigo: "pedido_saiu_whatsapp_v1",
            nome: "Pedido Saiu Para Entrega — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.PedidoSaiuParaEntrega,
            assuntoTemplate: "",
            corpoTemplate: "{{ nome }}, seu pedido nº {{ numero }} saiu para entrega e logo chega até você."),
            """{"template":"pedido_saiu","idioma":"pt_BR","param1":"{{ nome }}","param2":"{{ numero }}"}""");

        yield return ComMetadados(TemplateNotificacao.Criar(
            codigo: "pedido_entregue_whatsapp_v1",
            nome: "Pedido Entregue — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.PedidoEntregue,
            assuntoTemplate: "",
            corpoTemplate: """Pedido nº {{ numero }} entregue. Obrigada pela preferência, {{ nome }}!{{ if instagram != "" }} Siga a gente no Instagram: {{ instagram }}{{ end }}"""),
            """{"template":"pedido_entregue","idioma":"pt_BR","param1":"{{ nome }}"}""");

        // S26: avaliação em dois botões 30 min após a entrega. Dentro da janela sai interativa com os botões;
        // fora, o template "avaliacao" com os mesmos payloads como quick reply (voltam em button.payload).
        yield return ComMetadados(TemplateNotificacao.Criar(
            codigo: "avaliacao_whatsapp_v1",
            nome: "Pedido de Avaliação — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.AvaliacaoSolicitada,
            assuntoTemplate: "",
            corpoTemplate: "{{ nome }}, o que achou do pedido nº {{ numero }}? É só tocar num botão."),
            """{"template":"avaliacao","idioma":"pt_BR","param1":"{{ nome }}","botao1":"acao:avaliacao:positiva:{{ pedidoId }}|Gostei","botao2":"acao:avaliacao:negativa:{{ pedidoId }}|Não gostei"}""");

        // #1292: aviso de reembolso da ocorrencia (S27). Fora da janela de 24 h usa o modelo "reembolso_efetuado",
        // que precisa ser aprovado no WhatsApp Manager como os da onda 0.3.
        yield return ComMetadados(TemplateNotificacao.Criar(
            codigo: "reembolso_efetuado_whatsapp_v1",
            nome: "Reembolso Efetuado — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.ReembolsoEfetuado,
            assuntoTemplate: "",
            corpoTemplate: "{{ nome }}, devolvemos {{ valor }} referente ao pedido nº {{ numero }}. O prazo para aparecer no seu extrato depende do banco. Desculpe o transtorno!"),
            """{"template":"reembolso_efetuado","idioma":"pt_BR","param1":"{{ nome }}","param2":"{{ valor }}","param3":"{{ numero }}"}""");

        // ===== ADM-09 (#744): templates minimos SMS/WhatsApp p/ eventos criticos de cobranca/SLA.
        // Sem assunto (SMS/WhatsApp nao tem). Ficam inertes ate configurar provider Twilio/Meta e
        // ativar o canal em ConfiguracaoCanal (AtivoNoTenant); o objetivo aqui e cobrir o filtro
        // de canal que antes retornava "Nenhum template". =====
        yield return TemplateNotificacao.Criar(
            codigo: "fatura_vencida_sms_v1",
            nome: "Fatura Vencida — SMS",
            canal: CanalNotificacao.Sms,
            tipoEvento: TipoEventoNotificacao.FaturaVencida,
            assuntoTemplate: "",
            corpoTemplate: "EasyStok: fatura {{ numero_fatura }} ({{ valor }}) em atraso ha {{ dias_em_atraso }} dia(s). Regularize: {{ link_pagamento }}");

        yield return TemplateNotificacao.Criar(
            codigo: "fatura_vencida_whatsapp_v1",
            nome: "Fatura Vencida — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.FaturaVencida,
            assuntoTemplate: "",
            corpoTemplate: "Ola {{ nome }}! Sua fatura {{ numero_fatura }} ({{ valor }}) esta em atraso ha {{ dias_em_atraso }} dia(s). Para evitar a suspensao do servico, regularize: {{ link_pagamento }}");

        yield return TemplateNotificacao.Criar(
            codigo: "pagamento_falhou_sms_v1",
            nome: "Pagamento Falhou — SMS",
            canal: CanalNotificacao.Sms,
            tipoEvento: TipoEventoNotificacao.PagamentoFalhou,
            assuntoTemplate: "",
            corpoTemplate: "EasyStok: o pagamento de {{ valor }} falhou ({{ motivo }}). Tente novamente: {{ link_retry }}");

        yield return TemplateNotificacao.Criar(
            codigo: "pagamento_falhou_whatsapp_v1",
            nome: "Pagamento Falhou — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.PagamentoFalhou,
            assuntoTemplate: "",
            corpoTemplate: "Ola {{ nome }}! O pagamento de {{ valor }} via {{ metodo }} nao foi concluido ({{ motivo }}). Tente novamente para manter seu plano ativo: {{ link_retry }}");

        yield return TemplateNotificacao.Criar(
            codigo: "sla_violado_sms_v1",
            nome: "SLA Violado — SMS",
            canal: CanalNotificacao.Sms,
            tipoEvento: TipoEventoNotificacao.SlaViolado,
            assuntoTemplate: "",
            corpoTemplate: "EasyStok: SLA {{ tipoSla }} violado no chamado \"{{ titulo }}\" ({{ prioridade }}/{{ nivel }}) - {{ empresaNome }}.");

        yield return TemplateNotificacao.Criar(
            codigo: "sla_violado_whatsapp_v1",
            nome: "SLA Violado — WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.SlaViolado,
            assuntoTemplate: "",
            corpoTemplate: "SLA {{ tipoSla }} violado: chamado \"{{ titulo }}\" da {{ empresaNome }} - prioridade {{ prioridade }}, nivel {{ nivel }}. Requer atencao.");

        // ===== Catalogo de plataforma (N13). E-mail em Data/Templates/Email/{codigo}.html; texto de WhatsApp aqui,
        // com os metadados do modelo da Meta (nome aprovado no WhatsApp Manager e param1..N). Os modelos
        // precisam estar aprovados na Meta antes de a N6 enviar de verdade. =====
        yield return TemplateNotificacao.Criar(
            codigo: "reset_senha_whatsapp_v1",
            nome: "Reset de Senha · WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.ResetSenha,
            assuntoTemplate: "",
            corpoTemplate: "{{ codigo }} é o seu código de verificação do EasyStok. Ele vale por {{ expira_em_minutos }} minutos. Não compartilhe com ninguém.")
            .ComMetadados(
                """{"template":"codigo_redefinir_senha","idioma":"pt_BR","param1":"{{ codigo }}","botaoUrl0":"{{ codigo }}"}""");

        yield return TemplateNotificacao.Criar(
            codigo: "convite_acesso_email_v1",
            nome: "Convite de Acesso · Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ConviteAcesso,
            assuntoTemplate: "EasyStok: convite para acessar {{ empresa }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("convite_acesso_email_v1"));

        // O convite ja nasce como o modelo "convite_acesso_link" (botao URL), para nao aprovar dois modelos na
        // Meta; a N9 acrescenta o token do botao (botaoUrl0) e sobe este template para a versao 2.
        yield return TemplateNotificacao.Criar(
            codigo: "convite_acesso_whatsapp_v1",
            nome: "Convite de Acesso · WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.ConviteAcesso,
            assuntoTemplate: "",
            corpoTemplate: "Olá, {{ nome }}! Você foi convidado para o EasyStok da empresa {{ empresa }}. Use o link para criar sua senha: {{ link_convite }}")
            .ComMetadados(
                """{"template":"convite_acesso_link","idioma":"pt_BR","param1":"{{ nome }}","param2":"{{ empresa }}"}""");

        yield return TemplateNotificacao.Criar(
            codigo: "incidente_sistema_email_v1",
            nome: "Incidente do Sistema · Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.IncidenteSistema,
            assuntoTemplate: "EasyStok: {{ componente }} {{ estado_texto }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("incidente_sistema_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "incidente_sistema_whatsapp_v1",
            nome: "Incidente do Sistema · WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.IncidenteSistema,
            assuntoTemplate: "",
            corpoTemplate: "EasyStok informa: o componente {{ componente }} está {{ estado_texto }} desde {{ desde }}. Veja o resumo no e-mail enviado agora.")
            .ComMetadados(
                """{"template":"incidente_sistema","idioma":"pt_BR","param1":"{{ componente }}","param2":"{{ estado_texto }}","param3":"{{ desde }}"}""");

        yield return TemplateNotificacao.Criar(
            codigo: "prazo_estourado_email_v1",
            nome: "Prazo Estourado · Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.PrazoEstourado,
            assuntoTemplate: "EasyStok: {{ tipo_legivel }} há {{ atraso_texto }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("prazo_estourado_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "prazo_estourado_whatsapp_v1",
            nome: "Prazo Estourado · WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.PrazoEstourado,
            assuntoTemplate: "",
            corpoTemplate: "Atenção na loja: {{ tipo_legivel }} há {{ atraso_texto }} (referência {{ referencia }}). Abra o EasyStok para resolver.")
            .ComMetadados(
                """{"template":"prazo_estourado","idioma":"pt_BR","param1":"{{ tipo_legivel }}","param2":"{{ atraso_texto }}","param3":"{{ referencia }}"}""");

        yield return TemplateNotificacao.Criar(
            codigo: "resumo_diario_email_v1",
            nome: "Resumo Diário · Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ResumoDiario,
            assuntoTemplate: "EasyStok: resumo de {{ data }}",
            corpoTemplate: EmailTemplateLoader.LoadBody("resumo_diario_email_v1"));

        yield return TemplateNotificacao.Criar(
            codigo: "resumo_diario_whatsapp_v1",
            nome: "Resumo Diário · WhatsApp",
            canal: CanalNotificacao.WhatsApp,
            tipoEvento: TipoEventoNotificacao.ResumoDiario,
            assuntoTemplate: "",
            corpoTemplate: "Resumo de {{ data }} no EasyStok: {{ entregues }} pedidos entregues, faturamento de {{ faturamento }} e caixa {{ caixa_texto }}. O resumo completo está no seu e-mail.")
            .ComMetadados(
                """{"template":"resumo_diario","idioma":"pt_BR","param1":"{{ data }}","param2":"{{ entregues }}","param3":"{{ faturamento }}","param4":"{{ caixa_texto }}"}""");
    }

    public static IEnumerable<RotinaNotificacao> BuildDefaultRotinas()
    {
        var rotinaCobranca = RotinaNotificacao.Criar(
            codigo: "assinatura_expirando_global",
            nome: "Aviso de Vencimento de Assinatura",
            tipoEvento: TipoEventoNotificacao.AssinaturaExpirando,
            triggerTipo: TriggerTipoRotina.Evento,
            templateCodigo: "assinatura_expirando_email_v1",
            categoria: CategoriaConteudoNotificacao.Transacional);
        rotinaCobranca.DefinirFallback("[\"Email\"]", "system");
        rotinaCobranca.Ativar("system");
        yield return rotinaCobranca;

        var rotinaDunning = RotinaNotificacao.Criar(
            codigo: "assinatura_expirada_dunning_global",
            nome: "Dunning — Pagamento Pendente",
            tipoEvento: TipoEventoNotificacao.AssinaturaExpirada,
            triggerTipo: TriggerTipoRotina.Evento,
            templateCodigo: "assinatura_expirada_dunning_email_v1",
            categoria: CategoriaConteudoNotificacao.Transacional);
        rotinaDunning.DefinirFallback("[\"Email\"]", "system");
        rotinaDunning.Ativar("system");
        yield return rotinaDunning;

        var rotinaEstoque = RotinaNotificacao.Criar(
            codigo: "alerta_estoque_critico_global",
            nome: "Alerta de Estoque Crítico",
            tipoEvento: TipoEventoNotificacao.AlertaEstoqueCritico,
            triggerTipo: TriggerTipoRotina.Evento,
            templateCodigo: "alerta_estoque_critico_email_v1",
            categoria: CategoriaConteudoNotificacao.Operacional);
        rotinaEstoque.DefinirFallback("[\"Email\"]", "system");
        rotinaEstoque.Ativar("system");
        yield return rotinaEstoque;

        var rotinaProdutoVencendo = RotinaNotificacao.Criar(
            codigo: "produto_vencendo_global",
            nome: "Produto Próximo ao Vencimento",
            tipoEvento: TipoEventoNotificacao.ProdutoVencendo,
            triggerTipo: TriggerTipoRotina.Evento,
            templateCodigo: "produto_vencendo_email_v1",
            categoria: CategoriaConteudoNotificacao.Operacional);
        rotinaProdutoVencendo.DefinirFallback("[\"Email\"]", "system");
        rotinaProdutoVencendo.Ativar("system");
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
            CategoriaConteudoNotificacao.Seguranca, "[\"Email\",\"WhatsApp\"]", ModoTodos("usuario"));

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
            CategoriaConteudoNotificacao.Operacional, "[\"InApp\"]");

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
            CategoriaConteudoNotificacao.Transacional, "[\"InApp\"]");

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

        // ===== Atendimento por WhatsApp (S07) =====
        yield return MakeRotina("conversa_escalada_global", "Conversa Escalada para a Dona",
            TipoEventoNotificacao.ConversaEscalada, "conversa_escalada_push_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"Push\"]");

        // ===== Lembretes da dona (S43) =====
        yield return MakeRotina("lembrete_vencido_global", "Lembrete da Dona",
            TipoEventoNotificacao.LembreteVencido, "lembrete_vencido_push_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"Push\"]");

        // ===== Avisos de status do pedido ao cliente (S13): transacionais, so WhatsApp, sem janela de horario
        // (aviso de status nao espera). Rollback: desativar a rotina (Ativa=false). =====
        yield return MakeRotina("pedido_pago_confirmado_global", "Pedido Pago — Aviso ao Cliente",
            TipoEventoNotificacao.PedidoPagoConfirmado, "pedido_pago_whatsapp_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"WhatsApp\"]");
        yield return MakeRotina("pedido_em_preparo_global", "Pedido Em Preparo — Aviso ao Cliente",
            TipoEventoNotificacao.PedidoEmPreparo, "pedido_em_preparo_whatsapp_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"WhatsApp\"]");
        yield return MakeRotina("pedido_saiu_para_entrega_global", "Pedido Saiu Para Entrega — Aviso ao Cliente",
            TipoEventoNotificacao.PedidoSaiuParaEntrega, "pedido_saiu_whatsapp_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"WhatsApp\"]");
        yield return MakeRotina("pedido_entregue_global", "Pedido Entregue — Agradecimento ao Cliente",
            TipoEventoNotificacao.PedidoEntregue, "pedido_entregue_whatsapp_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"WhatsApp\"]");
        yield return MakeRotina("avaliacao_solicitada_global", "Pedido de Avaliação — 30 min após a entrega",
            TipoEventoNotificacao.AvaliacaoSolicitada, "avaliacao_whatsapp_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"WhatsApp\"]");

        // ===== Reembolso da ocorrencia (S27, #1292): transacional, so WhatsApp. Rollback: Ativa=false. =====
        yield return MakeRotina("reembolso_efetuado_global", "Reembolso Efetuado — Aviso ao Cliente",
            TipoEventoNotificacao.ReembolsoEfetuado, "reembolso_efetuado_whatsapp_v1",
            CategoriaConteudoNotificacao.Transacional, "[\"WhatsApp\"]");

        // ===== Catalogo de plataforma (N13): e-mail e WhatsApp, modo "todos". A rotina guarda modo e audiencia em
        // ParametrosJson; a leitura e da N5 (modo) e da N4 (audiencia), ate la as chaves sao inertes. Rollback:
        // Ativa=false nas quatro rotinas novas antes de reverter o codigo (o enum guarda o nome como texto). =====
        yield return MakeRotina("convite_acesso_global", "Convite de Acesso",
            TipoEventoNotificacao.ConviteAcesso, "convite_acesso_email_v1",
            CategoriaConteudoNotificacao.Seguranca, "[\"Email\",\"WhatsApp\"]", ModoTodos());

        yield return MakeRotina("incidente_sistema_global", "Incidente do Sistema",
            TipoEventoNotificacao.IncidenteSistema, "incidente_sistema_email_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"Email\",\"WhatsApp\"]", ModoTodos("superadmins"));

        var prazoEstourado = MakeRotina("prazo_estourado_global", "Prazo Estourado",
            TipoEventoNotificacao.PrazoEstourado, "prazo_estourado_email_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"Email\",\"WhatsApp\"]", ModoTodos("gestores"));
        prazoEstourado.DefinirJanela(new TimeOnly(7, 0), new TimeOnly(22, 0));
        yield return prazoEstourado;

        // Molde: nasce inativa e liga por empresa na N12.
        var resumoDiario = MakeRotina("resumo_diario_global", "Resumo Diário",
            TipoEventoNotificacao.ResumoDiario, "resumo_diario_email_v1",
            CategoriaConteudoNotificacao.Operacional, "[\"Email\",\"WhatsApp\"]", ModoTodos("admins"));
        resumoDiario.Desativar("system");
        yield return resumoDiario;
    }

    private static TemplateNotificacao ComMetadados(this TemplateNotificacao template, string metadadosJson)
    {
        template.DefinirMetadados(metadadosJson);
        return template;
    }

    /// <summary>Parametros do catalogo de plataforma: modo de canais e, quando ha, a audiencia que a N4 le.</summary>
    private static string ModoTodos(string? audiencia = null) =>
        audiencia is null
            ? """{"modoCanais":"todos"}"""
            : $$"""{"modoCanais":"todos","audiencia":"{{audiencia}}"}""";

    private static RotinaNotificacao MakeRotina(
        string codigo, string nome,
        TipoEventoNotificacao evento, string templateCodigo,
        CategoriaConteudoNotificacao categoria, string fallbackJson, string? parametrosJson = null)
    {
        var r = RotinaNotificacao.Criar(
            codigo: codigo, nome: nome, tipoEvento: evento,
            triggerTipo: TriggerTipoRotina.Evento,
            templateCodigo: templateCodigo, categoria: categoria);
        r.DefinirFallback(fallbackJson, AutorSistema);
        if (parametrosJson is not null) r.DefinirParametros(parametrosJson, AutorSistema);
        r.Ativar(AutorSistema);
        return r;
    }
}
