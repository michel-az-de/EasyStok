using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;

namespace EasyStock.Application.UseCases.GerenciarCategoria
{
    public sealed record CriarCategoriaCommand(
        Guid EmpresaId,
        string Nome,
        string? Descricao,
        Guid? CategoriaPaiId);

    public sealed record AtualizarCategoriaCommand(
        Guid Id,
        Guid EmpresaId,
        string Nome,
        string? Descricao,
        Guid? CategoriaPaiId);

    public sealed record CategoriaResult(
        Guid Id,
        Guid EmpresaId,
        Guid? CategoriaPaiId,
        string Nome,
        string? Descricao,
        DateTime CriadoEm,
        DateTime AlteradoEm,
        IReadOnlyCollection<CategoriaResult> SubCategorias,
        int? QuantidadeMinima = null,
        int? QuantidadeCritica = null);

    public class GerenciarCategoriaUseCase(
        ICategoriaRepository categoriaRepository,
        IUnitOfWork unitOfWork)
    {
        public async Task<CategoriaResult> CriarAsync(CriarCategoriaCommand command)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
            if (string.IsNullOrWhiteSpace(command.Nome))
                throw new UseCaseValidationException("Nome da categoria é obrigatório.");

            if (command.CategoriaPaiId.HasValue)
            {
                var pai = await categoriaRepository.GetByIdAsync(command.EmpresaId, command.CategoriaPaiId.Value);
                if (pai == null)
                    throw new UseCaseValidationException("Categoria pai nao encontrada ou nao pertence a esta empresa.");
            }

            var agora = DateTime.UtcNow;
            var categoria = new Categoria
            {
                Id = Guid.NewGuid(),
                EmpresaId = command.EmpresaId,
                Nome = command.Nome.Trim(),
                Descricao = command.Descricao?.Trim(),
                CategoriaPaiId = command.CategoriaPaiId,
                CriadoEm = agora,
                AlteradoEm = agora
            };

            await categoriaRepository.AddAsync(categoria);
            await unitOfWork.CommitAsync();

            return ToResult(categoria);
        }

        public async Task<CategoriaResult> AtualizarAsync(AtualizarCategoriaCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.Nome))
                throw new UseCaseValidationException("Nome da categoria é obrigatório.");

            var categoria = await categoriaRepository.GetByIdAsync(command.EmpresaId, command.Id);
            if (categoria == null)
                throw new UseCaseValidationException("Categoria nao encontrada.");

            if (command.CategoriaPaiId.HasValue)
            {
                if (command.CategoriaPaiId == command.Id)
                    throw new UseCaseValidationException("Uma categoria nao pode ser filha de si mesma.");
                var pai = await categoriaRepository.GetByIdAsync(command.EmpresaId, command.CategoriaPaiId.Value);
                if (pai == null)
                    throw new UseCaseValidationException("Categoria pai nao encontrada ou nao pertence a esta empresa.");
            }

            categoria.Nome = command.Nome.Trim();
            categoria.Descricao = command.Descricao?.Trim();
            categoria.CategoriaPaiId = command.CategoriaPaiId;
            categoria.AlteradoEm = DateTime.UtcNow;

            await categoriaRepository.UpdateAsync(categoria);
            await unitOfWork.CommitAsync();

            return ToResult(categoria);
        }

        public async Task AtualizarLimiaresAsync(Guid empresaId, Guid id, int? quantidadeMinima, int? quantidadeCritica)
        {
            var categoria = await categoriaRepository.GetByIdAsync(empresaId, id);
            if (categoria == null)
                throw new UseCaseValidationException("Categoria nao encontrada.");

            if (quantidadeMinima.HasValue && quantidadeMinima.Value < 0)
                throw new UseCaseValidationException("Quantidade minima nao pode ser negativa.");
            if (quantidadeCritica.HasValue && quantidadeCritica.Value < 0)
                throw new UseCaseValidationException("Quantidade critica nao pode ser negativa.");
            if (quantidadeMinima.HasValue && quantidadeCritica.HasValue && quantidadeCritica.Value >= quantidadeMinima.Value)
                throw new UseCaseValidationException("Quantidade critica precisa ser menor que a minima.");

            categoria.QuantidadeMinima = quantidadeMinima;
            categoria.QuantidadeCritica = quantidadeCritica;
            categoria.AlteradoEm = DateTime.UtcNow;

            await categoriaRepository.UpdateAsync(categoria);
            await unitOfWork.CommitAsync();
        }

        public async Task RemoverAsync(Guid id, Guid empresaId)
        {
            var categoria = await categoriaRepository.GetByIdAsync(empresaId, id);
            if (categoria == null)
                throw new UseCaseValidationException("Categoria nao encontrada.");

            if (await categoriaRepository.ExisteProdutosNaCategoriaAsync(id))
                throw new UseCaseValidationException("Nao e possivel remover uma categoria que possui produtos vinculados.");

            await categoriaRepository.DeleteAsync(empresaId, id);
            await unitOfWork.CommitAsync();
        }

        public async Task<IReadOnlyCollection<CategoriaResult>> ListarAsync(Guid empresaId)
        {
            UseCaseGuards.EnsureEmpresaId(empresaId);

            var categorias = await categoriaRepository.GetByEmpresaAsync(empresaId);
            return categorias.Select(ToResult).ToArray();
        }

        public async Task<CategoriaResult?> ObterAsync(Guid id, Guid empresaId)
        {
            var categoria = await categoriaRepository.GetByIdAsync(empresaId, id);
            if (categoria == null)
                return null;
            return ToResult(categoria);
        }

        private static CategoriaResult ToResult(Categoria c) => new(
            c.Id,
            c.EmpresaId,
            c.CategoriaPaiId,
            c.Nome,
            c.Descricao,
            c.CriadoEm,
            c.AlteradoEm,
            c.SubCategorias?.Select(ToResult).ToArray() ?? [],
            c.QuantidadeMinima,
            c.QuantidadeCritica);
    }
}
