using EasyStock.Application.UseCases.Cliente;
using EasyStock.Application.UseCases.CriarCliente;

namespace EasyStock.Application.UseCases.ObterClienteDetalhes;

public sealed record ObterClienteDetalhesQuery(Guid EmpresaId, Guid Id, int MaxAlteracoes = 100);

public class ObterClienteDetalhesUseCase(IClienteRepository repo)
{
    public async Task<ClienteDetalheResult?> ExecuteAsync(ObterClienteDetalhesQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(q.Id, "Id");

        var c = await repo.GetByIdWithDetailsAsync(q.EmpresaId, q.Id);
        if (c == null) return null;

        var alteracoes = await repo.GetAlteracoesAsync(c.Id, q.MaxAlteracoes);

        return new ClienteDetalheResult(
            CriarClienteUseCase.Map(c),
            c.Enderecos.Select(e => new ClienteEnderecoResult(
                e.Id, e.ClienteId, e.Tipo, e.Logradouro, e.Numero, e.Complemento,
                e.Bairro, e.Cidade, e.Estado, e.Cep, e.Pais, e.Referencia,
                e.Padrao, e.CriadoEm)).ToList(),
            c.Telefones.Select(t => new ClienteTelefoneResult(
                t.Id, t.ClienteId, t.Tipo, t.Numero, t.Whatsapp, t.Principal,
                t.Observacao, t.CriadoEm)).ToList(),
            c.Documentos.Select(d => new ClienteDocumentoResult(
                d.Id, d.ClienteId, d.Tipo, d.Valor, d.Emissor, d.EmitidoEm,
                d.ValidoAte, d.Principal, d.CriadoEm)).ToList(),
            alteracoes.Select(a => new ClienteAlteracaoResult(
                a.Id, a.ClienteId, a.AlteradoPorUserId, a.AlteradoPorNome,
                a.Campo, a.ValorAntigo, a.ValorNovo, a.AlteradoEm, a.Origem)).ToList()
        );
    }
}
