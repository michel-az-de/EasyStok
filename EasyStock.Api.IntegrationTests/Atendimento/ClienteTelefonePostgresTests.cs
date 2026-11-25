using DotNet.Testcontainers.Builders;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>
/// #3341: a busca do cliente pelo telefone casa em qualquer grafia do cadastro (DDD + 8 últimos dígitos) e o
/// desempate decide em SQL. O teste unitário roda no provedor em memória; aqui o mesmo predicado e a mesma
/// ordenação são traduzidos pelo Npgsql, que é o que roda em produção.
/// </summary>
public sealed class ClienteTelefonePostgresTests : IAsyncLifetime
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private BancoDeTeste? _pg;
    private bool _isAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new BancoDeTeste();
            await _pg.StartAsync();
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    [SkippableFact]
    public async Task AchaEmQualquerGrafiaEPrefereNumeroIdenticoEOCadastroMaisAntigo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var db = NovoContexto();
        var agora = DateTime.UtcNow;
        SemearEmpresa(db, agora);
        var recente = Cadastro("Recente", "11 99757-3992", agora);
        var antigo = Cadastro("Antigo", "(11) 99757-3992", agora.AddDays(-30));
        var identico = Cadastro("Identico", "1133334444", agora.AddDays(-1), outroNumero: "+5511997573992");
        var fixo = Cadastro("Fixo", "1133334444", agora.AddDays(-60));
        var limpo = Cadastro("Limpo", "11997573992", agora.AddDays(-2));
        db.Clientes.AddRange(recente, antigo, identico, fixo, limpo);
        await db.SaveChangesAsync();
        var repo = new ClienteRepository(db);

        (await repo.FindByTelefoneAsync(EmpresaId, "+5511997573992"))!.Id
            .Should().Be(identico.Id, "o número idêntico na lista vence outra grafia no telefone principal");

        db.Remove(identico);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await repo.FindByTelefoneAsync(EmpresaId, "+5511997573992"))!.Id
            .Should().Be(limpo.Id, "a grafia só de dígitos vence a mascarada, mesmo mais antiga");

        db.ChangeTracker.Clear();
        db.Remove(limpo);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await repo.FindByTelefoneAsync(EmpresaId, "+5511997573992"))!.Id
            .Should().Be(antigo.Id, "entre duas grafias mascaradas vence o cadastro mais antigo");
        (await repo.FindByTelefoneAsync(EmpresaId, "+551133334444"))!.Id
            .Should().Be(fixo.Id, "telefone fixo casa com fixo");
        (await repo.FindByTelefoneAsync(EmpresaId, "+5521997573992")).Should().BeNull("DDD diferente é outra pessoa");
    }

    // Revisão da #3350 (A2): a trava de telefone compartilhado usa a mesma equivalência da busca, traduzida pelo Npgsql.
    [SkippableFact]
    public async Task DoisCadastrosEmGrafiasDiferentesSaoTelefoneCompartilhado()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var db = NovoContexto();
        var agora = DateTime.UtcNow;
        SemearEmpresa(db, agora);
        string[] grafias = ["+5511997573992", "5511997573992", "11997573992"];
        var ana = Cadastro("Ana", "(11) 99757-3992", agora.AddDays(-30));
        db.Clientes.Add(ana);
        await db.SaveChangesAsync();
        var repo = new ClienteRepository(db);

        (await repo.TemTelefoneCompartilhadoAsync(EmpresaId, grafias, []))
            .Should().BeFalse("um cadastro mascarado só resolve a conversa");

        db.Clientes.Add(Cadastro("Bruno", "11 99757-3992", agora));
        await db.SaveChangesAsync();
        (await repo.TemTelefoneCompartilhadoAsync(EmpresaId, grafias, []))
            .Should().BeTrue("duas pessoas no mesmo número, em grafias diferentes, pedem conferência");

        db.Clientes.Add(Cadastro("Carla", "1133334444", agora, outroNumero: "(11) 99757-3992"));
        await db.SaveChangesAsync();
        (await repo.TemTelefoneCompartilhadoAsync(EmpresaId, grafias, []))
            .Should().BeTrue("o número na lista de telefones também conta");
    }

    private static void SemearEmpresa(EasyStockDbContext db, DateTime agora) =>
        db.Empresas.Add(new Empresa
        {
            Id = EmpresaId, Nome = "Empresa Telefone", Documento = EmpresaId.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora,
        });

    private EasyStockDbContext NovoContexto()
    {
        var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(_pg!.GetConnectionString()).Options);
        db.SetMobileTenantContext(EmpresaId);
        return db;
    }

    private static Cliente Cadastro(string nome, string telefone, DateTime criadoEm, string? outroNumero = null)
    {
        var cliente = Cliente.Criar(EmpresaId, nome);
        cliente.Telefone = telefone;
        cliente.CriadoEm = criadoEm;
        cliente.AlteradoEm = criadoEm;
        if (outroNumero is not null)
            cliente.Telefones.Add(new ClienteTelefone
            {
                Id = Guid.NewGuid(), ClienteId = cliente.Id, Tipo = "celular", Numero = outroNumero,
                Whatsapp = true, CriadoEm = criadoEm, AlteradoEm = criadoEm,
            });
        return cliente;
    }
}
