using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.BackgroundServices
{
    public sealed class AnalisadorEstoqueBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<EasyStockConfiguracoes> config,
        ILogger<AnalisadorEstoqueBackgroundService> logger)
        : BackgroundService
    {
        private readonly TimeSpan _intervalo = TimeSpan.FromMinutes(60);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("AnalisadorEstoqueBackgroundService iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await AnalisarAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Erro durante analise de estoque no background service.");
                }

                await Task.Delay(_intervalo, stoppingToken);
            }
        }

        private async Task AnalisarAsync(CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();
            var estoqueRepo = scope.ServiceProvider.GetRequiredService<IItemEstoqueRepository>();
            var notificacaoRepo = scope.ServiceProvider.GetRequiredService<INotificacaoRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var empresaRepo = scope.ServiceProvider.GetRequiredService<IEmpresaRepository>();

            var empresas = await empresaRepo.GetAllAsync();

            foreach (var empresa in empresas)
                await AnalisarEmpresaAsync(empresa, estoqueRepo, notificacaoRepo, unitOfWork);

            await unitOfWork.CommitAsync();
            logger.LogInformation("Analise de estoque concluida para {TotalEmpresas} empresa(s).", empresas.Count());
        }

        private async Task AnalisarEmpresaAsync(
            Empresa empresa,
            IItemEstoqueRepository estoqueRepo,
            INotificacaoRepository notificacaoRepo,
            IUnitOfWork unitOfWork)
        {
            var cfg = config.Value;

            await ProcessarPaginadoAsync(
                page => estoqueRepo.GetEstoqueBaixoAsync(empresa.Id, cfg.LimiteEstoqueBaixoDefault, page, 100),
                async item =>
                {
                    var jaTem = await notificacaoRepo.ExisteNotificacaoNaoLidaAsync(
                        empresa.Id, TipoAlertaEstoque.EstoqueBaixo, item.Id);
                    if (jaTem) return;

                    await notificacaoRepo.AddAsync(Notificacao.Criar(
                        empresa.Id,
                        TipoAlertaEstoque.EstoqueBaixo,
                        $"Estoque baixo: item '{item.CodigoInterno ?? item.Id.ToString()}' com {item.QuantidadeAtual.Value} unidade(s).",
                        item.Id));
                });

            await ProcessarPaginadoAsync(
                page => estoqueRepo.GetProximoVencimentoAsync(empresa.Id, cfg.DiasAlertaVencimento, page, 100),
                async item =>
                {
                    var jaTem = await notificacaoRepo.ExisteNotificacaoNaoLidaAsync(
                        empresa.Id, TipoAlertaEstoque.ProximoVencimento, item.Id);
                    if (jaTem) return;

                    var diasRestantes = item.ValidadeEm?.DiasAteVencimento() ?? 0;
                    await notificacaoRepo.AddAsync(Notificacao.Criar(
                        empresa.Id,
                        TipoAlertaEstoque.ProximoVencimento,
                        $"Produto proximos ao vencimento: '{item.CodigoInterno ?? item.Id.ToString()}' vence em {diasRestantes} dia(s).",
                        item.Id));
                });

            await ProcessarPaginadoAsync(
                page => estoqueRepo.GetItensParadosAsync(empresa.Id, cfg.DiasItemParado, page, 100),
                async item =>
                {
                    var jaTem = await notificacaoRepo.ExisteNotificacaoNaoLidaAsync(
                        empresa.Id, TipoAlertaEstoque.ProdutoParado, item.Id);
                    if (jaTem) return;

                    await notificacaoRepo.AddAsync(Notificacao.Criar(
                        empresa.Id,
                        TipoAlertaEstoque.ProdutoParado,
                        $"Produto parado ha mais de {cfg.DiasItemParado} dias: '{item.CodigoInterno ?? item.Id.ToString()}'.",
                        item.Id));
                });
        }

        private static async Task ProcessarPaginadoAsync(
            Func<int, Task<(IEnumerable<ItemEstoque> Items, int TotalCount)>> carregarPagina,
            Func<ItemEstoque, Task> processarItem)
        {
            const int pageSize = 100;
            var page = 1;

            while (true)
            {
                var (items, totalCount) = await carregarPagina(page);
                var paginaAtual = items.ToList();

                foreach (var item in paginaAtual)
                    await processarItem(item);

                if (paginaAtual.Count == 0 || page * pageSize >= totalCount)
                    break;

                page++;
            }
        }
    }
}
