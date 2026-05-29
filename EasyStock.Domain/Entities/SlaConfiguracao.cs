namespace EasyStock.Domain.Entities
{
    public class SlaConfiguracao
    {
        public Guid Id { get; set; }
        public Guid? EmpresaId { get; set; }
        public Guid? PlanoId { get; set; }
        public TicketPrioridade Prioridade { get; set; }
        public int MinutosResposta { get; set; }
        public int MinutosResolucao { get; set; }
        public bool HorarioComercialApenas { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Plano? Plano { get; set; }

        public static SlaConfiguracao Criar(
            TicketPrioridade prioridade,
            int minutosResposta,
            int minutosResolucao,
            Guid? empresaId = null,
            Guid? planoId = null,
            bool horarioComercialApenas = false)
        {
            var agora = DateTime.UtcNow;
            return new SlaConfiguracao
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                PlanoId = planoId,
                Prioridade = prioridade,
                MinutosResposta = minutosResposta,
                MinutosResolucao = minutosResolucao,
                HorarioComercialApenas = horarioComercialApenas,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }
    }
}
