namespace EasyStock.Domain.Services
{
    /// <summary>
    /// Decide se um ticket pode ser escalonado automaticamente.
    /// Regras conservadoras: cooldown 6h, nao escala N3, respeita
    /// horario comercial se configurado.
    /// </summary>
    public sealed class EscalonamentoPolicy
    {
        public static readonly TimeSpan CooldownPadrao = TimeSpan.FromHours(6);

        public EscalonamentoDecisao Decidir(
            AdminTicket ticket,
            DateTime agoraUtc,
            EscalonamentoConfig? config = null)
        {
            if (ticket is null) throw new ArgumentNullException(nameof(ticket));

            config ??= EscalonamentoConfig.Padrao();

            if (ticket.Nivel >= NivelAtendimento.N3)
                return EscalonamentoDecisao.NaoEscalar("Ticket ja esta no nivel maximo (N3).");

            if (ticket.Status == TicketStatus.Resolvido || ticket.Status == TicketStatus.Fechado)
                return EscalonamentoDecisao.NaoEscalar("Ticket nao esta mais ativo.");

            // SLA viola = pre-condicao
            var slaResolucaoEstourada = ticket.PrazoResolucao.HasValue && agoraUtc > ticket.PrazoResolucao.Value;
            var slaRespostaEstourada = ticket.PrazoResposta.HasValue
                && agoraUtc > ticket.PrazoResposta.Value
                && ticket.PrimeiraRespostaEm is null;

            if (!slaResolucaoEstourada && !slaRespostaEstourada)
                return EscalonamentoDecisao.NaoEscalar("Nenhum SLA violado.");

            // cooldown apos ultimo escalonamento (usa AlteradoEm como proxy
            // ate termos coluna dedicada)
            if (agoraUtc - ticket.AlteradoEm < config.Cooldown)
                return EscalonamentoDecisao.NaoEscalar("Em cooldown.");

            // horario comercial (opcional)
            if (config.SomenteHorarioComercial)
            {
                var horaLocal = agoraUtc.AddHours(config.OffsetUtc).Hour;
                if (horaLocal < config.HoraInicioComercial || horaLocal >= config.HoraFimComercial)
                    return EscalonamentoDecisao.NaoEscalar("Fora de horario comercial.");
            }

            var proximo = ticket.Nivel switch
            {
                NivelAtendimento.N1 => NivelAtendimento.N2,
                NivelAtendimento.N2 => NivelAtendimento.N3,
                _ => NivelAtendimento.N3
            };

            var motivo = slaResolucaoEstourada ? "SLA de resolucao violado" : "SLA de resposta violado";
            return EscalonamentoDecisao.Escalar(proximo, motivo);
        }
    }

    public sealed record EscalonamentoDecisao(bool DeveEscalar, NivelAtendimento? ProximoNivel, string Motivo)
    {
        public static EscalonamentoDecisao NaoEscalar(string motivo) => new(false, null, motivo);
        public static EscalonamentoDecisao Escalar(NivelAtendimento nivel, string motivo) => new(true, nivel, motivo);
    }

    public sealed record EscalonamentoConfig(
        TimeSpan Cooldown,
        bool SomenteHorarioComercial,
        int HoraInicioComercial,
        int HoraFimComercial,
        int OffsetUtc)
    {
        public static EscalonamentoConfig Padrao() => new(EscalonamentoPolicy.CooldownPadrao, false, 9, 18, -3);
    }
}
