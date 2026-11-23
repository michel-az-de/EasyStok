using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Automacoes;

public sealed record ModeloMensagemLocalResult(
    Guid Id, string Nome, string Idioma, string Categoria, string? Cabecalho, string Corpo, string? Rodape,
    int QuantidadeParametros, bool Arquivado, DateTime AlteradoEm)
{
    internal static ModeloMensagemLocalResult De(ModeloMensagemLocal m) => new(
        m.Id, m.Nome, m.Idioma, m.Categoria, m.Cabecalho, m.Corpo, m.Rodape, m.QuantidadeParametros, m.Arquivado, m.AlteradoEm);
}

public sealed record SalvarModeloMensagemLocalCommand(
    Guid EmpresaId, string Nome, string Idioma, string Categoria, string? Cabecalho, string Corpo, string? Rodape);

public sealed class ModeloMensagemLocalNaoEncontradoException(Guid id) : Exception($"Mensagem padrão {id} não encontrada.");

/// <summary>Nome e idioma já usados por outra mensagem padrão da empresa: 409 no controller.</summary>
public sealed class ModeloMensagemLocalDuplicadoException(string nome, string idioma)
    : Exception($"Já existe uma mensagem padrão {nome} em {idioma}.");

/// <summary>
/// #3204: mensagens padrão locais (formato aprovado pela Meta). Nome + idioma são únicos por empresa,
/// contando as arquivadas. Não há render: o envio usa o modelo já aprovado na Meta.
/// </summary>
public sealed class ModelosMensagemLocaisUseCases(
    IModeloMensagemLocalRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<IReadOnlyList<ModeloMensagemLocalResult>> ListarAsync(Guid empresaId, bool incluirArquivados, CancellationToken ct = default) =>
        (await repository.ListarAsync(empresaId, incluirArquivados, ct)).Select(ModeloMensagemLocalResult.De).ToList();

    public async Task<ModeloMensagemLocalResult> CriarAsync(SalvarModeloMensagemLocalCommand command, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var modelo = Validar(() => ModeloMensagemLocal.Criar(command.EmpresaId, command.Nome, command.Idioma,
            command.Categoria, command.Cabecalho, command.Corpo, command.Rodape, agora));
        await GarantirNomeLivreAsync(command.EmpresaId, modelo.Nome, modelo.Idioma, null, ct);

        await repository.AddAsync(modelo, ct);
        await unitOfWork.CommitAsync();
        return ModeloMensagemLocalResult.De(modelo);
    }

    public async Task<ModeloMensagemLocalResult> EditarAsync(Guid id, SalvarModeloMensagemLocalCommand command, CancellationToken ct = default)
    {
        var modelo = await ObterAsync(command.EmpresaId, id, ct);
        var nome = Validar(() => ModeloMensagemLocal.NormalizarNome(command.Nome));
        var idioma = Validar(() => ModeloMensagemLocal.NormalizarIdioma(command.Idioma));
        await GarantirNomeLivreAsync(command.EmpresaId, nome, idioma, id, ct);

        var agora = relogio.GetUtcNow().UtcDateTime;
        Validar(() =>
        {
            modelo.Editar(nome, idioma, command.Categoria, command.Cabecalho, command.Corpo, command.Rodape, agora);
            return modelo;
        });
        await unitOfWork.CommitAsync();
        return ModeloMensagemLocalResult.De(modelo);
    }

    public async Task<ModeloMensagemLocalResult> ArquivarAsync(Guid empresaId, Guid id, bool arquivado, CancellationToken ct = default)
    {
        var modelo = await ObterAsync(empresaId, id, ct);
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (arquivado) modelo.Arquivar(agora); else modelo.Desarquivar(agora);
        await unitOfWork.CommitAsync();
        return ModeloMensagemLocalResult.De(modelo);
    }

    private async Task<ModeloMensagemLocal> ObterAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        await repository.ObterAsync(empresaId, id, ct) ?? throw new ModeloMensagemLocalNaoEncontradoException(id);

    private async Task GarantirNomeLivreAsync(Guid empresaId, string nome, string idioma, Guid? excetoId, CancellationToken ct)
    {
        if (await repository.ExisteAsync(empresaId, nome, idioma, excetoId, ct))
            throw new ModeloMensagemLocalDuplicadoException(nome, idioma);
    }

    private static T Validar<T>(Func<T> acao)
    {
        try
        {
            return acao();
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}
