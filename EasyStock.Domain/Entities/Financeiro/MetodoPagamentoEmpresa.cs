using EasyStock.Domain.Financeiro;

namespace EasyStock.Domain.Entities.Financeiro;

public sealed class MetodoPagamentoEmpresa
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string Codigo { get; private set; } = "";
    public bool Ativo { get; private set; } = true;
    public long Revisao { get; private set; }
    public Guid UltimaOperacaoId { get; private set; }
    public Guid AlteradoPor { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    public static MetodoPagamentoEmpresa Criar(Guid empresaId, string codigo)
    {
        if (empresaId == Guid.Empty || !MetodosPagamentoCaixa.Todos.Contains(codigo))
            throw new ArgumentException("Empresa ou método de pagamento inválido.");
        return new() { Id = Guid.NewGuid(), EmpresaId = empresaId, Codigo = codigo };
    }

    public void Configurar(long revisao, Guid requestId, bool ativo, Guid ator)
    {
        if (requestId == Guid.Empty || ator == Guid.Empty) throw new ArgumentException("Identificação da operação e responsável obrigatórios.");
        if (UltimaOperacaoId == requestId)
        {
            if (Ativo == ativo) return;
            throw new InvalidOperationException("Esta operação já foi utilizada com outros dados.");
        }
        if (Revisao != revisao) throw new InvalidOperationException("O método foi alterado. Reabra o cadastro para conferir.");
        Ativo = ativo; UltimaOperacaoId = requestId; AlteradoPor = ator; AlteradoEm = DateTime.UtcNow; Revisao++;
    }
}
