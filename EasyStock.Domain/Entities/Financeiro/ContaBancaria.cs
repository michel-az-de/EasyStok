namespace EasyStock.Domain.Entities.Financeiro;

public sealed class ContaBancaria
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string Nome { get; private set; } = "";
    public string Tipo { get; private set; } = "";
    public string Instituicao { get; private set; } = "";
    public string Agencia { get; private set; } = "";
    public string Conta { get; private set; } = "";
    public string Titular { get; private set; } = "";
    public bool Ativo { get; private set; }
    public long Revisao { get; private set; }
    public Guid UltimaOperacaoId { get; private set; }
    public Guid AlteradoPor { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    public static ContaBancaria Criar(Guid empresaId, Guid requestId, string nome, string tipo,
        string instituicao, string? agencia, string conta, string titular, bool ativo, Guid ator)
    {
        if (empresaId == Guid.Empty) throw new ArgumentException("Empresa obrigatória.");
        var nova = new ContaBancaria { Id = requestId, EmpresaId = empresaId };
        nova.Atualizar(0, requestId, nome, tipo, instituicao, agencia, conta, titular, ativo, ator);
        return nova;
    }

    public void Atualizar(long revisao, Guid requestId, string nome, string tipo, string instituicao,
        string? agencia, string conta, string titular, bool ativo, Guid ator)
    {
        if (requestId == Guid.Empty || ator == Guid.Empty) throw new ArgumentException("Identificação da operação e responsável obrigatórios.");
        nome = Texto(nome, 150, "Nome");
        instituicao = Texto(instituicao, 150, "Instituição");
        agencia = Texto(agencia, 30, "Agência", false);
        conta = Texto(conta, 50, "Conta");
        titular = Texto(titular, 150, "Titular");
        if (tipo is not ("corrente" or "poupanca" or "pagamento")) throw new ArgumentException("Tipo de conta inválido.");
        if (UltimaOperacaoId == requestId)
        {
            if (Nome == nome && Tipo == tipo && Instituicao == instituicao && Agencia == agencia && Conta == conta && Titular == titular && Ativo == ativo) return;
            throw new InvalidOperationException("Esta operação já foi utilizada com outros dados.");
        }
        if (Revisao != revisao) throw new InvalidOperationException("A conta foi alterada. Reabra o cadastro para conferir.");
        Nome = nome; Tipo = tipo; Instituicao = instituicao; Agencia = agencia; Conta = conta; Titular = titular; Ativo = ativo;
        UltimaOperacaoId = requestId; AlteradoPor = ator; AlteradoEm = DateTime.UtcNow; Revisao++;
    }

    private static string Texto(string? valor, int limite, string campo, bool obrigatorio = true)
    {
        var texto = valor?.Trim() ?? "";
        if ((obrigatorio && texto.Length == 0) || texto.Length > limite || texto.Contains('<') || texto.Contains('>'))
            throw new ArgumentException($"{campo} inválido. Informe até {limite} caracteres, sem HTML.");
        return texto;
    }
}
