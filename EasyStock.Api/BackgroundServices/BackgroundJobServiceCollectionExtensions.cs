using EasyStock.Api.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Api.BackgroundServices;

public static class BackgroundJobServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BackgroundJobOptions>(
            configuration.GetSection(BackgroundJobOptions.SectionName));

        services.TryAddSingleton<IPedidoFornecedorRecebimentoProcessor, NoOpPedidoFornecedorRecebimentoProcessor>();

        var options = configuration.GetSection(BackgroundJobOptions.SectionName).Get<BackgroundJobOptions>()
            ?? new BackgroundJobOptions();

        if (options.EnableAnalisadorEstoque)
            services.AddHostedService<AnalisadorEstoqueBackgroundService>();

        if (options.EnableCacheWarmup)
            services.AddHostedService<CacheWarmupService>();

        // Health snapshot service (singleton para ser injetado no DiagnosticoController).
        // O singleton sempre é registrado para o DiagnosticoController poder consumir o último snapshot,
        // mas o HostedService (loop em background) só roda se a flag estiver habilitada.
        services.AddSingleton<HealthSnapshotService>();
        if (options.EnableHealthSnapshot)
            services.AddHostedService(sp => sp.GetRequiredService<HealthSnapshotService>());

        // Backup automático de logs no file storage (a cada 30 min)
        if (options.EnableLogStorage)
            services.AddHostedService<LogStorageBackgroundService>();

        if (options.EnableAlertasEstoqueJob)
            services.AddHostedService<AlertasEstoqueJob>();

        if (options.EnableProcessarRecebimentoJob)
            services.AddHostedService<ProcessarRecebimentoJob>();

        if (options.EnableRecalcularVelocidadesJob)
            services.AddHostedService<RecalcularVelocidadesJob>();

        if (options.EnableRelatorioMensalJob)
            services.AddHostedService<RelatorioMensalJob>();

        if (options.EnableDiagnosticoEmailReport)
            services.AddHostedService<DiagnosticoEmailReportJob>();

        // ContaFinanceiraVencimentoJob (CAP/CAR) — diario 09:30 UTC, marca
        // parcelas vencidas e atualiza status agregado das contas.
        if (options.EnableContaFinanceiraVencimentoJob)
            services.AddHostedService<ContaFinanceiraVencimentoJob>();

        // ContaReceberPixReconciliacaoJob (CAP/CAR) — horario, consulta Efi
        // pra fechar gaps de webhooks perdidos em parcelas CR com Pix ativo.
        if (options.EnableContaReceberPixReconciliacaoJob)
            services.AddHostedService<ContaReceberPixReconciliacaoJob>();

        // CobrancaPedidoJob (S11) — a cada 60 s, expira links do Mercado Pago vencidos, reemite uma vez
        // para pedido da conversa e cancela os demais (libera a vaga).
        if (options.EnableCobrancaPedido)
            services.AddHostedService<CobrancaPedidoJob>();

        // ImpressaoPendenteAlertaJob (S20) — a cada 2 min, canhoto pendente há mais de 3 min vira
        // impressao.atrasada no SSE de operação (o console avisa a dona).
        if (options.EnableImpressaoPendenteAlerta)
            services.AddHostedService<ImpressaoPendenteAlertaJob>();

        // PedidoAtrasoJob (S21) — a cada 60 s, publica pedido.atrasado uma vez por pedido aguardando
        // com o início previsto vencido (o KDS deriva o card atrasado sozinho; o job é o aviso no SSE).
        if (options.EnablePedidoAtraso)
            services.AddHostedService<PedidoAtrasoJob>();

        // CaixaEsquecidoJob (#641) — diario 10:00 UTC, detecta caixas abertos nao fechados de
        // dias anteriores e notifica in-app (so notifica, nao fecha).
        if (options.EnableCaixaEsquecidoJob)
            services.AddHostedService<CaixaEsquecidoJob>();

        // Atendimento WhatsApp (S03): drena a fila de midia que o webhook enfileira. Precisa ficar
        // neste processo porque a fila e em memoria (BackgroundQueueService).
        services.AddHostedService<AtendimentoFilaMidiaBackgroundService>();

        // S39: mensagens programadas ao cliente, disparadas pelo banco (não é fila em memória).
        if (options.EnableMensagensProgramadas)
            services.AddHostedService<MensagensProgramadasBackgroundService>();

        // S43: lembretes internos da dona (pagamento sem baixa, cliente sem resposta, manuais vencidos).
        if (options.EnableAvaliadorLembretes)
            services.AddHostedService<AvaliadorLembretesBackgroundService>();

        // S30: campanhas — primeira onda no horário agendado, conciliação com o outbox, encerramento e lembrete.
        if (options.EnableCampanhaJob)
            services.AddHostedService<CampanhaJob>();

        // Atendimento WhatsApp (S06): drena a fila do turno do agente, mesmo motivo (fila em memoria).
        if (options.EnableAtendimentoTurnoAgente)
            services.AddHostedService<AtendimentoFilaTurnoAgenteBackgroundService>();

        // Chat do site (S36): apaga as sessões vencidas.
        if (options.EnableLimpezaSessoesChatSite)
            services.AddHostedService<LimpezaSessoesChatSiteBackgroundService>();

        return services;
    }
}
