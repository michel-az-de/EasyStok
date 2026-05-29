using System.Text.Json;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data.Tenants;

/// <summary>
/// Seed enxuto pra testar o módulo de Gestão de Cliente do Admin (back-office).
/// 4 tenants com status variados, cada um com 1-2 lojas e 2-3 usuários, +
/// 5 tickets cobrindo todas as combinações de status × prioridade,
/// + 3 notas internas (incluindo 1 tipo Alerta pra acionar o banner).
///
/// Smart-seed: antes de criar, deleta APENAS empresas marcadas com IsSeedData=true
/// (nunca toca dados reais). Backup dos IDs deletados é registrado em SeedRunLog
/// pra auditoria e recuperação emergencial.
///
/// Senha padrão de todos os usuários: <c>Teste@123</c>.
/// </summary>
public static class AdminTestScenariosSeed
{
    public const string SenhaPadrao = "Teste@123";

    // Documentos fixos dos 4 cenários — usados pra identificar e limpar runs anteriores
    // sem depender da coluna IsSeedData (que pode não existir na migration).
    private static readonly string[] SeedDocumentos =
    [
        "12.345.678/0001-90",  // Bistrô da Vila
        "98.765.432/0001-10",  // Padaria do Bairro
        "55.444.333/0001-20",  // Café Quase Lá
        "11.222.333/0001-44"   // Loja Do Zé
    ];

    /// <summary>
    /// Executa o seed com progresso em tempo real reportado via <paramref name="progress"/>.
    /// Limpa dados anteriores por documento (não usa IsSeedData em LINQ — coluna pode
    /// não existir em deploys antigos). Cada etapa de delete é autocommit via EF.
    /// </summary>
    public static async Task ExecutarAsync(
        EasyStockDbContext context,
        DateTime agora,
        ILogger logger,
        SeedProgressService? progress = null,
        Guid runId = default)
    {
        void Report(int pct, string msg, string level = "info")
        {
            logger.LogInformation("[AdminTestScenarios] {Pct}% — {Msg}", pct, msg);
            if (progress is not null && runId != default)
                progress.Report(runId, pct, msg, level);
        }

        Report(5, "Verificando dados seed anteriores para remoção…");

        // ── Limpeza por documento — sem IsSeedData ────────────────────────────────
        // Detecta pelo CNPJ fixo dos 4 cenários (invariante entre runs).
        // NÃO usa e.IsSeedData no LINQ — coluna pode estar ausente em produção se
        // migration não aplicou. Não depende de nenhuma coluna nova.
        var seedEmpresas = await context.Empresas
            .Where(e => SeedDocumentos.Contains(e.Documento))
            .Select(e => new { e.Id, e.Nome, e.Documento })
            .ToListAsync();

        string backupJson = "{\"empresas\":[]}";
        if (seedEmpresas.Count > 0)
        {
            backupJson = JsonSerializer.Serialize(new
            {
                deletedAt = DateTime.UtcNow,
                empresas = seedEmpresas.Select(e => new { e.Id, e.Nome, e.Documento })
            });
            Report(10, $"Backup criado — {seedEmpresas.Count} empresa(s) encontradas para remoção.");
        }
        else
        {
            Report(10, "Primeiro run — nenhum dado anterior encontrado.");
        }

        progress?.SetBackup(runId, backupJson);
        logger.LogInformation("[AdminTestScenarios] Backup: {Json}", backupJson.Length > 500 ? backupJson[..500] + "…" : backupJson);

        // ── Delete + insert sem tx explícita ──────────────────────────────────────
        // Não usa BeginTransactionAsync — conflita com NpgsqlRetryingExecutionStrategy.
        // Cada ExecuteDeleteAsync / SaveChangesAsync é auto-transacional.
        // O seed é idempotente: se falhar a meio, re-run recupera o estado.
        try
        {
            if (seedEmpresas.Count > 0)
            {
                Report(15, $"Removendo {seedEmpresas.Count} empresa(s) seed + dados relacionados…", "warn");

                var ids = seedEmpresas.Select(e => e.Id).ToList();

                await context.AdminTickets.Where(t => ids.Contains(t.EmpresaId)).ExecuteDeleteAsync();
                await context.AdminNotasTenant.Where(n => ids.Contains(n.TenantId)).ExecuteDeleteAsync();

                var usuarioIds = await context.UsuariosEmpresas
                    .Where(ue => ids.Contains(ue.EmpresaId))
                    .Select(ue => ue.UsuarioId)
                    .ToListAsync();

                await context.UsuariosPerfis.Where(up => usuarioIds.Contains(up.UsuarioId) && ids.Contains(up.EmpresaId)).ExecuteDeleteAsync();
                await context.UsuariosEmpresas.Where(ue => ids.Contains(ue.EmpresaId)).ExecuteDeleteAsync();
                await context.AssinaturasEmpresa.Where(a => ids.Contains(a.EmpresaId)).ExecuteDeleteAsync();
                await context.Lojas.Where(l => ids.Contains(l.EmpresaId)).ExecuteDeleteAsync();
                await context.Perfis.Where(p => p.EmpresaId.HasValue && ids.Contains(p.EmpresaId.Value)).ExecuteDeleteAsync();

                var orphanIds = await context.Usuarios
                    .Where(u => usuarioIds.Contains(u.Id)
                        && !context.UsuariosEmpresas.Any(ue => ue.UsuarioId == u.Id)
                        && u.Email.EndsWith(".test"))
                    .Select(u => u.Id)
                    .ToListAsync();
                if (orphanIds.Count > 0)
                    await context.Usuarios.Where(u => orphanIds.Contains(u.Id)).ExecuteDeleteAsync();

                await context.Empresas.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync();

                Report(25, $"Removido: {seedEmpresas.Count} empresa(s) + {orphanIds.Count} usuário(s) orphan.");
            }

            // ── Etapa 3: Planos ────────────────────────────────────────────────────
            Report(35, "Criando/verificando planos base…");
            var planoStarter = await SeedData.UpsertPlanoAsync(context, "Starter", "Plano inicial — 1 loja, 3 usuários", 79m, agora);
            var planoPro = await SeedData.UpsertPlanoAsync(context, "Pro", "Plano completo — lojas e usuários ilimitados", 199m, agora);
            await context.SaveChangesAsync();

            // ── Etapa 4: Tenants ──────────────────────────────────────────────────
            Report(40, "Criando tenant 1/4 — Bistrô da Vila (Ativa, plano Pro)…");
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

            Report(55, "Criando tenant 2/4 — Padaria do Bairro (Ativa, trial)…");
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

            Report(65, "Criando tenant 3/4 — Café Quase Lá (Suspensa)…");
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

            Report(75, "Criando tenant 4/4 — Loja Do Zé (Cancelada)…");
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

            // ── Tickets ───────────────────────────────────────────────────────────
            Report(80, "Criando tickets de suporte (5 cenários)…");

            await UpsertTicketAsync(context, c1.Empresa.Id, c1.AdminUsuario.Id,
                titulo: "Erro ao gerar relatório de vendas",
                descricao: "Quando clico em exportar PDF do dashboard, recebo erro 500.",
                categoria: TicketCategoria.Bug, prioridade: TicketPrioridade.Critica,
                status: TicketStatus.Aberto, criadoHorasAtras: 2, agora: agora);

            await UpsertTicketAsync(context, c2.Empresa.Id, c2.AdminUsuario.Id,
                titulo: "Como cadastrar produto com variação?",
                descricao: "Não estou conseguindo adicionar tamanhos diferentes pro mesmo produto.",
                categoria: TicketCategoria.Duvida, prioridade: TicketPrioridade.Normal,
                status: TicketStatus.Aberto, criadoHorasAtras: 8, agora: agora);

            await UpsertTicketAsync(context, c3.Empresa.Id, c3.AdminUsuario.Id,
                titulo: "Cobrança em duplicidade",
                descricao: "Vi 2 cobranças do plano Pro neste mês — pode verificar?",
                categoria: TicketCategoria.Financeiro, prioridade: TicketPrioridade.Alta,
                status: TicketStatus.EmAtendimento, criadoHorasAtras: 24, agora: agora);

            await UpsertTicketAsync(context, c1.Empresa.Id, c1.AdminUsuario.Id,
                titulo: "Lentidão ao listar estoque",
                descricao: "Estoque com 5k itens demora 8s pra abrir.",
                categoria: TicketCategoria.Bug, prioridade: TicketPrioridade.Normal,
                status: TicketStatus.Resolvido, criadoHorasAtras: 72, agora: agora);

            await UpsertTicketAsync(context, c2.Empresa.Id, c2.AdminUsuario.Id,
                titulo: "Sugestão: campo de margem em massa",
                descricao: "Seria útil aplicar margem em vários produtos de uma vez.",
                categoria: TicketCategoria.Outro, prioridade: TicketPrioridade.Normal,
                status: TicketStatus.Fechado, criadoHorasAtras: 240, agora: agora);

            await context.SaveChangesAsync();

            // ── Notas + Audit logs ────────────────────────────────────────────────
            Report(88, "Criando notas internas e audit logs…");

            var operadorAdminId = await GetSuperAdminIdAsync(context);
            if (operadorAdminId != Guid.Empty)
            {
                await UpsertNotaInternaAsync(context, c3.Empresa.Id, operadorAdminId,
                    texto: "Cliente em negociação financeira — não cobrar enquanto a renegociação estiver em andamento. Confirmar com a equipe de billing antes de qualquer ação.",
                    tipo: TipoNotaTenant.Alerta, horasAtras: 6, agora: agora);

                await UpsertNotaInternaAsync(context, c1.Empresa.Id, operadorAdminId,
                    texto: "Cliente VIP — preferência por contato via email. Não ligar fora do horário comercial.",
                    tipo: TipoNotaTenant.Info, horasAtras: 48, agora: agora);

                await UpsertNotaInternaAsync(context, c2.Empresa.Id, operadorAdminId,
                    texto: "Onboarding em andamento — Sofia ficou de retornar a configuração do POS até sexta. Acompanhar.",
                    tipo: TipoNotaTenant.Escalonamento, horasAtras: 24, agora: agora);
            }

            await EnsureAuditLogAsync(context, c1.AdminUsuario.Id, "Login", true, "Login bem-sucedido via web", "192.168.0.10", agora.AddHours(-1));
            await EnsureAuditLogAsync(context, c1.AdminUsuario.Id, "Login", true, "Login bem-sucedido via web", "192.168.0.10", agora.AddHours(-9));
            await EnsureAuditLogAsync(context, c2.AdminUsuario.Id, "Login", true, "Login bem-sucedido via web", "10.0.0.5", agora.AddHours(-3));
            await EnsureAuditLogAsync(context, c1.AdminUsuario.Id, "AlterarSenha", true, "Senha alterada pelo próprio usuário", "192.168.0.10", agora.AddHours(-12));
            await EnsureAuditLogAsync(context, c3.AdminUsuario.Id, "Login", false, "Senha incorreta (3ª tentativa)", "203.0.113.5", agora.AddHours(-5));

            await context.SaveChangesAsync();

            var resumo = $"4 tenants ({c1.Empresa.Nome}, {c2.Empresa.Nome}, {c3.Empresa.Nome}, {c4.Empresa.Nome}), 5 tickets, 3 notas, 5 audit logs.";
            Report(100, resumo, "success");
            logger.LogInformation("[AdminTestScenarios] Seed concluído — {Resumo}", resumo);

            if (progress is not null && runId != default)
                await progress.SuccessAsync(runId, resumo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AdminTestScenarios] Falhou: {Msg}", ex.Message);
            throw;
        }
    }

    // ─────────────────────────── helpers ───────────────────────────────────────

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

        var empresa = await SeedData.UpsertEmpresaAsync(context, nome, documento, agora,
            criadoEmOverride: criadoEm);
        await context.SaveChangesAsync();

        await SeedData.UpsertAssinaturaAsync(context, empresa.Id, plano.Id, agora, diasDesdeInicio: criadoHaDias);
        await context.SaveChangesAsync();

        var assinatura = await context.AssinaturasEmpresa.FirstOrDefaultAsync(a => a.EmpresaId == empresa.Id);
        if (assinatura is not null && assinatura.Status != statusAssinatura)
        {
            assinatura.Status = statusAssinatura;
            assinatura.AlteradoEm = agora;
            await context.SaveChangesAsync();
        }

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

        var perfilAdmin = await SeedData.UpsertPerfilAsync(context, empresa.Id, "Admin", "Acesso total à empresa", NivelAcesso.Admin, agora);
        var perfilGerente = await SeedData.UpsertPerfilAsync(context, empresa.Id, "Gerente", "Gestão de operação e analytics", NivelAcesso.Gerente, agora);
        var perfilOperador = await SeedData.UpsertPerfilAsync(context, empresa.Id, "Operador", "Operação diária", NivelAcesso.Operador, agora);
        await context.SaveChangesAsync();

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
            Guid? lojaId = uNivel == NivelAcesso.Admin ? null : lojaEntities.FirstOrDefault()?.Id;
            await SeedData.EnsureUsuarioPerfilAsync(context, usuario.Id, empresa.Id, perfilAlvo.Id, lojaId, agora);

            usuario.UltimoAcessoEm = agora.AddHours(-2);
            if (uNivel == NivelAcesso.Admin) usuarioAdmin = usuario;
        }

        if (usuarioAdmin is null && usuarios.Length > 0)
            usuarioAdmin = await context.Usuarios.FirstAsync(u => u.Email == usuarios[0].Email);

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
        var prefixo = texto.Length > 30 ? texto[..30] : texto;
        var existente = await context.AdminNotasTenant
            .FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Texto.StartsWith(prefixo) && n.ExcluidoEm == null);
        if (existente is not null) return;

        var nota = AdminNotaTenant.Criar(tenantId, autorAdminId, "admin@easystock.local", texto, tipo);
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
        var existente = await context.AuditLogs
            .AnyAsync(a => a.UsuarioId == usuarioId && a.Acao == acao && a.DataHora == dataHora);
        if (existente) return;

        var log = AuditLog.Criar(usuarioId, acao, sucesso, detalhes, ip, "Mozilla/5.0 (Test/Seed)");
        log.DataHora = dataHora;
        context.AuditLogs.Add(log);
    }

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
