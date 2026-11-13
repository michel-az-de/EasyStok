using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;

namespace EasyStock.Application.UseCases.Atendimento.Avisos;

/// <summary>
/// Uma pessoa da equipe e os avisos da equipe que ela recebe no WhatsApp (#2211). Sem telefone confirmado a inscrição
/// fica gravada, mas nada chega: a tela mostra o motivo.
/// </summary>
public sealed record PessoaAvisadaResult(
    Guid UsuarioId, string Nome, bool TelefoneConfirmado, bool Atencao, bool Pedido, bool Pagamento);

/// <summary>A equipe ativa da loja com as inscrições nos avisos da equipe.</summary>
public sealed class ListarInscricoesAvisosEquipeUseCase(IAudienciaUsuarios usuarios, IPreferenciaNotificacaoRepository preferencias)
{
    public async Task<IReadOnlyList<PessoaAvisadaResult>> ExecuteAsync(Guid empresaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var equipe = await EquipeDaLoja.ListarAsync(usuarios, empresaId, ct);
        if (equipe.Count == 0) return [];

        var ids = equipe.Select(u => u.Id).ToList();
        var ligadas = new Dictionary<string, HashSet<Guid>>();
        foreach (var aviso in AvisosDaEquipe.Todos)
            ligadas[aviso.RotinaCodigo] = (await preferencias.ListarDaRotinaAsync(empresaId, aviso.RotinaCodigo, ids, ct))
                .Where(p => p.Habilitada).Select(p => p.UsuarioId).ToHashSet();

        bool Ligado(AvisoDaEquipe aviso, Guid id) => ligadas[aviso.RotinaCodigo].Contains(id);
        return equipe
            .OrderBy(u => u.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new PessoaAvisadaResult(u.Id, u.Nome, EquipeDaLoja.TelefoneConfirmado(u),
                Ligado(AvisosDaEquipe.AtencaoNoAtendimento, u.Id), Ligado(AvisosDaEquipe.PedidoComCanhoto, u.Id),
                Ligado(AvisosDaEquipe.ProblemaNoPagamento, u.Id)))
            .ToList();
    }
}

/// <summary>Grava os três avisos de uma pessoa de uma vez. Pessoa fora da equipe ativa da loja é recusada.</summary>
public sealed class SalvarInscricaoAvisosEquipeUseCase(
    IAudienciaUsuarios usuarios, IPreferenciaNotificacaoRepository preferencias, IUnitOfWork unitOfWork)
{
    public async Task<PessoaAvisadaResult> ExecuteAsync(
        Guid empresaId, Guid usuarioId, bool atencao, bool pedido, bool pagamento, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var pessoa = (await EquipeDaLoja.ListarAsync(usuarios, empresaId, ct)).FirstOrDefault(u => u.Id == usuarioId)
                     ?? throw new UseCaseValidationException("Essa pessoa não faz parte da equipe ativa da loja.");

        await preferencias.DefinirAsync(usuarioId, empresaId, AvisosDaEquipe.AtencaoNoAtendimento.RotinaCodigo, atencao, ct);
        await preferencias.DefinirAsync(usuarioId, empresaId, AvisosDaEquipe.PedidoComCanhoto.RotinaCodigo, pedido, ct);
        await preferencias.DefinirAsync(usuarioId, empresaId, AvisosDaEquipe.ProblemaNoPagamento.RotinaCodigo, pagamento, ct);
        await unitOfWork.CommitAsync();

        return new PessoaAvisadaResult(pessoa.Id, pessoa.Nome, EquipeDaLoja.TelefoneConfirmado(pessoa), atencao, pedido, pagamento);
    }
}

/// <summary>Os mesmos candidatos da audiência <c>inscritos</c>: todo nível da loja, só quem está ativo.</summary>
internal static class EquipeDaLoja
{
    private static readonly NivelAcesso[] Niveis =
        [NivelAcesso.Admin, NivelAcesso.Gerente, NivelAcesso.Operador, NivelAcesso.Visualizador];

    public static async Task<IReadOnlyList<UsuarioParaAudiencia>> ListarAsync(
        IAudienciaUsuarios usuarios, Guid empresaId, CancellationToken ct) =>
        (await usuarios.ListarDaEmpresaAsync(empresaId, Niveis, ct)).Where(u => u.Ativo).DistinctBy(u => u.Id).ToList();

    public static bool TelefoneConfirmado(UsuarioParaAudiencia u) =>
        !string.IsNullOrWhiteSpace(u.Telefone) && u.TelefoneVerificadoEm is not null;
}
