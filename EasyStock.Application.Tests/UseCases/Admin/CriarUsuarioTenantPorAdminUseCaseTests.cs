using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Admin.CriarUsuarioTenantPorAdmin;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Admin;

// #1159: a request do SuperAdmin não tem empresa no token; sem o tenant da empresa alvo,
// o RLS recusa o INSERT de Perfil/UsuarioEmpresa (42501 em "perfis").
public class CriarUsuarioTenantPorAdminUseCaseTests
{
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IUsuarioEmpresaRepository _usuarioEmpresas = Substitute.For<IUsuarioEmpresaRepository>();
    private readonly IUsuarioPerfilRepository _usuarioPerfis = Substitute.For<IUsuarioPerfilRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();

    private CriarUsuarioTenantPorAdminUseCase Sut() => new(
        _usuarios, _empresas, _perfis, _usuarioEmpresas, _usuarioPerfis, _hasher, _uow, _tenant,
        null, NullLogger<CriarUsuarioTenantPorAdminUseCase>.Instance);

    [Fact]
    public async Task DefineOTenantDaEmpresaAlvoAntesDeGravar()
    {
        var empresa = Empresa.Criar("Demonstração", "00000000000191");
        _empresas.GetByIdAsync(empresa.Id).Returns(empresa);
        _perfis.GetPadroesAsync().Returns([]);
        _hasher.Hash(Arg.Any<string>()).Returns("hash");

        await Sut().ExecuteAsync(new CriarUsuarioTenantPorAdminCommand(
            empresa.Id, "Operador Teste", "operador@exemplo.com", NivelAcesso.Operador, EnviarEmail: false));

        Received.InOrder(() =>
        {
            _tenant.SetCurrentTenant(empresa.Id);
            _perfis.AddAsync(Arg.Any<Perfil>());
            _uow.CommitAsync();
        });
    }
}
