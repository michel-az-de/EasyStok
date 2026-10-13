namespace EasyStock.Domain.Entities.Atendimento;

public sealed class CotacaoLogistica
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmpresaId { get; set; }
    public string Provedor { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string SolicitacaoJson { get; set; } = "{}";
    public int ValorCentavos { get; set; }
    public int DuracaoMinutos { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime CriadoEm { get; set; }
    public bool Simulado { get; set; }
    public string? Indisponibilidade { get; set; }
}

public sealed class DespachoLogistica
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmpresaId { get; set; }
    public Guid CotacaoId { get; set; }
    public Guid ViagemId { get; set; }
    public Guid UsuarioId { get; set; }
    public string ChaveIdempotencia { get; set; } = "";
    public string Provedor { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string? RastreioUrl { get; set; }
    public string PedidoIdsJson { get; set; } = "[]";
    public string EventosJson { get; set; } = "[]";
    public string Status { get; private set; } = "agendado";
    public int ValorCentavos { get; set; }
    public bool Simulado { get; set; }
    public DateTime CriadoEm { get; set; }
    public Guid? MovimentoCaixaId { get; set; }
    public string? ChavePagamento { get; set; }
    public string? MeioPagamento { get; set; }
    public string? ReferenciaPagamento { get; set; }

    public void RegistrarEvento(string tipo, string? motivo)
    {
        var permitido = (Status, tipo) switch
        {
            ("agendado", "a_caminho" or "cancelado") => true,
            ("a_caminho", "entregue" or "falha" or "devolvendo") => true,
            ("falha", "a_caminho" or "devolvendo") => true,
            ("entregue", "devolvendo") => true,
            ("devolvendo", "devolvido") => true,
            _ => false
        };
        if (!permitido)
            throw new RegraDeDominioVioladaException($"Não é possível mudar de {Status} para {tipo}.");
        if (tipo is "falha" or "devolvendo" or "cancelado" && string.IsNullOrWhiteSpace(motivo))
            throw new RegraDeDominioVioladaException("Informe o motivo desta ocorrência.");
        Status = tipo;
    }
}

public sealed class PoliticaLogistica
{
    public Guid EmpresaId { get; set; }
    public bool Automatico { get; set; }
    public int TetoCentavos { get; set; } = 3000;
    public bool PermitirAgrupar { get; set; }
}
