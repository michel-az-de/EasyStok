using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Automacoes;

public sealed record RespostaProntaResult(Guid Id, string Titulo, string Atalho, string Texto, bool Arquivada, DateTime AlteradaEm)
{
    internal static RespostaProntaResult De(RespostaPronta r) => new(r.Id, r.Titulo, r.Atalho, r.Texto, r.Arquivada, r.AlteradaEm);
}

public sealed record RespostaRenderizadaResult(Guid RespostaId, Guid ConversaId, string Texto);

public sealed record SalvarRespostaProntaCommand(Guid EmpresaId, string Titulo, string Atalho, string Texto);

public sealed class RespostaProntaNaoEncontradaException(Guid id) : Exception($"Resposta pronta {id} não encontrada.");

/// <summary>Atalho já usado por outra resposta da empresa: 409 no controller.</summary>
public sealed class AtalhoDuplicadoException(string atalho) : Exception($"O atalho {atalho} já existe.");

/// <summary>
/// S42: biblioteca de respostas prontas do console (US-009). O atalho é único por empresa, contando as
/// arquivadas. O render acontece no servidor com os dados da conversa; variável sem valor é recusada.
/// </summary>
public sealed class RespostasProntasUseCases(
    IRespostaProntaRepository repository,
    IConversaRepository conversas,
    VariaveisAtendimento variaveis,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<IReadOnlyList<RespostaProntaResult>> ListarAsync(Guid empresaId, bool incluirArquivadas, CancellationToken ct = default) =>
        (await repository.ListarAsync(empresaId, incluirArquivadas, ct)).Select(RespostaProntaResult.De).ToList();

    public async Task<RespostaProntaResult> CriarAsync(SalvarRespostaProntaCommand command, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var resposta = Validar(() => RespostaPronta.Criar(command.EmpresaId, command.Titulo, command.Atalho, command.Texto, agora));
        await GarantirAtalhoLivreAsync(command.EmpresaId, resposta.Atalho, null, ct);

        await repository.AddAsync(resposta, ct);
        await unitOfWork.CommitAsync();
        return RespostaProntaResult.De(resposta);
    }

    public async Task<RespostaProntaResult> EditarAsync(Guid id, SalvarRespostaProntaCommand command, CancellationToken ct = default)
    {
        var resposta = await ObterAsync(command.EmpresaId, id, ct);
        var atalho = Validar(() => RespostaPronta.NormalizarAtalho(command.Atalho));
        await GarantirAtalhoLivreAsync(command.EmpresaId, atalho, id, ct);

        var agora = relogio.GetUtcNow().UtcDateTime;
        Validar(() => { resposta.Editar(command.Titulo, atalho, command.Texto, agora); return resposta; });
        await unitOfWork.CommitAsync();
        return RespostaProntaResult.De(resposta);
    }

    public async Task<RespostaProntaResult> ArquivarAsync(Guid empresaId, Guid id, bool arquivada, CancellationToken ct = default)
    {
        var resposta = await ObterAsync(empresaId, id, ct);
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (arquivada) resposta.Arquivar(agora); else resposta.Desarquivar(agora);
        await unitOfWork.CommitAsync();
        return RespostaProntaResult.De(resposta);
    }

    public async Task<RespostaRenderizadaResult> RenderizarAsync(Guid empresaId, Guid id, Guid conversaId, CancellationToken ct = default)
    {
        var resposta = await ObterAsync(empresaId, id, ct);
        var conversa = await conversas.ObterPorIdAsync(empresaId, conversaId, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);

        var valores = await variaveis.ResolverAsync(empresaId, conversa, ct: ct);
        var texto = Validar(() => resposta.Renderizar(valores));
        return new RespostaRenderizadaResult(resposta.Id, conversa.Id, texto);
    }

    private async Task<RespostaPronta> ObterAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        await repository.ObterAsync(empresaId, id, ct) ?? throw new RespostaProntaNaoEncontradaException(id);

    private async Task GarantirAtalhoLivreAsync(Guid empresaId, string atalho, Guid? excetoId, CancellationToken ct)
    {
        if (await repository.ExisteAtalhoAsync(empresaId, atalho, excetoId, ct))
            throw new AtalhoDuplicadoException(atalho);
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
