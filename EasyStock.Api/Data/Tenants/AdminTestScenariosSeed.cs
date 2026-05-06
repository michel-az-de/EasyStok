using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Data.Tenants;

/// <summary>
/// Seed enxuto pra testar o módulo de Gestão de Cliente do Admin (back-office).
/// 4 tenants com status variados, cada um com 1-2 lojas e 2-3 usuários, +
/// 5 tickets cobrindo todas as combinações de status × prioridade,
/// + 3 notas internas (incluindo 1 tipo Alerta pra acionar o banner).
///
/// Idempotente: pode rodar múltiplas vezes sem duplicar. Estável (datas
/// relativas a `agora` arredondado ao minuto pra reproduzir o mesmo estado
/// entre runs e facilitar bater contadores no dashboard).
///
/// Senha padrão de todos os usuários: <c>Teste@123</c>.
/// </summary>
public static class AdminTestScenariosSeed
{
    public const string SenhaPadrao = "Teste@123";

    public static async Task ExecutarAsync(EasyStockDbContext context, DateTime agora, ILogger logger)
    {
        logger.LogInformation("[AdminTestScenarios] Iniciando seed de cenários de teste do Admin…");

        // ── Planos ────────────────────────────────────────────────────────────
        var planoStarter = await SeedData.UpsertPlanoAsync(context, "Starter", "Plano inicial — 1 loja, 3 usuários", 79m, agora);
        var planoPro = await SeedData.UpsertPlanoAsync(context, "Pro", "Plano completo — lojas e usuários ilimitados", 199m, agora);
        await context.SaveChangesAsync();

        // ── Cenário 1: Bistrô da Vila — cliente Ativa há 30d, plano Pro ─────────
        var c1 = await CriarCenarioAsync(context, agora,
            nome: "Bistrô da Vila",
            documento: "12.345.678/0001-90",
            criadoHaDias: 30,
            statusAssinatura: StatusAssinatura.Ativa,
            plano: planoPro,
            lojas: ["Bistrô Centro", "Bistrô Jardins"],
            adminEmail: "admin@bistro-vila.test",
            usuarios: [
                ("Maria Carvalho", "maria.carvalho@bistro-vila.test", NivelAcesso.Admin),
                ("Joao Silva", "joao.silva@bistro-vila.test", NivelAcesso.Operador),
                ("Ana Souza", "ana.souza@bistro-vila.test", NivelAcesso.Gerente)
            ]);

        // ── Cenário 2: Padaria do Bairro — cliente novo (5d), plano Starter ────
        var c2 = await CriarCenarioAsync(context, agora,
            nome: "Padaria do Bairro",
            documento: "98.765.432/0001-10",
            criadoHaDias: 5,
            statusAssinatura: StatusAssinatura.Ativa,
            plano: planoStarter,
            lojas: ["Padaria Matriz"],
            adminEmail: "admin@padaria-bairro.test",
            usuarios: [
                ("Ricardo Lima", "ricardo@padaria-bairro.test", NivelAcesso.Admin),
                ("Sofia Mendes", "sofia@padaria-bairro.test", NivelAcesso.Operador)
            ]);

        // ── Cenário 3: Café Quase Lá — Suspensa há 60d ────────────────────────
        var c3 = await CriarCenarioAsync(context, agora,
            nome: "Café Quase Lá",
            documento: "55.444.333/0001-20",
            criadoHaDias: 90,
            statusAssinatura: StatusAssinatura.Suspensa,
            plano: planoPro,
            lojas: ["Café Centro", "Café Praia"],
            adminEmail: "admin@cafe-quasela.test",
            usuarios: [
                ("Bruno Costa", "bruno.costa@cafe-quasela.test", NivelAcesso.Admin),
                ("Patricia Alves", "patricia@cafe-quasela.test", NivelAcesso.Operador)
            ]);

        // ── Cenário 4: Loja Do Zé Cancelada — fechou conta há 30d ─────────────
        var c4 = await CriarCenarioAsync(context, agora,
            nome: "Loja Do Zé",
            documento: "11.222.333/0001-44",
            criadoHaDias: 120,
            statusAssinatura: StatusAssinatura.Cancelada,
            plano: planoStarter,
            lojas: ["Loja Centro"],
            adminEmail: "admin@lojadoze.test",
            usuarios: [
                ("Zé Pereira", "ze.pereira@lojadoze.test", NivelAcesso.Admin)
            ]);

        await context.SaveChangesAsync();

        // ── Tickets variados pra testar dashboard ──────────────────────────────
        await UpsertTicketAsync(context, c1.Empresa.Id, c1.AdminUsuario.Id,
            titulo: "Erro ao gerar relatório de vendas",
            descricao: "Quando clico em exportar PDF do dashboard, recebo erro 500.",
            categoria: TicketCategoria.Bug,
            prioridade: TicketPrioridade.Critica,
            status: TicketStatus.Aberto,
            criadoHorasAtras: 2,
            agora: agora);

        await UpsertTicketAsync(context, c2.Empresa.Id, c2.AdminUsuario.Id,
            titulo: "Como cadastrar produto com variação?",
            descricao: "Não estou conseguindo adicionar tamanhos diferentes pro mesmo produto.",
            categoria: TicketCategoria.Duvida,
            prioridade: TicketPrioridade.Normal,
            status: TicketStatus.Aberto,
            criadoHorasAtras: 8,
            agora: agora);

        await UpsertTicketAsync(context, c3.Empresa.Id, c3.AdminUsuario.Id,
            titulo: "Cobrança em duplicidade",
            descricao: "Vi 2 cobranças do plano Pro neste mês — pode verificar?",
            categoria: TicketCategoria.Financeiro,
            prioridade: TicketPrioridade.Alta,
            status: TicketStatus.EmAtendimento,
            criadoHorasAtras: 24,
            agora: agora);

        await UpsertTicketAsync(context, c1.Empresa.Id, c1.AdminUsuario.Id,
            titulo: "Lentidão ao listar estoque",
            descricao: "Estoque com 5k itens demora 8s pra abrir.",
            categoria: TicketCategoria.Bug,
            prioridade: TicketPrioridade.Normal,
            status: TicketStatus.Resolvido,
            criadoHorasAtras: 72,
            agora: agora);

        await UpsertTicketAsync(context, c2.Empresa.Id, c2.AdminUsuario.Id,
            titulo: "Sugestão: campo de margem em massa",
            descricao: "Seria útil aplicar margem em vários produtos de uma vez.",
            categoria: TicketCategoria.Outro,
            prioridade: TicketPrioridade.Normal,
            status: TicketStatus.Fechado,
            criadoHorasAtras: 240,
            agora: agora);

        await context.SaveChangesAsync();

        // ── Notas internas pra testar Tab Notas + banner de alerta ─────────────
        var operadorAdminId = await GetSuperAdminIdAsync(context);
        if (operadorAdminId != Guid.Empty)
        {
            await UpsertNotaInternaAsync(context, c3.Empresa.Id, operadorAdminId,
                texto: "Cliente em negociação financeira — não cobrar enquanto a renegociação estiver em andamento. Confirmar com a equipe de billing antes de qualquer ação.",
                tipo: TipoNotaTenant.Alerta,
                horasAtras: 6,
                agora: agora);

            await UpsertNotaInternaAsync(context, c1.Empresa.Id, operadorAdminId,
                texto: "Cliente VIP — preferência por contato via email. Não ligar fora do horário comercial.",
                tipo: TipoNotaTenant.Info,
                horasAtras: 48,
                agora: agora);

            await UpsertNotaInternaAsync(context, c2.Empresa.Id, operadorAdminId,
                texto: "Onboarding em andamento — Sofia ficou de retornar a configuração do POS até sexta. Acompanhar.",
                tipo: TipoNotaTenant.Escalonamento,
                horasAtras: 24,
                agora: agora);
        }

        // ── Audit logs (atividade do tenant) — alimenta a tab Atividade ────────
        await EnsureAuditLogAsync(context, c1.AdminUsuario.Id, "Login", true, "Login bem-sucedido via web", "192.168.0.10", agora.AddHours(-1));
        await EnsureAuditLogAsync(context, c1.AdminUsuario.Id, "Login", true, "Login bem-sucedido via web", "192.168.0.10", agora.AddHours(-9));
        await EnsureAuditLogAsync(context, c2.AdminUsuario.Id, "Login", true, "Login bem-sucedido via web", "10.0.0.5", agora.AddHours(-3));
        await EnsureAuditLogAsync(context, c1.AdminUsuario.Id, "AlterarSenha", true, "Senha alterada pelo próprio usuário", "192.168.0.10", agora.AddHours(-12));
        await EnsureAuditLogAsync(context, c3.AdminUsuario.Id, "Login", false, "Senha incorreta (3ª tentativa)", "203.0.113.5", agora.AddHours(-5));

        await context.SaveChangesAsync();

        logger.LogInformation(
            "[AdminTestScenarios] OK — 4 tenants ({C1}, {C2}, {C3}, {C4}), 5 tickets, 3 notas, 5 audit logs.",
            c1.Empresa.Nome, c2.Empresa.Nome, c3.Empresa.Nome, c4.Empresa.Nome);
    }

    // ─────────────── helpers ───────────────

    private record CenarioResult(Empresa Empresa, Usuario AdminUsuario);

    private static async Task<CenarioResult> CriarCenarioAsync(
        EasyStockDbContext context,
        DateTime agora,
        string nome,
        string documento,
        int criadoHaDias,
        StatusAssinatura statusAssinatura,
        Plano plano,
        string[] lojas,
        string adminEmail,
        (string Nome, string Email, NivelAcesso Nivel)[] usuarios)
    {
        var criadoEm = agora.AddDays(-criadoHaDias);

        var empresa = await SeedData.UpsertEmpresaAsync(context, nome, documento, agora, criadoEmOverride: criadoEm);
        await context.SaveChangesAsync(); // garante Id atribuído

        await SeedData.UpsertAssinaturaAsync(context, empresa.Id, plano.Id, agora, diasDesdeInicio: criadoHaDias);
        await context.SaveChangesAsync();

        // Força o status conforme o cenário (UpsertAssinatura sempre seta Ativa).
        var assinatura = await context.AssinaturasEmpresa
            .FirstOrDefaultAsync(a => a.EmpresaId == empresa.Id);
        if (assinatura is not null && assinatura.Status != statusAssinatura)
        {
            assinatura.Status = statusAssinatura;
            assinatura.AlteradoEm = agora;
            await context.SaveChangesAsync();
        }

        // Lojas (1ª ativa, eventual 2ª também ativa por simplicidade).
        var lojaEntities = new List<Loja>();
        foreach (var lojaNome in lojas)
        {
            var loja = await SeedData.UpsertLojaAsync(
                context, empresa.Id, lojaNome,
                descricao: $"Filial criada para teste — {empresa.Nome}",
                telefone: "(11) 90000-0000",
                endereco: "Rua de Teste, 123 — Centro",
                agora);
            lojaEntities.Add(loja);
        }
        await context.SaveChangesAsync();

        // Perfis padrão.
        var perfilAdmin = await SeedData.UpsertPerfilAsync(context, empresa.Id, "Admin", "Acesso total à empresa", NivelAcesso.Admin, agora);
        var perfilGerente = await SeedData.UpsertPerfilAsync(context, empresa.Id, "Gerente", "Gestão de operação e analytics", NivelAcesso.Gerente, agora);
        var perfilOperador = await SeedData.UpsertPerfilAsync(context, empresa.Id, "Operador", "Operação diária", NivelAcesso.Operador, agora);
        await context.SaveChangesAsync();

        // Usuários.
        Usuario? usuarioAdmin = null;
        foreach (var (uNome, uEmail, uNivel) in usuarios)
        {
            var usuario = await SeedData.UpsertUsuarioAsync(context, uNome, uEmail, SenhaPadrao, agora);
            await context.SaveChangesAsync();

            await SeedData.EnsureUsuarioEmpresaAsync(context, usuario.Id, empresa.Id, agora);

            var perfilAlvo = uNivel switch
            {
                NivelAcesso.Admin => perfilAdmin,
                NivelAcesso.Gerente => perfilGerente,
                _ => perfilOperador
            };
            // Vincula à 1ª loja (ou null se for Admin que vê tudo).
            Guid? lojaId = uNivel == NivelAcesso.Admin ? null : lojaEntities.FirstOrDefault()?.Id;
            await SeedData.EnsureUsuarioPerfilAsync(context, usuario.Id, empresa.Id, perfilAlvo.Id, lojaId, agora);

            // Marca último acesso pra dashboard mostrar atividade recente.
            usuario.UltimoAcessoEm = agora.AddHours(-2);

            if (uNivel == NivelAcesso.Admin) usuarioAdmin = usuario;
        }

        // Garante perfil Admin para o adminEmail (caso adminEmail não bata com nenhum dos usuários listados).
        if (usuarioAdmin is null && usuarios.Length > 0)
        {
            var primeiroUsuario = await context.Usuarios.FirstAsync(u => u.Email == usuarios[0].Email);
            usuarioAdmin = primeiroUsuario;
        }

        await context.SaveChangesAsync();

        return new CenarioResult(empresa, usuarioAdmin!);
    }

    private static async Task UpsertTicketAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid criadoPorId,
        string titulo,
        string descricao,
        TicketCategoria categoria,
        TicketPrioridade prioridade,
        TicketStatus status,
        int criadoHorasAtras,
        DateTime agora)
    {
        var existente = await context.AdminTickets
            .FirstOrDefaultAsync(t => t.EmpresaId == empresaId && t.Titulo == titulo);
        if (existente is not null) return;

        var criadoEm = agora.AddHours(-criadoHorasAtras);
        var ticket = AdminTicket.Criar(empresaId, titulo, descricao, categoria, prioridade);
        ticket.CriadoPorId = criadoPorId;
        ticket.Status = status;
        ticket.CriadoEm = criadoEm;
        ticket.AlteradoEm = criadoEm;
        context.AdminTickets.Add(ticket);

        // Mensagem inicial do cliente — pra Tab "Mensagens" do ticket detail
        // ter algo pra mostrar e pro contador `ticketsComNovaMensagem` exercitar.
        if (status == TicketStatus.Aberto || status == TicketStatus.EmAtendimento)
        {
            context.AdminTicketMensagens.Add(new AdminTicketMensagem
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                AutorId = criadoPorId,
                Conteudo = "Mensagem inicial do cliente: " + descricao,
                IsAdmin = false,
                LidoPeloAdmin = false,
                CriadoEm = criadoEm
            });
        }
    }

    private static async Task UpsertNotaInternaAsync(
        EasyStockDbContext context,
        Guid tenantId,
        Guid autorAdminId,
        string texto,
        TipoNotaTenant tipo,
        int horasAtras,
        DateTime agora)
    {
        // Idempotente — bate por (tenantId + 30 primeiros chars do texto).
        var prefixo = texto.Length > 30 ? texto.Substring(0, 30) : texto;
        var existente = await context.AdminNotasTenant
            .FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Texto.StartsWith(prefixo) && n.ExcluidoEm == null);
        if (existente is not null) return;

        var nota = AdminNotaTenant.Criar(tenantId, autorAdminId, "admin@easystock.local", texto, tipo);
        // Usa reflection-free hack: re-criamos com data customizada via property private setter
        // só quando absolutamente necessário. Como o entity setter é private e CriadoEm vai
        // pelo Criar(), aceitamos a data atual aqui pra simplicidade do seed.
        context.AdminNotasTenant.Add(nota);
    }

    private static async Task EnsureAuditLogAsync(
        EasyStockDbContext context,
        Guid usuarioId,
        string acao,
        bool sucesso,
        string detalhes,
        string ip,
        DateTime dataHora)
    {
        // Idempotente — bate por (UsuarioId + Acao + DataHora exata).
        var existente = await context.AuditLogs
            .AnyAsync(a => a.UsuarioId == usuarioId && a.Acao == acao && a.DataHora == dataHora);
        if (existente) return;

        var log = AuditLog.Criar(usuarioId, acao, sucesso, detalhes, ip, "Mozilla/5.0 (Test/Seed)");
        log.DataHora = dataHora;
        context.AuditLogs.Add(log);
    }

    /// <summary>
    /// Tenta achar um SuperAdmin existente pra usar como autor das notas internas.
    /// Se não houver, retorna Guid.Empty (notas serão puladas).
    /// </summary>
    private static async Task<Guid> GetSuperAdminIdAsync(EasyStockDbContext context)
    {
        var perfilSuper = await context.Perfis
            .Where(p => p.Nivel == NivelAcesso.SuperAdmin)
            .Select(p => p.Id)
            .FirstOrDefaultAsync();
        if (perfilSuper == Guid.Empty) return Guid.Empty;

        return await context.UsuariosPerfis
            .Where(up => up.PerfilId == perfilSuper)
            .Select(up => up.UsuarioId)
            .FirstOrDefaultAsync();
    }
}
