using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.ListarUsuarios;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>#1753: o que a tela de Usuários e a de Auditoria leem.</summary>
public class GestaoDeUsuariosLeituraTests
{
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IAuditLogRepository _auditoria = Substitute.For<IAuditLogRepository>();
    private readonly Guid _empresaId = Guid.NewGuid();

    // Sem ninguém autenticado a política não libera ação nenhuma: estes testes leem só os dados da linha.
    private HierarquiaDeUsuarios Hierarquia() => new(_usuarios, Substitute.For<IUsuarioEmpresaRepository>(),
        Substitute.For<ICurrentUserAccessor>(), _auditoria, new FakeUnitOfWork(), Substitute.For<ILogger<HierarquiaDeUsuarios>>());

    private Usuario Usuario(string nome, bool vinculoAtivo, Perfil? perfil = null)
    {
        var usuario = Domain.Entities.Usuario.Criar(nome, $"{nome.ToLowerInvariant()}@casadababa.com", "$2a$11$hash");
        usuario.Empresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = _empresaId, Ativo = vinculoAtivo });
        if (perfil is not null)
            usuario.Perfis.Add(new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = _empresaId, PerfilId = perfil.Id, Perfil = perfil });
        return usuario;
    }

    [Fact]
    public async Task ListaMostraOAcessoDaEmpresaEOPerfil()
    {
        var cozinha = new Perfil { Id = Guid.NewGuid(), Nome = "Cozinha", Nivel = NivelAcesso.Operador, EmpresaId = _empresaId };
        var desativada = Usuario("Ana", vinculoAtivo: false, cozinha);
        var semPerfil = Usuario("Bia", vinculoAtivo: true);
        _usuarios.GetByEmpresaAsync(_empresaId, 1, 20).Returns((new[] { desativada, semPerfil }.AsEnumerable(), 2));

        var (lista, _) = await new ListarUsuariosUseCase(_usuarios, Hierarquia()).ExecuteAsync(new ListarUsuariosQuery(_empresaId));
        var linhas = lista.ToList();

        linhas[0].Ativo.Should().BeFalse("desativar desliga o vínculo, e é ele que a lista mostra");
        linhas[0].Perfil.Should().Be("Cozinha");
        linhas[0].PerfilId.Should().Be(cozinha.Id);
        linhas[1].Ativo.Should().BeTrue();
        linhas[1].PerfilId.Should().BeNull();
    }

    [Fact]
    public async Task PerfisAtribuiveisNuncaTrazemOSuporte()
    {
        _perfis.GetByEmpresaAsync(_empresaId).Returns(new[]
        {
            new Perfil { Id = Guid.NewGuid(), Nome = "Cozinha", Nivel = NivelAcesso.Operador, EmpresaId = _empresaId, ModuloInicial = "cozinha" },
            new Perfil { Id = Guid.NewGuid(), Nome = "Suporte", Nivel = NivelAcesso.SuperAdmin },
            new Perfil { Id = Guid.NewGuid(), Nome = "Dona", Nivel = NivelAcesso.Admin, EmpresaId = _empresaId },
        });

        var perfis = await new ListarPerfisAtribuiveisUseCase(_perfis, Hierarquia()).ExecuteAsync(_empresaId);

        perfis.Select(p => p.Nome).Should().Equal("Dona", "Cozinha");
    }

    [Fact]
    public async Task AuditoriaTrocaIdentificadorPeloNomeENaoVazaODeFora()
    {
        var dona = Usuario("Baba", vinculoAtivo: true);
        var deFora = Guid.NewGuid();
        IReadOnlyList<AuditoriaDeAcessoLinha> linhas =
        [
            new(Guid.NewGuid(), DateTime.UtcNow, "convite-reenviado", true, Guid.NewGuid(), "Ana", $"Convite reenviado por {dona.Id} (Admin); WhatsApp: nao"),
            new(Guid.NewGuid(), DateTime.UtcNow, "troca-de-email", true, Guid.NewGuid(), "Ana", $"Troca de e-mail solicitada por {deFora} (SuperAdmin)"),
            new(Guid.NewGuid(), DateTime.UtcNow, "login", true, Guid.NewGuid(), "Ana", "Login realizado com sucesso"),
        ];
        _auditoria.ListarPorEmpresaAsync(_empresaId, null, 1, 50, Arg.Any<CancellationToken>()).Returns((linhas, 3));
        _usuarios.GetByEmpresaAsync(_empresaId, 1, 500).Returns((new[] { dona }.AsEnumerable(), 1));

        var (itens, total) = await new ListarAuditoriaDeAcessoUseCase(_auditoria, _usuarios)
            .ExecuteAsync(new ListarAuditoriaDeAcessoQuery(_empresaId));

        total.Should().Be(3);
        itens[0].Detalhes.Should().Be("Convite reenviado por Baba (Admin); WhatsApp: nao");
        itens[1].Detalhes.Should().Be("Troca de e-mail solicitada por suporte (SuperAdmin)");
        itens[2].Detalhes.Should().Be("Login realizado com sucesso");
    }

    [Fact]
    public async Task AuditoriaLimitaAPagina()
    {
        _auditoria.ListarPorEmpresaAsync(_empresaId, null, 1, 100, Arg.Any<CancellationToken>())
            .Returns((Array.Empty<AuditoriaDeAcessoLinha>(), 0));

        await new ListarAuditoriaDeAcessoUseCase(_auditoria, _usuarios)
            .ExecuteAsync(new ListarAuditoriaDeAcessoQuery(_empresaId, null, Page: 0, PageSize: 5000));

        await _auditoria.Received(1).ListarPorEmpresaAsync(_empresaId, null, 1, 100, Arg.Any<CancellationToken>());
    }
}
