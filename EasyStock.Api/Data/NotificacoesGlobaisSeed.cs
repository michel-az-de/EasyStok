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

        // ===== Auth =====
        yield return TemplateNotificacao.Criar(
            codigo: "reset_senha_email_v1",
            nome: "Reset de Senha — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ResetSenha,
            assuntoTemplate: "EasyStok — Redefinicao de senha solicitada",
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto;background:#f8fafd;padding:20px">
                <div style="background:#0E2A6E;padding:20px;text-align:center;border-radius:8px 8px 0 0">
                  <h1 style="color:#fff;margin:0;font-size:24px">Easy<span style="color:#F26B25">Stok</span></h1>
                </div>
                <div style="background:#fff;padding:24px;border-radius:0 0 8px 8px">
                  <h2 style="color:#0E2A6E">Redefinicao de senha</h2>
                  <p>Ola, <strong>{{ nome }}</strong>!</p>
                  <p>Clique no botao abaixo para criar uma nova senha:</p>
                  <p style="text-align:center;margin:24px 0">
                    <a href="{{ link_redefinicao }}" style="display:inline-block;background:#E85814;color:#fff;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:600">Redefinir senha</a>
                  </p>
                  <p style="color:#6b7280;font-size:12px">O link expira em {{ expira_em_minutos }} minutos. Se voce nao solicitou, ignore este email.</p>
                </div>
                </body></html>
                """);

        yield return TemplateNotificacao.Criar(
            codigo: "confirmacao_email_email_v1",
            nome: "Confirmacao de Email — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ConfirmacaoEmail,
            assuntoTemplate: "EasyStok — Confirme seu email",
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto;background:#f8fafd;padding:20px">
                <div style="background:#0E2A6E;padding:20px;text-align:center;border-radius:8px 8px 0 0">
                  <h1 style="color:#fff;margin:0;font-size:24px">Easy<span style="color:#F26B25">Stok</span></h1>
                </div>
                <div style="background:#fff;padding:24px;border-radius:0 0 8px 8px">
                  <h2 style="color:#0E2A6E">Confirme seu email</h2>
                  <p>Ola, <strong>{{ nome }}</strong>!</p>
                  <p>Falta pouco — confirme seu email para ativar sua conta:</p>
                  <p style="text-align:center;margin:24px 0">
                    <a href="{{ link_confirmacao }}" style="display:inline-block;background:#E85814;color:#fff;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:600">Confirmar email</a>
                  </p>
                  <p style="color:#6b7280;font-size:12px">O link expira em {{ expira_em_horas }} horas.</p>
                </div>
                </body></html>
                """);

        // ===== Operacional faltante =====
        yield return TemplateNotificacao.Criar(
            codigo: "produto_vencido_email_v1",
            nome: "Produto Vencido — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ProdutoVencido,
            assuntoTemplate: "EasyStok — Produto VENCIDO: {{ produto_nome }}",
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#C03B2A">EasyStok — Produto vencido</h2>
                <p>O produto <strong>{{ produto_nome }}</strong> venceu em <strong>{{ data_vencimento }}</strong>.</p>
                <p>Lote: <strong>{{ lote_numero }}</strong> | Quantidade afetada: <strong>{{ quantidade }}</strong></p>
                <p>Acesse o EasyStok para registrar a perda.</p>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#0E2A6E">Novo ticket aberto</h2>
                <p>A empresa <strong>{{ empresaNome }}</strong> abriu o ticket <strong>{{ titulo }}</strong>.</p>
                <p>Prioridade: <strong>{{ prioridade }}</strong> | Nivel: <strong>{{ nivel }}</strong></p>
                <p>Acesse o painel para responder.</p>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#4f46e5">Seu relatorio esta pronto</h2>
                <p>Ola, <strong>{{ primeiroNome }}</strong>.</p>
                <p>O relatorio <strong>{{ reportLabel }}</strong> foi gerado com sucesso.</p>
                <p>Formato: {{ format }} | Linhas: {{ rowCount }}</p>
                <p>Acesse o EasyStock para baixar o arquivo.</p>
                <p style="color:#6b7280;font-size:12px">O arquivo fica disponivel por 30 dias.</p>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#dc2626">Nao conseguimos gerar o relatorio</h2>
                <p>Ola, <strong>{{ primeiroNome }}</strong>.</p>
                <p>Tentamos gerar o relatorio <strong>{{ reportLabel }}</strong>, mas nao foi possivel.</p>
                <p>{{ errorMensagem }}</p>
                <p>Acesse o EasyStock para tentar novamente.</p>
                <p style="color:#6b7280;font-size:12px">Codigo: {{ runId }}</p>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#0E2A6E">Ticket atribuido a voce</h2>
                <p>Voce foi designado para atender o ticket <strong>{{ titulo }}</strong>.</p>
                </body></html>
                """);

        yield return TemplateNotificacao.Criar(
            codigo: "sla_proximo_vencer_email_v1",
            nome: "SLA proximo de vencer — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.SlaProximoVencer,
            assuntoTemplate: "EasyStok — SLA proximo de vencer: {{ titulo }}",
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#B57A00">SLA proximo de vencer</h2>
                <p>Ticket <strong>{{ titulo }}</strong> esta a <strong>{{ percentual }}%</strong> do prazo de <strong>{{ tipoSla }}</strong>.</p>
                <p>Tempo restante: <strong>{{ minutosRestantes }} minutos</strong>.</p>
                </body></html>
                """);

        yield return TemplateNotificacao.Criar(
            codigo: "convite_csat_email_v1",
            nome: "Convite CSAT — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.ConviteCsat,
            assuntoTemplate: "EasyStok — Como avaliaria o atendimento?",
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto;background:#f8fafd;padding:20px">
                <div style="background:#0E2A6E;padding:20px;text-align:center;border-radius:8px 8px 0 0">
                  <h1 style="color:#fff;margin:0;font-size:24px">Easy<span style="color:#F26B25">Stok</span></h1>
                </div>
                <div style="background:#fff;padding:24px;border-radius:0 0 8px 8px">
                  <h2 style="color:#0E2A6E">Como avaliaria o atendimento?</h2>
                  <p>Ola, <strong>{{ nome }}</strong>!</p>
                  <p>Acabamos de fechar seu ticket <strong>{{ titulo_ticket }}</strong>, atendido por {{ atendenteNome }}.</p>
                  <p style="text-align:center;margin:24px 0">
                    <a href="{{ link_pesquisa }}" style="display:inline-block;background:#E85814;color:#fff;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:600">Avaliar atendimento</a>
                  </p>
                </div>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#0E2A6E">Nova fatura disponivel</h2>
                <p>Ola, <strong>{{ nome }}</strong>!</p>
                <p>Sua fatura <strong>{{ numero_fatura }}</strong> no valor de <strong>{{ valor }}</strong> esta disponivel.</p>
                <p>Vencimento: <strong>{{ vencimento }}</strong>.</p>
                <p style="text-align:center;margin:24px 0">
                  <a href="{{ link_pagamento }}" style="display:inline-block;background:#E85814;color:#fff;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:600">Pagar fatura</a>
                </p>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#B57A00">Sua fatura vence em breve</h2>
                <p>Ola, <strong>{{ nome }}</strong>!</p>
                <p>Fatura <strong>{{ numero_fatura }}</strong> de <strong>{{ valor }}</strong> vence em <strong>{{ dias_restantes }} dia(s)</strong> ({{ vencimento }}).</p>
                <p style="text-align:center;margin:24px 0">
                  <a href="{{ link_pagamento }}" style="display:inline-block;background:#E85814;color:#fff;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:600">Pagar agora</a>
                </p>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#18874E">Pagamento confirmado</h2>
                <p>Ola, <strong>{{ nome }}</strong>!</p>
                <p>Recebemos seu pagamento da fatura <strong>{{ numero_fatura }}</strong>.</p>
                <p>Valor: <strong>{{ valor }}</strong> em {{ data_pagamento }}.</p>
                <p style="text-align:center;margin:24px 0">
                  <a href="{{ link_recibo }}" style="display:inline-block;background:#0E2A6E;color:#fff;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:600">Ver recibo</a>
                </p>
                </body></html>
                """);

        yield return TemplateNotificacao.Criar(
            codigo: "fatura_vencida_email_v1",
            nome: "Fatura Vencida — Email",
            canal: CanalNotificacao.Email,
            tipoEvento: TipoEventoNotificacao.FaturaVencida,
            assuntoTemplate: "EasyStok — Fatura {{ numero_fatura }} em atraso",
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#C03B2A">Fatura em atraso</h2>
                <p>Ola, <strong>{{ nome }}</strong>!</p>
                <p>Fatura <strong>{{ numero_fatura }}</strong> de <strong>{{ valor }}</strong> esta em atraso ha <strong>{{ dias_em_atraso }} dia(s)</strong>.</p>
                <p style="text-align:center;margin:24px 0">
                  <a href="{{ link_pagamento }}" style="display:inline-block;background:#E85814;color:#fff;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:600">Regularizar agora</a>
                </p>
                </body></html>
                """);

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
            corpoTemplate: """
                <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
                <h2 style="color:#C03B2A">Pagamento nao processado</h2>
                <p>Ola, <strong>{{ nome }}</strong>!</p>
                <p>Nao conseguimos processar o pagamento de <strong>{{ valor }}</strong> via {{ metodo }}.</p>
                <p>Motivo: <em>{{ motivo }}</em></p>
                <p style="text-align:center;margin:24px 0">
                  <a href="{{ link_retry }}" style="display:inline-block;background:#E85814;color:#fff;padding:12px 28px;border-radius:6px;text-decoration:none;font-weight:600">Tentar novamente</a>
                </p>
                </body></html>
                """);

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
