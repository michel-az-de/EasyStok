using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using System.Runtime.CompilerServices;

namespace EasyStock.Application.UseCases.AnuncioIa
{
    public sealed record GerarAnuncioStreamingCommand(
        Guid EmpresaId,
        Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        string? InstrucoesComplementares);

    public class GerarAnuncioStreamingUseCase(
        IProdutoRepository produtoRepository,
        IProdutoVariacaoRepository produtoVariacaoRepository,
        IAssinaturaEmpresaRepository assinaturaRepository,
        IUsoIaRepository usoIaRepository,
        IGeradorDescricaoAnuncioStreaming geradorStreaming,
        IUnitOfWork unitOfWork)
    {
        public async IAsyncEnumerable<string> ExecuteAsync(
            GerarAnuncioStreamingCommand command,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

            var produto = await produtoRepository.GetByIdAsync(command.EmpresaId, command.ProdutoId)
                ?? throw new UseCaseValidationException("Produto nao encontrado.");

            ProdutoVariacao? variacao = null;
            if (command.ProdutoVariacaoId.HasValue)
            {
                variacao = await produtoVariacaoRepository.GetByIdAsync(command.ProdutoVariacaoId.Value)
                    ?? throw new UseCaseValidationException("Variacao de produto nao encontrada.");
                if (variacao.ProdutoId != produto.Id)
                    throw new UseCaseValidationException("A variacao informada nao pertence ao produto.");
            }

            await VerificarLimitePlanoAsync(command.EmpresaId);

            var agora = DateTime.UtcNow;
            var uso = await usoIaRepository.GetAsync(command.EmpresaId, agora.Year, agora.Month);
            if (uso == null)
            {
                uso = new UsoIa
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = command.EmpresaId,
                    Ano = agora.Year,
                    Mes = agora.Month,
                    TotalGeracoes = 0,
                    TotalTokens = 0,
                    AtualizadoEm = agora
                };
                await usoIaRepository.AddAsync(uso);
            }

            uso.TotalGeracoes++;
            uso.AtualizadoEm = DateTime.UtcNow;
            await usoIaRepository.UpdateAsync(uso);
            await unitOfWork.CommitAsync();

            await foreach (var chunk in geradorStreaming.GerarStreamAsync(produto, variacao, null, command.InstrucoesComplementares, ct))
            {
                yield return chunk;
            }
        }

        private async Task VerificarLimitePlanoAsync(Guid empresaId)
        {
            var assinatura = await assinaturaRepository.GetAtivaAsync(empresaId);
            if (assinatura?.Plano == null)
                return; // sem assinatura = sem bloqueio (plano default)

            var plano = assinatura.Plano;
            if (plano.GeracoesIaSaoIlimitadas)
                return;

            var agora = DateTime.UtcNow;
            var uso = await usoIaRepository.GetAsync(empresaId, agora.Year, agora.Month);
            var totalAtual = uso?.TotalGeracoes ?? 0;

            if (totalAtual >= plano.LimiteGeracoesIaMensais)
                throw new UseCaseValidationException(
                    $"Limite mensal de {plano.LimiteGeracoesIaMensais} geracoes de IA atingido para o plano '{plano.Nome}'.");
        }
    }
}
