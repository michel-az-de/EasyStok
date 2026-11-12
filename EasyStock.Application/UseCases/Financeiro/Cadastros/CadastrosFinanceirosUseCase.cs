using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.Cadastros;

public sealed record SalvarContaBancariaInput(Guid RequestId, string Nome, string Tipo, string Instituicao,
    string? Agencia, string Conta, string Titular, bool Ativo, long Revisao = 0, Guid? EmpresaId = null);
public sealed record ConfigurarMetodoPagamentoInput(Guid RequestId, bool Ativo, long Revisao, Guid? EmpresaId = null);
public sealed record MetodoPagamentoResult(string Codigo, string Nome, bool Ativo, long Revisao);

public sealed class CadastrosFinanceirosUseCase(ICadastrosFinanceirosRepository repo, IUnitOfWork uow, ICurrentUserAccessor usuario)
{
    public static bool PodeVer(ICurrentUserAccessor usuario) => usuario.IsAuthenticated && usuario.EmpresaId != Guid.Empty
        && usuario.TemPermissao(Permissao.AcessarModuloCaixa)
        && (usuario.TemPermissao(Permissao.VisualizarContasAPagar) || usuario.TemPermissao(Permissao.VisualizarContasAReceber));
    public static bool PodeGerenciar(ICurrentUserAccessor usuario) => PodeVer(usuario)
        && usuario.Nivel is NivelAcesso.SuperAdmin or NivelAcesso.Admin or NivelAcesso.Gerente
        && usuario.TemPermissao(Permissao.GerenciarCategoriasFinanceiras);

    private Guid Empresa(bool escrita, Guid? informada = null)
    {
        if (usuario.EmpresaId == Guid.Empty || (informada.HasValue && informada != usuario.EmpresaId))
            throw new UseCaseValidationException("Empresa da operação difere da sessão.");
        if (escrita ? !PodeGerenciar(usuario) : !PodeVer(usuario)) throw new UnauthorizedAccessException("Cadastro financeiro não disponível para seu perfil.");
        return usuario.EmpresaId;
    }

    public async Task<IReadOnlyList<ContaBancaria>> ListarContasAsync(CancellationToken ct)
    {
        var empresa = Empresa(false);
        return (await repo.ListarContasAsync(empresa, ct)).Where(c => c.EmpresaId == empresa).ToArray();
    }

    public async Task<ContaBancaria?> SalvarContaAsync(Guid? id, SalvarContaBancariaInput input, CancellationToken ct)
    {
        var empresa = Empresa(true, input.EmpresaId);
        var conta = await repo.ObterContaAsync(empresa, id ?? input.RequestId, ct);
        if (conta is not null && conta.EmpresaId != empresa) return null;
        if (conta is null)
        {
            if (id.HasValue) return null;
            conta = ContaBancaria.Criar(empresa, input.RequestId, input.Nome, input.Tipo, input.Instituicao,
                input.Agencia, input.Conta, input.Titular, input.Ativo, usuario.UsuarioId);
            repo.Adicionar(conta);
        }
        else conta.Atualizar(input.Revisao, input.RequestId, input.Nome, input.Tipo, input.Instituicao,
            input.Agencia, input.Conta, input.Titular, input.Ativo, usuario.UsuarioId);
        await uow.CommitAsync();
        return conta;
    }

    public async Task<ContaBancaria?> ObterContaAsync(Guid id, CancellationToken ct)
    {
        var empresa = Empresa(false);
        var conta = await repo.ObterContaAsync(empresa, id, ct);
        return conta?.EmpresaId == empresa ? conta : null;
    }

    public async Task<IReadOnlyList<MetodoPagamentoResult>> ListarMetodosAsync(CancellationToken ct)
    {
        // Quem recebe no Caixa precisa conhecer as formas disponíveis, sem acesso às contas bancárias.
        if (!usuario.IsAuthenticated || usuario.EmpresaId == Guid.Empty || !usuario.TemPermissao(Permissao.AcessarModuloCaixa))
            throw new UnauthorizedAccessException("Formas de pagamento não disponíveis para seu perfil.");
        var empresa = usuario.EmpresaId;
        var configurados = await repo.ListarMetodosAsync(empresa, ct);
        return MetodosPagamentoCaixa.Todos.Select(c => Resultado(c, configurados.SingleOrDefault(m => m.EmpresaId == empresa && m.Codigo == c))).ToArray();
    }

    public async Task<MetodoPagamentoResult> ConfigurarMetodoAsync(string codigo, ConfigurarMetodoPagamentoInput input, CancellationToken ct)
    {
        var empresa = Empresa(true, input.EmpresaId);
        if (!MetodosPagamentoCaixa.Todos.Contains(codigo)) throw new UseCaseValidationException("Método de pagamento inválido.");
        var metodo = await repo.ObterMetodoAsync(empresa, codigo, ct);
        if (metodo is not null && metodo.EmpresaId != empresa) throw new UseCaseValidationException("Método de outra empresa.");
        if (metodo is null)
        {
            metodo = MetodoPagamentoEmpresa.Criar(empresa, codigo);
            repo.Adicionar(metodo);
        }
        metodo.Configurar(input.Revisao, input.RequestId, input.Ativo, usuario.UsuarioId);
        await uow.CommitAsync();
        return Resultado(codigo, metodo);
    }

    private static MetodoPagamentoResult Resultado(string codigo, MetodoPagamentoEmpresa? metodo) => new(codigo,
        codigo switch { "credito" => "Cartão de crédito", "debito" => "Cartão de débito", "transferencia" => "Transferência", "dinheiro" => "Dinheiro", "pix" => "Pix", _ => "Outro" },
        metodo?.Ativo ?? true, metodo?.Revisao ?? 0);
}
