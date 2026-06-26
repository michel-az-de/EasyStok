namespace EasyStock.Domain.Entities
{
    public class AssinaturaEmpresa
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid PlanoId { get; set; }
        public DateTime DataInicio { get; set; }
        public DateTime? DataFim { get; set; }
        public StatusAssinatura Status { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        // Trial
        public DateTime? TrialFim { get; set; }
        public bool TrialAtivo => TrialFim.HasValue && TrialFim.Value > DateTime.UtcNow;

        // Coupon snapshot
        public string? CupomCodigo { get; set; }
        public decimal? DescontoAplicado { get; set; }

        public DateTime? SuspensaEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Plano? Plano { get; set; }

        public void Suspender()
        {
            if (Status == StatusAssinatura.Suspensa)
                return;
            if (Status == StatusAssinatura.Cancelada)
                throw new RegraDeDominioVioladaException("Assinatura cancelada nao pode ser suspensa.");
            Status = StatusAssinatura.Suspensa;
            SuspensaEm ??= DateTime.UtcNow;
            AlteradoEm = DateTime.UtcNow;
        }

        public void Cancelar(DateTime? dataFimEfetiva = null)
        {
            if (Status == StatusAssinatura.Cancelada)
                return;
            DataFim = dataFimEfetiva ?? DateTime.UtcNow;
            Status = StatusAssinatura.Cancelada;
            AlteradoEm = DateTime.UtcNow;
        }

        public void Reativar()
        {
            if (Status == StatusAssinatura.Cancelada)
                throw new RegraDeDominioVioladaException("Assinatura cancelada nao pode ser reativada. Crie uma nova assinatura.");
            DataFim = null;
            Status = StatusAssinatura.Ativa;
            SuspensaEm = null;
            AlteradoEm = DateTime.UtcNow;
        }

        public void AtivarTrial(int dias)
        {
            TrialFim = DateTime.UtcNow.AddDays(dias);
            AlteradoEm = DateTime.UtcNow;
        }

        /// <summary>
        /// Transiciona um teste (trial) vencido sem plano pago vigente para
        /// <see cref="StatusAssinatura.Expirada"/>. Idempotente: só age sobre Ativa.
        /// O SubscriptionGate ja bloqueia o acesso; isto alinha o status persistido
        /// (issue 694). Distinto de <see cref="Suspender"/>, que e a trilha de
        /// inadimplencia de plano PAGO (dunning + cancelamento).
        /// </summary>
        public void ExpirarPorTrial()
        {
            if (Status != StatusAssinatura.Ativa)
                return;
            Status = StatusAssinatura.Expirada;
            AlteradoEm = DateTime.UtcNow;
        }

        public void AplicarCupom(Cupom cupom)
        {
            CupomCodigo = cupom.Codigo;
            DescontoAplicado = cupom.Valor;
            cupom.IncrementarUso();
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
