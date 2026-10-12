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

    [Fact]
    public async Task NaoAchaNumeroDeOutraEmpresa()
    {
        await GravarAsync(Guid.NewGuid(), null, outroNumero: "11997573992");

        (await _repo.FindByTelefoneAsync(_empresaId, "11997573992")).Should().BeNull();
    }

    public void Dispose() => _db.Dispose();
}
