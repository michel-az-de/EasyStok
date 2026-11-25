using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.UnitTests.Repositories;

/// <summary>
/// #1290: a identificação pelo WhatsApp procura o cliente pelo telefone. O número pode estar no
/// <c>Cliente.Telefone</c> ou só na lista de telefones do cadastro (<see cref="ClienteTelefone"/>);
/// olhar só o primeiro criava lead duplicado para quem tem o WhatsApp como segundo número.
/// </summary>
public sealed class ClienteRepositoryTelefoneTests : IDisposable
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly EasyStockDbContext _db;
    private readonly ClienteRepository _repo;

    public ClienteRepositoryTelefoneTests()
    {
        _db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"cliente-telefone-{Guid.NewGuid()}")
            .Options);
        _db.SetMobileTenantContext(_empresaId);
        _repo = new ClienteRepository(_db);
    }

    private async Task<Cliente> GravarAsync(Guid empresaId, string? telefone, string? outroNumero = null)
    {
        var cliente = Cliente.Criar(empresaId, "Fulana");
        cliente.Telefone = telefone;
        if (outroNumero is not null)
            cliente.Telefones.Add(new ClienteTelefone
            {
                Id = Guid.NewGuid(),
                ClienteId = cliente.Id,
                Tipo = "celular",
                Numero = outroNumero,
                Whatsapp = true,
                CriadoEm = cliente.CriadoEm,
                AlteradoEm = cliente.CriadoEm,
            });
        _db.Clientes.Add(cliente);
        await _db.SaveChangesAsync();
        return cliente;
    }

    [Fact]
    public async Task AchaPeloTelefoneDoCadastro()
    {
        var cliente = await GravarAsync(_empresaId, "+5511997573992");

        (await _repo.FindByTelefoneAsync(_empresaId, "+5511997573992"))!.Id.Should().Be(cliente.Id);
    }

    [Fact]
    public async Task AchaPeloNumeroDaListaDeTelefones()
    {
        var cliente = await GravarAsync(_empresaId, "1133334444", outroNumero: "11997573992");

        var achado = await _repo.FindByTelefoneAsync(_empresaId, "11997573992");

        achado.Should().NotBeNull();
        achado!.Id.Should().Be(cliente.Id);
    }

    // #3341: o cadastro antigo pode ter o número em outra grafia (sem o nono dígito, com máscara ou com
    // o zero do tronco). A chave é DDD + 8 últimos dígitos; o nono dígito e o DDI não contam.
    [Theory]
    [InlineData("1197573992")]
    [InlineData("11 99757-3992")]
    [InlineData("(11) 99757-3992")]
    [InlineData("011 99757-3992")]
    [InlineData("5511997573992")]
    public async Task AchaOMesmoCelularEmQualquerGrafiaDoCadastro(string gravado)
    {
        var cliente = await GravarAsync(_empresaId, gravado);

        (await _repo.FindByTelefoneAsync(_empresaId, "+5511997573992"))!.Id.Should().Be(cliente.Id);
    }

    [Fact]
    public async Task AchaCadastroComNonoDigitoQuandoWhatsAppVemSemEle()
    {
        var cliente = await GravarAsync(_empresaId, "11997573992");

        (await _repo.FindByTelefoneAsync(_empresaId, "+551197573992"))!.Id.Should().Be(cliente.Id);
    }

    [Fact]
    public async Task NaoJuntaNumerosDeDddsDiferentes()
    {
        await GravarAsync(_empresaId, "21997573992");

        (await _repo.FindByTelefoneAsync(_empresaId, "+5511997573992")).Should().BeNull();
    }

    // Revisão da PR: com mais de um cadastro casando, o resultado não pode depender da ordem do banco.
    [Fact]
    public async Task NumeroIdenticoNaListaVenceOutraGrafiaNoTelefonePrincipal()
    {
        var emOutraGrafia = await GravarAsync(_empresaId, "1197573992");
        var identico = await GravarAsync(_empresaId, "1133334444", outroNumero: "+5511997573992");

        (await _repo.FindByTelefoneAsync(_empresaId, "+5511997573992"))!.Id.Should().Be(identico.Id);
        emOutraGrafia.Id.Should().NotBe(identico.Id);
    }

    [Fact]
    public async Task EntreDoisCadastrosEmOutraGrafiaVenceOMaisAntigo()
    {
        var recente = Cliente.Criar(_empresaId, "Recente");
        recente.Telefone = "11 99757-3992";
        recente.CriadoEm = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var antigo = Cliente.Criar(_empresaId, "Antigo");
        antigo.Telefone = "(11) 99757-3992";
        antigo.CriadoEm = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.Clientes.AddRange(recente, antigo); // o mais recente entra primeiro: só o desempate explícito o derruba
        await _db.SaveChangesAsync();

        (await _repo.FindByTelefoneAsync(_empresaId, "+5511997573992"))!.Id.Should().Be(antigo.Id);
    }

    // Revisão final: o número em grafia limpa (só dígitos, como o ERP e a importação gravam) vence o mascarado,
    // mesmo que o mascarado seja mais antigo; antes da #3341 só o limpo era achado.
    [Fact]
    public async Task GrafiaLimpaVenceOMascaradoMaisAntigo()
    {
        var mascarado = Cliente.Criar(_empresaId, "Mascarado");
        mascarado.Telefone = "(11) 99757-3992";
        mascarado.CriadoEm = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var limpo = Cliente.Criar(_empresaId, "Limpo");
        limpo.Telefone = "11997573992";
        limpo.CriadoEm = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.Clientes.AddRange(mascarado, limpo);
        await _db.SaveChangesAsync();

        (await _repo.FindByTelefoneAsync(_empresaId, "+5511997573992"))!.Id.Should().Be(limpo.Id);
    }

    // Aceite da #3341 (R2L-01, linha 5 da matriz): cadastro do ERP em 12 grafias x wa_id com e sem o nono dígito.
    // Antes, 15 das 24 combinações criavam lead duplicado.
    public static IEnumerable<object[]> MatrizDas24Combinacoes() =>
        from gravado in new[]
        {
            "+5511997573992", "5511997573992", "11997573992", "(11) 99757-3992", "11 99757-3992", "+55 11 99757-3992",
            "11 9 9757-3992", "011997573992", "11997573992 ", "+551197573992", "1197573992", "551197573992",
        }
        from waId in new[] { "+5511997573992", "+551197573992" }
        select new object[] { gravado, waId };

    [Theory]
    [MemberData(nameof(MatrizDas24Combinacoes))]
    public async Task MatrizDas24CombinacoesAchaOCadastroExistente(string gravado, string waId)
    {
        var cliente = await GravarAsync(_empresaId, gravado);

        (await _repo.FindByTelefoneAsync(_empresaId, waId))!.Id.Should().Be(cliente.Id);
    }

    [Fact]
    public async Task NaoAchaNumeroDeOutraEmpresa()
    {
        await GravarAsync(Guid.NewGuid(), null, outroNumero: "11997573992");

        (await _repo.FindByTelefoneAsync(_empresaId, "11997573992")).Should().BeNull();
    }

    public void Dispose() => _db.Dispose();
}
