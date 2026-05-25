namespace EasyStock.Web.Models.Api;

public record Cliente
{
    public required string Id { get; init; }
    public Guid EmpresaId { get; init; }
    public required string Nome { get; init; }
    public string? Apt { get; init; }
    public string? Endereco { get; init; }
    public string? Telefone { get; init; }
    public string? Email { get; init; }
    public string? Documento { get; init; }
    public string? Observacoes { get; init; }
    public int OrderCount { get; init; }
    public DateTime? LastOrderAt { get; init; }
    public bool Ativo { get; init; }
    public DateTime CriadoEm { get; init; }
    public DateTime AlteradoEm { get; init; }
    public string Status => Ativo ? "ativo" : "inativo";
}

public record ClienteDetalhe
{
    public required Cliente Cliente { get; init; }
    public List<ClienteEndereco> Enderecos { get; init; } = new();
    public List<ClienteTelefone> Telefones { get; init; } = new();
    public List<ClienteDocumento> Documentos { get; init; } = new();
    public List<ClienteAlteracao> Alteracoes { get; init; } = new();
}

public record ClienteEndereco(
    string Id, string ClienteId, string? Tipo,
    string? Logradouro, string? Numero, string? Complemento,
    string? Bairro, string? Cidade, string? Estado, string? Cep, string? Pais,
    string? Referencia, bool Padrao, DateTime CriadoEm);

public record ClienteTelefone(
    string Id, string ClienteId, string? Tipo, string Numero,
    bool Whatsapp, bool Principal, string? Observacao, DateTime CriadoEm);

public record ClienteDocumento(
    string Id, string ClienteId, string Tipo, string Valor,
    string? Emissor, DateTime? EmitidoEm, DateTime? ValidoAte,
    bool Principal, DateTime CriadoEm);

public record ClienteAlteracao(
    string Id, string ClienteId, string? AlteradoPorUserId, string? AlteradoPorNome,
    string Campo, string? ValorAntigo, string? ValorNovo,
    DateTime AlteradoEm, string? Origem);

public record MobileClienteSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Apt { get; init; }
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public int OrderCount { get; init; }
    public DateTime LastOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public Guid? EmpresaId { get; init; }
    public Guid? LojaId { get; init; }
    public Guid? ErpClienteId { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? LastDeviceId { get; init; }
    public string? LastOperatorName { get; init; }
    public bool Linked => ErpClienteId.HasValue && ErpClienteId.Value != Guid.Empty;
}
