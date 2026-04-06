using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;

namespace EasyStock.Application.UseCases.AnuncioIa
{
    public sealed record SalvarRascunhoAnuncioCommand(
        Guid EmpresaId,
        Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        string Titulo,
        string Conteudo,
        string? InstrucoesUsadas,
        int TokensConsumidos);

    public sealed record SalvarRascunhoAnuncioResult(
        Guid Id,
        Guid ProdutoId,
        string Titulo,
        DateTime CriadoEm);

    public class SalvarRascunhoAnuncioUseCase(
        IProdutoRepository produtoRepository,
        IAnuncioIaRepository anuncioIaRepository,
        IUnitOfWork unitOfWork)
    {
        public async Task<SalvarRascunhoAnuncioResult> ExecuteAsync(SalvarRascunhoAnuncioCommand command)
        {
            var produto = await produtoRepository.GetByIdAsync(command.EmpresaId, command.ProdutoId)
                ?? throw new UseCaseValidationException("Produto nao encontrado.");

            var anuncio = new EasyStock.Domain.Entities.AnuncioIa
            {
                Id = Guid.NewGuid(),
                EmpresaId = command.EmpresaId,
                ProdutoId = produto.Id,
                ProdutoVariacaoId = command.ProdutoVariacaoId,
                Titulo = command.Titulo,
                Conteudo = command.Conteudo,
                InstrucoesUsadas = command.InstrucoesUsadas,
                TokensConsumidos = command.TokensConsumidos,
                Salvo = true,
                CriadoEm = DateTime.UtcNow
            };

            await anuncioIaRepository.AddAsync(anuncio);
            await unitOfWork.CommitAsync();

            return new SalvarRascunhoAnuncioResult(anuncio.Id, anuncio.ProdutoId, anuncio.Titulo, anuncio.CriadoEm);
        }
    }
}
