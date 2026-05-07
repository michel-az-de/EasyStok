using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
        var existentes = await context.NotifConfiguracoesCanal
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
        var existentes = await context.NotifTemplates
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
        var existentes = await context.NotifRotinas
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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#4f46e5">Renovação da sua assinatura EasyStock</h2>
                <p>Olá, <strong>{{ nome }}</strong>!</p>
                <p>Sua assinatura vence em <strong>{{ vencimento }}</strong>. Para continuar usando o EasyStock sem interrupções, realize o pagamento via Pix:</p>
                <p><strong>Valor:</strong> {{ valor }}</p>
                {{ if qr_code != "" }}<img src="data:image/png;base64,{{ qr_code }}" alt="QR Code Pix" style="width:200px;height:200px" />{{ end }}
                <p style="margin-top:16px"><strong>Pix Copia e Cola:</strong></p>
                <pre style="background:#f3f4f6;padding:12px;border-radius:6px;word-break:break-all;font-size:12px">{{ pix_copia_cola }}</pre>
                <p style="color:#6b7280;font-size:12px">Após o pagamento, sua assinatura será renovada automaticamente por 30 dias.</p>
                </body></html>
                """);

        yield return TemplateNotificacao.Criar(
            codigo: "assinatura_expirada_dunning_email_v1",
            nome: "Dunning — Pagamento Pendente — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.AssinaturaExpirada,
            assuntoTemplate: "EasyStock — Pagamento pendente (aviso {{ numero_lembrete }}/3)",
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#dc2626">EasyStock — Pagamento pendente</h2>
                <p>Olá, <strong>{{ nome }}</strong>!</p>
                <p><strong>{{ urgencia }}</strong></p>
                <p>Regularize o pagamento de <strong>{{ valor }}</strong> via Pix para restaurar o acesso ao EasyStock:</p>
                {{ if qr_code != "" }}<img src="data:image/png;base64,{{ qr_code }}" alt="QR Code Pix" style="width:200px;height:200px" />{{ end }}
                <p style="margin-top:16px"><strong>Pix Copia e Cola:</strong></p>
                <pre style="background:#f3f4f6;padding:12px;border-radius:6px;word-break:break-all;font-size:12px">{{ pix_copia_cola }}</pre>
                <p style="color:#6b7280;font-size:12px">Após o pagamento seu acesso será restaurado automaticamente em minutos.</p>
                </body></html>
                """);

        yield return TemplateNotificacao.Criar(
            codigo: "alerta_estoque_critico_email_v1",
            nome: "Alerta de Estoque Crítico — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.AlertaEstoqueCritico,
            assuntoTemplate: "EasyStock — Alerta de estoque crítico: {{ produto_nome }}",
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#d97706">EasyStock — Alerta de estoque crítico</h2>
                <p>O produto <strong>{{ produto_nome }}</strong> está abaixo do nível mínimo.</p>
                <p>Estoque atual: <strong>{{ quantidade_atual }}</strong> | Mínimo: <strong>{{ quantidade_minima }}</strong></p>
                <p>Acesse o EasyStock para reabastecer o estoque.</p>
                </body></html>
                """);

        yield return TemplateNotificacao.Criar(
            codigo: "produto_vencendo_email_v1",
            nome: "Produto Vencendo — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ProdutoVencendo,
            assuntoTemplate: "EasyStock — Produto vencendo em {{ dias_restantes }} dia(s): {{ produto_nome }}",
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#d97706">EasyStock — Produto próximo ao vencimento</h2>
                <p>O produto <strong>{{ produto_nome }}</strong> vence em <strong>{{ dias_restantes }} dia(s)</strong>.</p>
                <p>Data de vencimento: <strong>{{ data_vencimento }}</strong> | Lote: <strong>{{ lote_numero }}</strong></p>
                <p>Quantidade disponível: <strong>{{ quantidade }}</strong></p>
                <p>Acesse o EasyStock para tomar uma ação antes que o produto expire.</p>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#0E2A6E">Sua solicitacao foi respondida</h2>
                <p>Recebemos uma resposta no seu ticket <strong>{{ titulo }}</strong>.</p>
                <p>Acesse o painel para visualizar a conversa completa.</p>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#dc2626">SLA violado</h2>
                <p>O ticket <strong>{{ titulo }}</strong> excedeu o prazo de <strong>{{ tipoSla }}</strong>.</p>
                <p>Empresa: {{ empresaNome }} | Prioridade: {{ prioridade }} | Nivel: {{ nivel }}</p>
                <p>Acesse o painel para tomar acao imediata.</p>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#0E2A6E">Bug encaminhado para desenvolvimento</h2>
                <p><strong>{{ titulo }}</strong></p>
                <p>Severidade: <strong>{{ severidade }}</strong> | Componente: <strong>{{ componente }}</strong></p>
                <p>Origem: ticket {{ ticketOrigemId }}.</p>
                <p>Acesse o painel para detalhes tecnicos.</p>
                </body></html>
                """);
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
