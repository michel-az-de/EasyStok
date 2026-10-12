using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services;

/// <summary>
/// N13: a empresa padrão (<c>Auth:Google:EmpresaPadrao</c>, CNPJ ou nome exato; tenant único da ADR-0056) era um helper
/// privado do <c>AuthController</c> e não servia ao Worker. Agora é um serviço: CNPJ primeiro, nome exato quando único,
/// uma consulta por processo quando acha e nenhuma memória da falha.
/// </summary>
public class EmpresaPadraoResolverTests
{
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly EmpresaPadraoCache _cache = new();

    private EmpresaPadraoResolver Criar(string? chave)
    {
        var valores = new Dictionary<string, string?>();
        if (chave is not null) valores[EmpresaPadraoResolver.Chave] = chave;
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        return new EmpresaPadraoResolver(config, _empresas, _cache, NullLogger<EmpresaPadraoResolver>.Instance);
    }

    [Fact]
    public async Task AchaPorCnpj()
    {
        var casa = Empresa.Criar("Casa da Baba", "11222333000181");
        _empresas.GetByDocumentoAsync("11222333000181").Returns(casa);

        var id = await Criar("11222333000181").ResolverAsync();

        id.Should().Be(casa.Id);
        await _empresas.DidNotReceive().GetAllAsync();
    }

    [Fact]
    public async Task AchaPorNomeExatoQuandoUnico()
    {
        var casa = Empresa.Criar("Casa da Baba", "11222333000181");
        _empresas.GetByDocumentoAsync("casa da baba").Returns((Empresa?)null);
        _empresas.GetAllAsync().Returns([casa, Empresa.Criar("Outra Loja", "99888777000166")]);

        var id = await Criar("casa da baba").ResolverAsync();

        id.Should().Be(casa.Id);
    }

    [Fact]
    public async Task NomeAmbiguoNaoResolve()
    {
        _empresas.GetByDocumentoAsync("Casa da Baba").Returns((Empresa?)null);
        _empresas.GetAllAsync().Returns([Empresa.Criar("Casa da Baba", "11222333000181"), Empresa.Criar("Casa da Baba", "99888777000166")]);

        var id = await Criar("Casa da Baba").ResolverAsync();

        id.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task SemChaveNaoResolve(string? chave)
    {
        var id = await Criar(chave).ResolverAsync();

        id.Should().BeNull();
        await _empresas.DidNotReceiveWithAnyArgs().GetByDocumentoAsync(default!);
    }

    [Fact]
    public async Task GuardaOAcertoPorProcessoENaoGuardaAFalha()
    {
        var casa = Empresa.Criar("Casa da Baba", "11222333000181");
        _empresas.GetByDocumentoAsync("Casa da Baba").Returns((Empresa?)null);
        _empresas.GetAllAsync().Returns(
            _ => Task.FromResult<IEnumerable<Empresa>>([]),
            _ => Task.FromResult<IEnumerable<Empresa>>([casa]));
        var resolver = Criar("Casa da Baba");

        (await resolver.ResolverAsync()).Should().BeNull();
        (await resolver.ResolverAsync()).Should().Be(casa.Id);
        (await resolver.ResolverAsync()).Should().Be(casa.Id);

        await _empresas.Received(2).GetAllAsync();
    }
}
