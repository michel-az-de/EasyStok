using EasyStock.Domain.Enums.Campanhas;

namespace EasyStock.Domain.Entities.Campanhas;

/// <summary>
/// Um cliente dentro de uma campanha (S28), único por <c>(CampanhaId, ClienteId)</c>. Nasce
/// <see cref="StatusCampanhaDestinatario.Pendente"/> no cálculo do público (S29), que também o exclui
/// com motivo; o disparo (S30) o enfileira no outbox de notificações e acompanha o envio. O índice
/// <c>(ClienteId, EnviadoEm)</c> sustenta o limite de uma campanha por semana por cliente.
/// </summary>
public class CampanhaDestinatario
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid CampanhaId { get; private set; }
    public Guid ClienteId { get; private set; }

    /// <summary>Onda em que saiu; 0 enquanto pendente ou excluído.</summary>
    public int Onda { get; private set; }

    public StatusCampanhaDestinatario Status { get; private set; }

    /// <summary><see cref="MotivoExclusaoCampanha"/> quando <see cref="StatusCampanhaDestinatario.Excluido"/>.</summary>
    public string? MotivoExclusao { get; private set; }

    /// <summary><c>OutboxMensagemNotificacao</c> que leva a mensagem (sem FK: o outbox é expurgado).</summary>
    public Guid? OutboxMensagemId { get; private set; }

    public DateTime? EnviadoEm { get; private set; }

    /// <summary>Pedido atribuído à campanha (S30).</summary>
    public Guid? PedidoId { get; private set; }

    // EF Core ctor sem parâmetros
    private CampanhaDestinatario() { }

    public static CampanhaDestinatario Criar(Campanha campanha, Guid clienteId)
    {
        ArgumentNullException.ThrowIfNull(campanha);
        if (clienteId == Guid.Empty) throw new RegraDeDominioVioladaException("ClienteId é obrigatório.");

        return new CampanhaDestinatario
        {
            Id = Guid.NewGuid(),
            EmpresaId = campanha.EmpresaId,
            CampanhaId = campanha.Id,
            ClienteId = clienteId,
            Onda = 0,
            Status = StatusCampanhaDestinatario.Pendente,
        };
    }

    public void Excluir(string motivo)
    {
        if (!MotivoExclusaoCampanha.EhValido(motivo))
            throw new RegraDeDominioVioladaException($"Motivo de exclusão inválido: {motivo}.");
        GarantirPendente();
        Status = StatusCampanhaDestinatario.Excluido;
        MotivoExclusao = motivo;
    }

    /// <summary>Ainda não saiu: o cálculo do público (S29) pode refazê-lo ou tirá-lo da campanha.</summary>
    public bool Recalculavel => Status is StatusCampanhaDestinatario.Pendente or StatusCampanhaDestinatario.Excluido;

    /// <summary>
    /// Recálculo do público (S29): volta a <see cref="StatusCampanhaDestinatario.Pendente"/> sem motivo
    /// ou passa a <see cref="StatusCampanhaDestinatario.Excluido"/> com o motivo novo. Quem já foi
    /// enfileirado ou enviado não muda.
    /// </summary>
    public void Reclassificar(string? motivo)
    {
        if (!Recalculavel)
            throw new RegraDeDominioVioladaException($"Destinatário {Status} já saiu e não é recalculado.");
        if (motivo is not null && !MotivoExclusaoCampanha.EhValido(motivo))
            throw new RegraDeDominioVioladaException($"Motivo de exclusão inválido: {motivo}.");
        Status = StatusCampanhaDestinatario.Pendente;
        MotivoExclusao = null;
        if (motivo is not null) Excluir(motivo);
    }

    public void Enfileirar(int onda, Guid outboxMensagemId)
    {
        if (onda <= 0) throw new RegraDeDominioVioladaException("Onda precisa ser maior que zero.");
        if (outboxMensagemId == Guid.Empty) throw new RegraDeDominioVioladaException("Mensagem do outbox é obrigatória.");
        GarantirPendente();
        Onda = onda;
        OutboxMensagemId = outboxMensagemId;
        Status = StatusCampanhaDestinatario.Enfileirado;
    }

    public void MarcarEnviado(DateTime em)
    {
        if (Status != StatusCampanhaDestinatario.Enfileirado)
            throw new RegraDeDominioVioladaException($"Destinatário {Status} não pode ser marcado como enviado.");
        Status = StatusCampanhaDestinatario.Enviado;
        EnviadoEm = Campanha.Utc(em);
    }

    private void GarantirPendente()
    {
        if (Status != StatusCampanhaDestinatario.Pendente)
            throw new RegraDeDominioVioladaException($"Destinatário {Status} não está mais pendente.");
    }
}
