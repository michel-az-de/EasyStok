using EasyStock.Application.UseCases.FeatureFlags;
using LojaEntity = EasyStock.Domain.Entities.Loja;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.ValueObjects.Pix;

namespace EasyStock.Application.UseCases.Pagamentos.PixLoja;

/// <summary>
/// Chave Pix da loja como o console a vê (#2233): só o tipo e o final da chave. A chave inteira entra pelo cadastro e
/// sai apenas dentro do copia e cola do pedido.
/// </summary>
/// <param name="PixDiretoLigado">A flag <see cref="FeatureCatalogo.PagamentoPixDireto"/> do tenant.</param>
public sealed record ChavePixLojaResult(
    Guid LojaId, bool Configurada, string? Tipo, string? ChaveMascarada, string? NomeRecebedor, string? Cidade, bool PixDiretoLigado);

/// <param name="Tipo"><c>cpf</c>, <c>cnpj</c>, <c>email</c>, <c>telefone</c> ou <c>aleatoria</c>.</param>
public sealed record DefinirChavePixLojaCommand(
    Guid EmpresaId, Guid LojaId, string? Tipo, string? Chave, string? NomeRecebedor, string? Cidade)
{
    /// <summary>Esconde a chave: o comando pode cair num log estruturado.</summary>
    public override string ToString() =>
        $"DefinirChavePixLojaCommand {{ EmpresaId = {EmpresaId}, LojaId = {LojaId}, Tipo = {Tipo}, Chave = ***, NomeRecebedor = {NomeRecebedor}, Cidade = {Cidade} }}";
}

/// <summary>Lê, grava e remove a chave Pix da loja (#2233). Loja de outra empresa responde nulo (404).</summary>
public sealed class ChavePixLojaUseCases(ILojaRepository lojas, ITenantFeatureFlagRepository flags, IUnitOfWork unitOfWork)
{
    public async Task<ChavePixLojaResult?> ObterAsync(Guid empresaId, Guid lojaId, CancellationToken ct = default)
    {
        var loja = await CarregarAsync(empresaId, lojaId);
        return loja is null ? null : await ResultadoAsync(loja, ct);
    }

    public async Task<ChavePixLojaResult?> DefinirAsync(DefinirChavePixLojaCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var loja = await CarregarAsync(command.EmpresaId, command.LojaId);
        if (loja is null) return null;

        RecebedorPix recebedor;
        try
        {
            recebedor = RecebedorPix.Criar(ChavePix.Criar(Tipo(command.Tipo), command.Chave), command.NomeRecebedor, command.Cidade);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            // As mensagens do domínio não repetem a chave (ChavePix).
            throw new UseCaseValidationException(ex.Message);
        }

        loja.DefinirRecebedorPix(recebedor);
        await lojas.UpdateAsync(loja);
        await unitOfWork.CommitAsync();
        return await ResultadoAsync(loja, ct);
    }

    public async Task<ChavePixLojaResult?> RemoverAsync(Guid empresaId, Guid lojaId, CancellationToken ct = default)
    {
        var loja = await CarregarAsync(empresaId, lojaId);
        if (loja is null) return null;
        if (loja.PixChave is not null)
        {
            loja.DefinirRecebedorPix(null);
            await lojas.UpdateAsync(loja);
            await unitOfWork.CommitAsync();
        }
        return await ResultadoAsync(loja, ct);
    }

    /// <summary>E-mail mostra o começo e o domínio; os outros tipos, só os 4 últimos caracteres.</summary>
    public static string Mascarar(ChavePix chave)
    {
        ArgumentNullException.ThrowIfNull(chave);
        if (chave.Tipo == TipoChavePix.Email)
        {
            var arroba = chave.Valor.IndexOf('@');
            return chave.Valor[..Math.Min(2, arroba)] + "***" + chave.Valor[arroba..];
        }
        return "***" + chave.Valor[^4..];
    }

    private async Task<LojaEntity?> CarregarAsync(Guid empresaId, Guid lojaId)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var loja = await lojas.GetByIdAsync(empresaId, lojaId);
        return loja is not null && loja.EmpresaId == empresaId ? loja : null;
    }

    private async Task<ChavePixLojaResult> ResultadoAsync(LojaEntity loja, CancellationToken ct)
    {
        var ligado = (await flags.ListarAtivasAsync(loja.EmpresaId, ct)).Contains(FeatureCatalogo.PagamentoPixDireto);
        var recebedor = loja.ObterRecebedorPix();
        return recebedor is null
            ? new ChavePixLojaResult(loja.Id, false, null, null, null, null, ligado)
            : new ChavePixLojaResult(loja.Id, true, Nome(recebedor.Chave.Tipo), Mascarar(recebedor.Chave),
                recebedor.Nome, recebedor.Cidade, ligado);
    }

    private static TipoChavePix Tipo(string? tipo) =>
        tipo?.Trim().ToLowerInvariant() switch
        {
            "cpf" => TipoChavePix.Cpf,
            "cnpj" => TipoChavePix.Cnpj,
            "email" => TipoChavePix.Email,
            "telefone" => TipoChavePix.Telefone,
            "aleatoria" => TipoChavePix.Aleatoria,
            _ => throw new UseCaseValidationException("Tipo de chave Pix deve ser cpf, cnpj, email, telefone ou aleatoria."),
        };

    private static string Nome(TipoChavePix tipo) => tipo.ToString().ToLowerInvariant();
}
