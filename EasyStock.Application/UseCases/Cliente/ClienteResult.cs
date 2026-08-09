namespace EasyStock.Application.UseCases.Cliente;

/// <summary>DTO de retorno de Use cases de Cliente.</summary>
public sealed record ClienteResult(
    Guid Id,
    Guid EmpresaId,
    string Nome,
    string? Apt,
    string? Endereco,
    string? Telefone,
    string? Email,
    string? Documento,
    string? Observacoes,
    int OrderCount,
    DateTime? LastOrderAt,
    bool Ativo,
    DateTime CriadoEm,
    DateTime AlteradoEm,
    // Pessoa jurídica (ADR-0048): no fim e com default, porque este record é posicional —
    // inserir no meio quebraria o Map e todos os call-sites de uma vez.
    string TipoPessoa = "fisica",
    string? NomeFantasia = null,
    string? InscricaoEstadual = null
);

/// <summary>Cliente com sub-coleções (pra tela de detalhe rica).</summary>
public sealed record ClienteDetalheResult(
    ClienteResult Cliente,
    IReadOnlyList<ClienteEnderecoResult> Enderecos,
    IReadOnlyList<ClienteTelefoneResult> Telefones,
    IReadOnlyList<ClienteDocumentoResult> Documentos,
    IReadOnlyList<ClienteAlteracaoResult> Alteracoes
);

public sealed record ClienteEnderecoResult(
    Guid Id, Guid ClienteId, string? Tipo,
    string? Logradouro, string? Numero, string? Complemento,
    string? Bairro, string? Cidade, string? Estado, string? Cep, string? Pais,
    string? Referencia, bool Padrao, DateTime CriadoEm
);

public sealed record ClienteTelefoneResult(
    Guid Id, Guid ClienteId, string? Tipo, string Numero,
    bool Whatsapp, bool Principal, string? Observacao, DateTime CriadoEm
);

public sealed record ClienteDocumentoResult(
    Guid Id, Guid ClienteId, string Tipo, string Valor,
    string? Emissor, DateTime? EmitidoEm, DateTime? ValidoAte,
    bool Principal, DateTime CriadoEm
);

public sealed record ClienteAlteracaoResult(
    Guid Id, Guid ClienteId, Guid? AlteradoPorUserId, string? AlteradoPorNome,
    string Campo, string? ValorAntigo, string? ValorNovo,
    DateTime AlteradoEm, string? Origem
);
