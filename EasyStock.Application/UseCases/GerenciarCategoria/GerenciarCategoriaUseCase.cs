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
        IReadOnlyCollection<CategoriaResult> SubCategorias);

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
                var pai = await categoriaRepository.GetByIdAsync(command.CategoriaPaiId.Value);
                if (pai == null || pai.EmpresaId != command.EmpresaId)
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

            var categoria = await categoriaRepository.GetByIdAsync(command.Id);
            if (categoria == null || categoria.EmpresaId != command.EmpresaId)
                throw new UseCaseValidationException("Categoria nao encontrada.");

            if (command.CategoriaPaiId.HasValue)
            {
                if (command.CategoriaPaiId == command.Id)
                    throw new UseCaseValidationException("Uma categoria nao pode ser filha de si mesma.");
                var pai = await categoriaRepository.GetByIdAsync(command.CategoriaPaiId.Value);
                if (pai == null || pai.EmpresaId != command.EmpresaId)
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

        public async Task RemoverAsync(Guid id, Guid empresaId)
        {
            var categoria = await categoriaRepository.GetByIdAsync(id);
            if (categoria == null || categoria.EmpresaId != empresaId)
                throw new UseCaseValidationException("Categoria nao encontrada.");

            if (await categoriaRepository.ExisteProdutosNaCategoriaAsync(id))
                throw new UseCaseValidationException("Nao e possivel remover uma categoria que possui produtos vinculados.");

            await categoriaRepository.DeleteAsync(empresaId, id);
            await unitOfWork.CommitAsync();
        }

        public async Task<IReadOnlyCollection<CategoriaResult>> ListarAsync(Guid empresaId)
        {
            if (empresaId == Guid.Empty)
                throw new UseCaseValidationException("EmpresaId é obrigatório.");

            var categorias = await categoriaRepository.GetByEmpresaAsync(empresaId);
            return categorias.Select(ToResult).ToArray();
        }

        public async Task<CategoriaResult?> ObterAsync(Guid id, Guid empresaId)
        {
            var categoria = await categoriaRepository.GetByIdAsync(id);
            if (categoria == null || categoria.EmpresaId != empresaId)
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
            c.SubCategorias?.Select(ToResult).ToArray() ?? []);
    }
}
