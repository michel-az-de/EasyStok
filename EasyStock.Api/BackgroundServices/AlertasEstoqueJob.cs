using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job para gerar alertas de estoque automaticamente.
/// Executa periodicamente para identificar itens com estoque baixo, vencimento próximo, etc.
/// </summary>
public sealed class AlertasEstoqueJob(
    IServiceProvider serviceProvider,
    ILogger<AlertasEstoqueJob> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(1); // Executa a cada hora

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job de alertas de estoque iniciado");

        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessarAlertasAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no processamento de alertas de estoque");
            }
        }
    }

    private async Task ProcessarAlertasAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var itemEstoqueRepo = scope.ServiceProvider.GetRequiredService<IItemEstoqueRepository>();
        var notificacaoRepo = scope.ServiceProvider.GetRequiredService<INotificacaoRepository>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var empresaRepo = scope.ServiceProvider.GetRequiredService<IEmpresaRepository>();

        // Obter todas as empresas ativas
        var empresas = await empresaRepo.GetAllAsync();
        foreach (var empresa in empresas.Where(e => e.Ativa))
        {
            await ProcessarEmpresaAsync(empresa, itemEstoqueRepo, notificacaoRepo, emailService, cancellationToken);
        }
    }

    private async Task ProcessarEmpresaAsync(
        Empresa empresa,
        IItemEstoqueRepository itemEstoqueRepo,
        INotificacaoRepository notificacaoRepo,
        IEmailService emailService,
        CancellationToken cancellationToken)
    {
        // 1. Itens com estoque baixo
        var (itensBaixo, _) = await itemEstoqueRepo.GetEstoqueBaixoAsync(empresa.Id, 5, 1, 100);
        foreach (var item in itensBaixo)
        {
            var existe = await notificacaoRepo.ExisteNotificacaoNaoLidaAsync(empresa.Id, TipoAlertaEstoque.EstoqueBaixo, item.Id);
            if (!existe)
            {
                var notificacao = Notificacao.Criar(
                    empresa.Id,
                    TipoAlertaEstoque.EstoqueBaixo,
                    $"Item {item.Produto?.Nome ?? "N/A"} com estoque baixo: {item.QuantidadeAtual.Value} unidades",
                    item.Id);

                await notificacaoRepo.AddAsync(notificacao);

                // Enviar email se empresa tiver email configurado
                if (!string.IsNullOrEmpty(empresa.Email))
                {
                    await emailService.SendAsync(
                        empresa.Email,
                        "Alerta: Estoque Baixo",
                        $"O item {item.Produto?.Nome ?? "N/A"} está com estoque baixo ({item.QuantidadeAtual.Value} unidades).");
                }
            }
        }

        // 2. Itens próximos ao vencimento
        var (itensVencimento, _) = await itemEstoqueRepo.GetProximoVencimentoAsync(empresa.Id, 30, 1, 100);
        foreach (var item in itensVencimento)
        {
            var existe = await notificacaoRepo.ExisteNotificacaoNaoLidaAsync(empresa.Id, TipoAlertaEstoque.VencimentoProximo, item.Id);
            if (!existe)
            {
                var notificacao = Notificacao.Criar(
                    empresa.Id,
                    TipoAlertaEstoque.VencimentoProximo,
                    $"Item {item.Produto?.Nome ?? "N/A"} vence em {item.ValidadeEm?.DiasAteVencimento() ?? 0} dias",
                    item.Id);

                await notificacaoRepo.AddAsync(notificacao);
            }
        }

        // 3. Itens parados
        var (itensParados, _) = await itemEstoqueRepo.GetItensParadosAsync(empresa.Id, 90, 1, 100);
        foreach (var item in itensParados)
        {
            var existe = await notificacaoRepo.ExisteNotificacaoNaoLidaAsync(empresa.Id, TipoAlertaEstoque.ItemParado, item.Id);
            if (!existe)
            {
                var notificacao = Notificacao.Criar(
                    empresa.Id,
                    TipoAlertaEstoque.ItemParado,
                    $"Item {item.Produto?.Nome ?? "N/A"} parado há {(DateTime.UtcNow - (item.UltimaMovimentacaoEm ?? item.EntradaEm)).Days} dias",
                    item.Id);

                await notificacaoRepo.AddAsync(notificacao);
            }
        }

        logger.LogInformation("Alertas processados para empresa {EmpresaId}", empresa.Id);
    }
}