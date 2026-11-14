using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AdicionarClienteDocumento;
using Microsoft.Extensions.Logging;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #2563: o documento extra do cliente valida CPF/CNPJ como o cadastro (CLI-01). Antes,
/// "111.111.111-11" era gravado e um CPF sem dígitos virava texto vazio.
/// </summary>
public class AdicionarClienteDocumentoUseCaseTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private readonly IClienteRepository _repo = Substitute.For<IClienteRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ClienteEntity _cliente = new() { Id = Guid.NewGuid(), EmpresaId = Empresa, Nome = "Ana" };

    public AdicionarClienteDocumentoUseCaseTests() =>
        _repo.GetByIdAsync(Empresa, _cliente.Id).Returns(_cliente);

    private Task<Guid?> Adicionar(string tipo, string valor) =>
        new AdicionarClienteDocumentoUseCase(_repo, _uow, Substitute.For<ILogger<AdicionarClienteDocumentoUseCase>>())
            .ExecuteAsync(new AdicionarClienteDocumentoCommand(Empresa, _cliente.Id, tipo, valor));

    [Theory]
    [InlineData("cpf", "111.111.111-11")]
    [InlineData("cpf", "abc")]
    [InlineData("cnpj", "11.222.333/0001-99")]
    public async Task Recusa_documento_invalido_e_nao_grava(string tipo, string valor)
    {
        var act = () => Adicionar(tipo, valor);

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _repo.DidNotReceive().AddDocumentoAsync(Arg.Any<ClienteDocumento>());
    }

    [Fact]
    public async Task Grava_cpf_valido_so_com_os_digitos()
    {
        await Adicionar("CPF", "529.982.247-25");

        await _repo.Received(1).AddDocumentoAsync(Arg.Is<ClienteDocumento>(d => d.Tipo == "cpf" && d.Valor == "52998224725"));
    }

    [Fact]
    public async Task Outro_tipo_continua_livre()
    {
        await Adicionar("rg", "12.345.678-9");

        await _repo.Received(1).AddDocumentoAsync(Arg.Is<ClienteDocumento>(d => d.Valor == "12.345.678-9"));
    }
}
