namespace EasyStock.Infra.Postgre.Concurrency;

/// <summary>
/// Chaves estaveis para advisory locks do PostgreSQL (pg_try_advisory_lock).
///
/// Cada chave e um long (64 bits) escolhido para ser mnemonico em hex ASCII e nao colidir
/// entre si. Quem adicionar uma chave nova: garantir que nao colide com nenhuma listada
/// abaixo (grep).
/// </summary>
public static class LockKeys
{
    /// <summary>
    /// Cobranca de assinaturas (mensalidade SaaS).
    /// CobrancaAssinaturaJob serializa entre replicas pra nao cobrar 2x.
    /// </summary>
    public const long CobrancaAssinatura = 0x4B69_6C6C_4561_7379L;

    /// <summary>
    /// Geracao de faturas a partir do vencimento.
    /// </summary>
    public const long FaturaVencimento = 0x4661_7475_5665_6E63L;

    /// <summary>
    /// Reconciliacao Pix/boleto x faturas.
    /// </summary>
    public const long FaturaReconciliacao = 0x4661_7475_5265_636FL;

    /// <summary>
    /// SLA monitor (Helpdesk) — varredura de tickets fora de SLA.
    /// </summary>
    public const long SlaMonitor = 0x534C_4148_0000_0001L;

    /// <summary>
    /// Base do dispatcher de notificacoes — soma com shardKey por canal.
    /// Reservado: 0x4E4F_5449_0000_0000 ate 0x4E4F_5449_FFFF_FFFF.
    /// </summary>
    public const long NotificacoesDispatcherBase = 0x4E4F_5449_0000_0000L;

    /// <summary>
    /// Migrations + Seeds no startup da API.
    /// Serializa boot multi-replica pra evitar race em DDL/seed que zere dados (R6).
    /// Quando outra replica detem o lock, esta replica pula migrations/seeds —
    /// confia que a primeira concluiu antes do health check passar.
    /// </summary>
    public const long StartupMigrationsAndSeed = 0x426F_6F74_5365_6564L;

    /// <summary>
    /// Varredura diária de caixas esquecidos abertos (CaixaEsquecidoJob).
    /// Serializa entre réplicas pra não notificar 2x. "CaixEsq" em hex ASCII.
    /// </summary>
    public const long CaixaEsquecidoMonitor = 0x4361_6978_4573_7100L;
}
