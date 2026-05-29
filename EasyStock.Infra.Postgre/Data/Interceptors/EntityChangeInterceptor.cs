using System.Text.Json;
using EasyStock.Application.Ports.Output;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Data.Interceptors;

/// <summary>
/// F10-B — Interceptor de auditoria universal.
///
/// Le <see cref="ChangeTracker"/> e gera <see cref="EntityAlteracao"/> pra
/// cada entidade do allowlist que foi criada, atualizada ou removida.
///
/// Principios:
///   #2 — operacao principal NUNCA falha por auditoria (try-catch global).
///   #4 — default-deny: allowlist explicita por entidade.
///   #41 — self-audit skip (EntityAlteracao, *Alteracao nao audita a si).
///
/// PII mascarada por default. Original criptografado em F10-B-2 (KMS).
/// </summary>
public sealed class EntityChangeInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<EntityChangeInterceptor> _logger;

    // --- Allowlist (default-deny) ---
    // Apenas entidades listadas aqui geram audit entries.
    private static readonly Dictionary<Type, HashSet<string>> Allowlist = new()
    {
        [typeof(Pedido)] = new() { "Status", "Total", "ClienteId", "Observacoes", "ClienteNome", "Origem" },
        [typeof(PedidoItem)] = new() { "Quantidade", "PrecoUnitario", "ProdutoId", "Subtotal" },
        [typeof(PedidoPagamento)] = new() { "Valor", "Metodo", "Estornado", "EstornadoEm" },
        [typeof(MovimentoCaixa)] = new() { "Valor", "Tipo", "Descricao", "Metodo", "Categoria", "Origem", "EstornadoEm", "MotivoEstorno" },
        [typeof(FechamentoCaixa)] = new() { "SaldoInicial", "SaldoFinal", "TotalVendas", "TotalPagamentosPedidos", "TotalEntradasExtras", "TotalSaidasExtras", "FechadoPorUserId", "FechadoPorNome" },
        [typeof(ItemEstoque)] = new() { "Status", "CustoUnitario", "PrecoVendaSugerido", "ValidadeEm", "LojaId", "QuantidadeAtual" },
        [typeof(Lote)] = new() { "Status", "Codigo", "DataProducao", "FinalizadoEm", "Observacoes" },
        [typeof(LoteItem)] = new() { "Quantidade" },
        [typeof(Loja)] = new() { "Nome", "Ativa" },
        [typeof(Empresa)] = new() { "Nome", "Plano", "Status" },
    };

    // Campos que NUNCA entram no audit, mesmo se no allowlist.
    private static readonly HashSet<string> AlwaysIgnored = new(StringComparer.Ordinal)
    {
        "CriadoEm", "AlteradoEm", "Versao", "IsDeletado", "UltimaMovimentacaoEm"
    };

    // Campos PII — se presentes no allowlist, valor e mascarado.
    private static readonly HashSet<string> PiiFields = new(StringComparer.Ordinal)
    {
        "Documento", "Telefone", "Email", "CpfCnpj", "Endereco", "Observacoes",
        "ClienteTelefone", "ClienteNome"
    };

    // Tipos que devem ser skipados (self-audit prevention).
    private static readonly HashSet<Type> SelfAuditTypes = new()
    {
        typeof(EntityAlteracao),
        typeof(ClienteAlteracao),
        typeof(ProdutoAlteracao),
        typeof(VendaAlteracao),
        typeof(MovimentacaoEstoqueAlteracao),
    };

    private const int MaxAlteracoesJsonBytes = 50 * 1024; // 50KB

    public EntityChangeInterceptor(
        ICurrentUserAccessor currentUser,
        IHttpContextAccessor httpContextAccessor,
        ILogger<EntityChangeInterceptor> logger)
    {
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        GenerateAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        GenerateAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void GenerateAuditEntries(DbContext? context)
    {
        if (context is null) return;

        try
        {
            var now = DateTime.UtcNow;
            var (userId, userName, origem) = ResolveActor();

            // Snapshot entries ANTES de iterar — evita ConcurrentModification
            // ao adicionar EntityAlteracao no mesmo ChangeTracker.
            var entries = context.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Where(e => !SelfAuditTypes.Contains(e.Entity.GetType()))
                .Where(e => Allowlist.ContainsKey(e.Entity.GetType()))
                .ToList();

            foreach (var entry in entries)
            {
                try
                {
                    var entityType = entry.Entity.GetType();
                    var allowedFields = Allowlist[entityType];
                    var empresaId = GetEmpresaId(entry);

                    if (empresaId == Guid.Empty) continue; // sem tenant, skip

                    var entidadeId = GetEntityId(entry);
                    if (entidadeId == Guid.Empty) continue;

                    var tipoEntidade = entityType.Name;
                    var acao = entry.State switch
                    {
                        EntityState.Added => "criado",
                        EntityState.Modified => "atualizado",
                        EntityState.Deleted => "removido",
                        _ => "desconhecido"
                    };

                    if (entry.State == EntityState.Modified)
                    {
                        // Gera 1 entry por campo modificado + 1 com AlteracoesJson (diff completo)
                        var changes = new List<object>();
                        foreach (var prop in entry.Properties)
                        {
                            if (!prop.IsModified) continue;
                            var propName = prop.Metadata.Name;
                            if (AlwaysIgnored.Contains(propName)) continue;
                            if (!allowedFields.Contains(propName)) continue;

                            var oldVal = FormatValue(propName, prop.OriginalValue);
                            var newVal = FormatValue(propName, prop.CurrentValue);

                            if (oldVal == newVal) continue; // sem mudanca real

                            changes.Add(new { campo = propName, de = oldVal, para = newVal });

                            context.Add(new EntityAlteracao
                            {
                                Id = Guid.NewGuid(),
                                EmpresaId = empresaId,
                                TipoEntidade = tipoEntidade,
                                EntidadeId = entidadeId,
                                Acao = acao,
                                Campo = propName,
                                ValorAntigo = MaskIfPii(propName, oldVal),
                                ValorNovo = MaskIfPii(propName, newVal),
                                AlteradoPorUserId = userId,
                                AlteradoPorNome = userName,
                                Origem = origem,
                                AlteradoEm = now,
                            });
                        }

                        // AlteracoesJson consolidado (se houve mudancas)
                        if (changes.Count > 0)
                        {
                            var json = SerializeChanges(changes);
                            context.Add(new EntityAlteracao
                            {
                                Id = Guid.NewGuid(),
                                EmpresaId = empresaId,
                                TipoEntidade = tipoEntidade,
                                EntidadeId = entidadeId,
                                Acao = acao,
                                AlteracoesJson = json,
                                AlteradoPorUserId = userId,
                                AlteradoPorNome = userName,
                                Origem = origem,
                                AlteradoEm = now,
                            });
                        }
                    }
                    else // Added or Deleted
                    {
                        var changes = new List<object>();
                        foreach (var prop in entry.Properties)
                        {
                            var propName = prop.Metadata.Name;
                            if (AlwaysIgnored.Contains(propName)) continue;
                            if (propName == "Id" || propName == "EmpresaId") continue;
                            if (!allowedFields.Contains(propName)) continue;

                            var val = FormatValue(propName, prop.CurrentValue);
                            if (entry.State == EntityState.Added)
                                changes.Add(new { campo = propName, de = (string?)null, para = MaskIfPii(propName, val) });
                            else
                                changes.Add(new { campo = propName, de = MaskIfPii(propName, val), para = (string?)null });
                        }

                        var json = changes.Count > 0 ? SerializeChanges(changes) : null;

                        context.Add(new EntityAlteracao
                        {
                            Id = Guid.NewGuid(),
                            EmpresaId = empresaId,
                            TipoEntidade = tipoEntidade,
                            EntidadeId = entidadeId,
                            Acao = acao,
                            AlteracoesJson = json,
                            AlteradoPorUserId = userId,
                            AlteradoPorNome = userName,
                            Origem = origem,
                            AlteradoEm = now,
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Principio #2: falha de auditoria de 1 entry NAO derruba operacao.
                    _logger.LogWarning(ex, "EntityChangeInterceptor: falha ao auditar {Entity}",
                        entry.Entity.GetType().Name);
                }
            }
        }
        catch (Exception ex)
        {
            // Principio #2: catch global — operacao NUNCA falha por auditoria.
            _logger.LogError(ex, "EntityChangeInterceptor: falha global ao gerar audit entries");
        }
    }

    // --- Helpers ---

    private (Guid? userId, string? userName, string origem) ResolveActor()
    {
        // 1. Mobile device (via MobileApiKeyAuthHandler)
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Items.TryGetValue("mobile-device", out var deviceObj) == true)
        {
            // Mobile sync — usuario resolvido pelo MobileSystemUserResolver (F9-E)
            var operatorName = httpContext.Items.TryGetValue("mobile-operator-name", out var opName)
                ? opName as string : null;
            return (null, operatorName ?? "Mobile", "mobile");
        }

        // 2. Web/API JWT
        if (_currentUser.IsAuthenticated)
        {
            return (_currentUser.UsuarioId, null, "web");
        }

        // 3. Fallback sistema
        return (null, "Sistema", "sistema");
    }

    private static Guid GetEmpresaId(EntityEntry entry)
    {
        var prop = entry.Metadata.FindProperty("EmpresaId");
        if (prop is null) return Guid.Empty;
        var val = entry.Property("EmpresaId").CurrentValue;
        return val is Guid g ? g : Guid.Empty;
    }

    private static Guid GetEntityId(EntityEntry entry)
    {
        var prop = entry.Metadata.FindProperty("Id");
        if (prop is null) return Guid.Empty;
        var val = entry.Property("Id").CurrentValue;
        return val is Guid g ? g : Guid.Empty;
    }

    private static string? FormatValue(string propName, object? value)
    {
        if (value is null) return null;
        if (value is DateTime dt) return dt.ToString("O");
        if (value is DateTimeOffset dto) return dto.ToString("O");
        if (value is Guid g) return g.ToString();
        if (value is decimal d) return d.ToString("F2");
        if (value is bool b) return b ? "true" : "false";
        return value.ToString();
    }

    private static string? MaskIfPii(string propName, string? value)
    {
        if (value is null) return null;
        if (!PiiFields.Contains(propName)) return value;

        // Mascarar: mostra ultimos 4 chars, prefixo ***
        if (value.Length <= 4) return "***";
        return "***" + value[^4..];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static string? SerializeChanges(List<object> changes)
    {
        var json = JsonSerializer.Serialize(changes, JsonOptions);

        if (json.Length > MaxAlteracoesJsonBytes)
        {
            // Truncar: reduz lista de changes até caber, mantendo JSON válido
            var keep = changes.Count;
            while (keep > 1)
            {
                keep = keep / 2;
                var partial = changes.Take(keep).ToList();
                partial.Add(new { _truncated = true, totalFields = changes.Count, keptFields = keep });
                json = JsonSerializer.Serialize(partial, JsonOptions);
                if (json.Length <= MaxAlteracoesJsonBytes) return json;
            }
            // Fallback: metadata only
            return JsonSerializer.Serialize(new { _truncated = true, totalFields = changes.Count, keptFields = 0 }, JsonOptions);
        }

        return json;
    }
}
