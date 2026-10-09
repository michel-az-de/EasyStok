using EasyStock.Api.Data;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.UnitTests.Authorization;

public class PerfisCasaDaBabaSeedTests
{
    [Fact]
    public async Task ConsultaDoRefresh_CarregaPermissoesExplicitasEEntradaSemJwt()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var usuario = Usuario.Criar("Cozinha teste", "cozinha@teste.local", "hash-apenas-fixture");
        using (var db = new EasyStockDbContext(options))
        {
            var empresa = Empresa.Criar("Loja teste", null);
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            await PerfisCasaDaBabaSeed.ExecutarAsync(db, empresa.Id);
            var perfil = await db.Perfis.IgnoreQueryFilters().SingleAsync(p => p.Nome == "Cozinha");
            db.Usuarios.Add(usuario);
            db.UsuariosEmpresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuario.Id,
                EmpresaId = empresa.Id, Ativo = true, CriadoEm = DateTime.UtcNow });
            db.UsuariosPerfis.Add(new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = usuario.Id,
                EmpresaId = empresa.Id, PerfilId = perfil.Id, AtribuidoEm = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        using var leitura = new EasyStockDbContext(options);
        var resultado = await new UsuarioRepository(leitura).GetByIdAsync(usuario.Id);
        var carregado = resultado!.Perfis.Single().Perfil!;
        carregado.ModuloInicial.Should().Be("cozinha");
        carregado.Permissoes.Select(p => p.Permissao).Should().BeEquivalentTo(
            new[] { Permissao.AcessarModuloProducao, Permissao.AcessarModuloCozinha, Permissao.GerenciarEstoque });
    }

    [Fact]
    public async Task DuasExecucoes_NaoDuplicamNemAlteramPerfilExistenteOuOutraEmpresa()
    {
        using var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var bypass = db.UseRowLevelSecurityBypass();
        var empresa = Empresa.Criar("Casa da Baba teste", null);
        var outra = Empresa.Criar("Outra empresa", null);
        db.Empresas.AddRange(empresa, outra);
        var personalizado = new Perfil { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Atendimento",
            Nivel = NivelAcesso.Gerente, ModuloInicial = "financeiro", CriadoEm = DateTime.UtcNow };
        db.Perfis.Add(personalizado);
        await db.SaveChangesAsync();
        await PerfisCasaDaBabaSeed.ExecutarAsync(db, empresa.Id);
        await PerfisCasaDaBabaSeed.ExecutarAsync(db, empresa.Id);
        var perfis = await db.Perfis.IgnoreQueryFilters().Include(p => p.Permissoes).ToListAsync();
        perfis.Should().HaveCount(3).And.OnlyContain(p => p.EmpresaId == empresa.Id);
        personalizado.Nivel.Should().Be(NivelAcesso.Gerente);
        personalizado.ModuloInicial.Should().Be("financeiro");
        personalizado.Permissoes.Should().BeEmpty();
        perfis.Single(p => p.Nome == "Dona").Permissoes.Should().HaveCount(19);
        perfis.Single(p => p.Nome == "Cozinha").Permissoes.Should().HaveCount(3);
        (await db.Usuarios.CountAsync()).Should().Be(0);
    }
}
