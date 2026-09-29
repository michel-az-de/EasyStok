using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Usuários da empresa para a atribuição de conversa (S41) em Postgres real: com o tenant ligado, o
/// vínculo a um perfil padrão (EmpresaId nulo, escondido pelo filtro e pela RLS) ainda aparece, e só
/// entram vínculos ativos desta empresa.
/// </summary>
public class AtendenteRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task ListaUsuariosAtivosComPerfilPadraoEDaEmpresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Casa da Baba Atendentes", "11111111000191");
        var outra = Empresa.Criar("Outra Atendentes", "22222222000191");
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var ana = Usuario.Criar("Ana", $"ana-{sufixo}@x.com", "hash");
        var leo = Usuario.Criar("Leo", $"leo-{sufixo}@x.com", "hash");
        var inativo = Usuario.Criar("Ivo", $"ivo-{sufixo}@x.com", "hash");
        var deFora = Usuario.Criar("Fora", $"fora-{sufixo}@x.com", "hash");
        var padraoOperador = new Perfil { Id = Guid.NewGuid(), EmpresaId = null, Nome = $"Operador-{sufixo}", Nivel = NivelAcesso.Operador, CriadoEm = agora };
        var visualizadorDaEmpresa = new Perfil { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Leitura", Nivel = NivelAcesso.Visualizador, CriadoEm = agora };
        visualizadorDaEmpresa.Permissoes.Add(new PerfilPermissao { Id = Guid.NewGuid(), PerfilId = visualizadorDaEmpresa.Id, Permissao = Permissao.VisualizarRelatorios });

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(empresa, outra);
            db.Usuarios.AddRange(ana, leo, inativo, deFora);
            db.Perfis.AddRange(padraoOperador, visualizadorDaEmpresa);
            db.UsuariosEmpresas.AddRange(
                Vinculo(ana, empresa, ativo: true, agora),
                Vinculo(leo, empresa, ativo: true, agora),
                Vinculo(inativo, empresa, ativo: false, agora),
                Vinculo(deFora, outra, ativo: true, agora));
            db.UsuariosPerfis.AddRange(
                Perfil(ana, empresa, padraoOperador, agora),
                Perfil(leo, empresa, visualizadorDaEmpresa, agora),
                Perfil(deFora, outra, padraoOperador, agora));
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresa.Id);
        var repo = new AtendenteRepository(leitura);

        var usuarios = await repo.ListarUsuariosAtivosAsync(empresa.Id);

        usuarios.Select(u => u.Nome).Should().BeEquivalentTo(["Ana", "Leo"]);
        usuarios.Single(u => u.Nome == "Ana").Perfis.Should().ContainSingle(p => p.Nivel == NivelAcesso.Operador && p.Permissoes.Count == 0);
        usuarios.Single(u => u.Nome == "Leo").Perfis.Should().ContainSingle(p => p.Permissoes.Contains(Permissao.VisualizarRelatorios));
        (await repo.ObterUsuarioAtivoAsync(empresa.Id, inativo.Id)).Should().BeNull();
        (await repo.ObterUsuarioAtivoAsync(empresa.Id, deFora.Id)).Should().BeNull();
        (await repo.ObterUsuarioAtivoAsync(empresa.Id, ana.Id))!.Nome.Should().Be("Ana");
    }

    private static UsuarioEmpresa Vinculo(Usuario u, Empresa e, bool ativo, DateTime agora) =>
        new() { Id = Guid.NewGuid(), UsuarioId = u.Id, EmpresaId = e.Id, Ativo = ativo, CriadoEm = agora };

    private static UsuarioPerfil Perfil(Usuario u, Empresa e, Perfil p, DateTime agora) =>
        new() { Id = Guid.NewGuid(), UsuarioId = u.Id, EmpresaId = e.Id, PerfilId = p.Id, AtribuidoEm = agora };
}
