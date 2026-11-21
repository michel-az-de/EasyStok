namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// #2492: o que a equipe decidiu sobre um telefone do WhatsApp ao corrigir o cliente de uma conversa.
/// Diz qual cliente o telefone NÃO é e, quando a equipe apontou outro, qual ele É. Vale para as próximas
/// conversas do telefone até ser revogada. Telefone só em hash (SHA-256 do E.164 canônico).
/// </summary>
public sealed class DecisaoVinculoTelefone
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string TelefoneHash { get; private set; } = null!;
    public Guid? ClienteRecusadoId { get; private set; }
    public Guid? ClienteEscolhidoId { get; private set; }
    public Guid ConversaOrigemId { get; private set; }
    public Guid AlteracaoVinculoId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public DateTime CriadaEm { get; private set; }
    public DateTime? RevogadaEm { get; private set; }
    public Guid? RevogadaPorUsuarioId { get; private set; }

    public bool Ativa => RevogadaEm is null;

    private DecisaoVinculoTelefone() { }

    /// <summary>O telefone não é <paramref name="clienteRecusadoId"/>.</summary>
    public static DecisaoVinculoTelefone Recusar(Guid empresaId, string telefoneHash, Guid clienteRecusadoId,
        Guid conversaOrigemId, Guid alteracaoVinculoId, Guid usuarioId, DateTime agora) =>
        Nova(empresaId, telefoneHash, clienteRecusadoId, null, conversaOrigemId, alteracaoVinculoId, usuarioId, agora);

    /// <summary>O telefone é <paramref name="clienteEscolhidoId"/> e, quando informado, não é <paramref name="clienteRecusadoId"/>.</summary>
    public static DecisaoVinculoTelefone Escolher(Guid empresaId, string telefoneHash, Guid clienteEscolhidoId, Guid? clienteRecusadoId,
        Guid conversaOrigemId, Guid alteracaoVinculoId, Guid usuarioId, DateTime agora)
    {
        if (clienteEscolhidoId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Informe o cliente escolhido.");
        var recusado = clienteRecusadoId == clienteEscolhidoId ? null : clienteRecusadoId;
        return Nova(empresaId, telefoneHash, recusado, clienteEscolhidoId, conversaOrigemId, alteracaoVinculoId, usuarioId, agora);
    }

    public void Revogar(Guid usuarioId, DateTime agora)
    {
        if (!Ativa) return;
        RevogadaEm = DateTime.SpecifyKind(agora, DateTimeKind.Utc);
        RevogadaPorUsuarioId = usuarioId;
    }

    private static DecisaoVinculoTelefone Nova(Guid empresaId, string telefoneHash, Guid? recusado, Guid? escolhido,
        Guid conversaOrigemId, Guid alteracaoVinculoId, Guid usuarioId, DateTime agora)
    {
        if (empresaId == Guid.Empty || conversaOrigemId == Guid.Empty || alteracaoVinculoId == Guid.Empty || usuarioId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Empresa, conversa, alteração e usuário são obrigatórios na decisão de vínculo.");
        if (string.IsNullOrWhiteSpace(telefoneHash) || telefoneHash.Length != 64)
            throw new RegraDeDominioVioladaException("O hash do telefone deve ter 64 caracteres.");
        if (recusado == Guid.Empty || recusado is null && escolhido is null)
            throw new RegraDeDominioVioladaException("A decisão precisa recusar ou escolher um cliente.");
        return new DecisaoVinculoTelefone
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            TelefoneHash = telefoneHash,
            ClienteRecusadoId = recusado,
            ClienteEscolhidoId = escolhido,
            ConversaOrigemId = conversaOrigemId,
            AlteracaoVinculoId = alteracaoVinculoId,
            UsuarioId = usuarioId,
            CriadaEm = DateTime.SpecifyKind(agora, DateTimeKind.Utc)
        };
    }
}
