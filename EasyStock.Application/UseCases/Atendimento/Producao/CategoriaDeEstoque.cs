namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <summary>
/// Categoria de estoque que a produção do console cria quando precisa (M2.2 "Cardápio", M2.3
/// "Insumos"). O produto de estoque exige uma categoria; a dona não pensa nisso, então ela existe
/// sozinha, achada pelo nome sem diferenciar maiúsculas.
/// </summary>
public static class CategoriaDeEstoque
{
    public static async Task<Guid> ObterOuCriarAsync(
        ICategoriaRepository categorias, IUnitOfWork unitOfWork, Guid empresaId, string nome, string descricao)
    {
        var existente = (await categorias.GetByEmpresaAsync(empresaId))
            .FirstOrDefault(c => string.Equals(c.Nome.Trim(), nome, StringComparison.OrdinalIgnoreCase));
        if (existente is not null) return existente.Id;

        var agora = DateTime.UtcNow;
        var categoria = new Categoria
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = nome, Descricao = descricao,
            CriadoEm = agora, AlteradoEm = agora,
        };
        await categorias.AddAsync(categoria);
        await unitOfWork.CommitAsync();
        return categoria.Id;
    }
}
