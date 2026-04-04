using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities
{
    public class Notificacao
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public TipoAlertaEstoque TipoAlerta { get; set; }
        public string Mensagem { get; set; } = null!;
        public bool Lida { get; set; }
        public Guid? ReferenciaId { get; set; }
        public DateTime CriadaEm { get; set; }
        public DateTime? LidaEm { get; set; }

        public Empresa? Empresa { get; set; }

        public static Notificacao Criar(Guid empresaId, TipoAlertaEstoque tipo, string mensagem, Guid? referenciaId = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                TipoAlerta = tipo,
                Mensagem = mensagem,
                Lida = false,
                ReferenciaId = referenciaId,
                CriadaEm = DateTime.UtcNow
            };

        public void MarcarComoLida()
        {
            Lida = true;
            LidaEm = DateTime.UtcNow;
        }
    }
}
