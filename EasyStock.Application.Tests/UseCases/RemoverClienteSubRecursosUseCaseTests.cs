using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RemoverClienteDocumento;
using EasyStock.Application.UseCases.RemoverClienteEndereco;
using EasyStock.Application.UseCases.RemoverClienteTelefone;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Cobertura para o fix A1 (commit d4af130) — IDOR cross-tenant em
/// sub-recursos de Cliente. Operador da empresa A nao deve conseguir
/// remover endereco/telefone/documento de cliente da empresa B mesmo
/// passando o sub-recurso id correto.
/// </summary>
public class RemoverClienteSubRecursosUseCaseTests
{
    private readonly IClienteRepository _repo = Substitute.For<IClienteRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    // ── Endereco ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoverEndereco_DeveLancarValidation_QuandoEmpresaIdVazio()
    {
        var useCase = new RemoverClienteEnderecoUseCase(
            _repo, _uow, Substitute.For<ILogger<RemoverClienteEnderecoUseCase>>());

        var act = () => useCase.ExecuteAsync(
            new RemoverClienteEnderecoCommand(Guid.Empty, Guid.NewGuid(), Guid.NewGuid()));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _repo.DidNotReceive().RemoveEnderecoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task RemoverEndereco_DeveRetornarFalse_QuandoRepoNaoEncontra_CenarioCrossTenant()
    {
        var empresaInvasora = Guid.NewGuid();
        var clienteAlheio = Guid.NewGuid();
        var enderecoAlheio = Guid.NewGuid();
        // Repo retorna false — sub-recurso pertence a outra empresa.
        _repo.RemoveEnderecoAsync(empresaInvasora, clienteAlheio, enderecoAlheio).Returns(false);

        var useCase = new RemoverClienteEnderecoUseCase(
            _repo, _uow, Substitute.For<ILogger<RemoverClienteEnderecoUseCase>>());

        var result = await useCase.ExecuteAsync(
            new RemoverClienteEnderecoCommand(empresaInvasora, clienteAlheio, enderecoAlheio));

        result.Should().BeFalse();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task RemoverEndereco_DeveCommitarERetornarTrue_QuandoSucesso()
    {
        var empresa = Guid.NewGuid();
        var cliente = Guid.NewGuid();
        var endereco = Guid.NewGuid();
        _repo.RemoveEnderecoAsync(empresa, cliente, endereco).Returns(true);

        var useCase = new RemoverClienteEnderecoUseCase(
            _repo, _uow, Substitute.For<ILogger<RemoverClienteEnderecoUseCase>>());

        var result = await useCase.ExecuteAsync(
            new RemoverClienteEnderecoCommand(empresa, cliente, endereco));

        result.Should().BeTrue();
        await _repo.Received(1).RemoveEnderecoAsync(empresa, cliente, endereco);
        await _uow.Received(1).CommitAsync();
    }

    // ── Telefone ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoverTelefone_DeveRetornarFalse_QuandoRepoNaoEncontra_CenarioCrossTenant()
    {
        var empresaInvasora = Guid.NewGuid();
        var cliente = Guid.NewGuid();
        var telefone = Guid.NewGuid();
        _repo.RemoveTelefoneAsync(empresaInvasora, cliente, telefone).Returns(false);

        var useCase = new RemoverClienteTelefoneUseCase(
            _repo, _uow, Substitute.For<ILogger<RemoverClienteTelefoneUseCase>>());

        var result = await useCase.ExecuteAsync(
            new RemoverClienteTelefoneCommand(empresaInvasora, cliente, telefone));

        result.Should().BeFalse();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task RemoverTelefone_DeveCommitarERetornarTrue_QuandoSucesso()
    {
        var empresa = Guid.NewGuid();
        var cliente = Guid.NewGuid();
        var telefone = Guid.NewGuid();
        _repo.RemoveTelefoneAsync(empresa, cliente, telefone).Returns(true);

        var useCase = new RemoverClienteTelefoneUseCase(
            _repo, _uow, Substitute.For<ILogger<RemoverClienteTelefoneUseCase>>());

        var result = await useCase.ExecuteAsync(
            new RemoverClienteTelefoneCommand(empresa, cliente, telefone));

        result.Should().BeTrue();
        await _repo.Received(1).RemoveTelefoneAsync(empresa, cliente, telefone);
        await _uow.Received(1).CommitAsync();
    }

    // ── Documento ────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoverDocumento_DeveRetornarFalse_QuandoRepoNaoEncontra_CenarioCrossTenant()
    {
        var empresaInvasora = Guid.NewGuid();
        var cliente = Guid.NewGuid();
        var documento = Guid.NewGuid();
        _repo.RemoveDocumentoAsync(empresaInvasora, cliente, documento).Returns(false);

        var useCase = new RemoverClienteDocumentoUseCase(
            _repo, _uow, Substitute.For<ILogger<RemoverClienteDocumentoUseCase>>());

        var result = await useCase.ExecuteAsync(
            new RemoverClienteDocumentoCommand(empresaInvasora, cliente, documento));

        result.Should().BeFalse();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task RemoverDocumento_DeveCommitarERetornarTrue_QuandoSucesso()
    {
        var empresa = Guid.NewGuid();
        var cliente = Guid.NewGuid();
        var documento = Guid.NewGuid();
        _repo.RemoveDocumentoAsync(empresa, cliente, documento).Returns(true);

        var useCase = new RemoverClienteDocumentoUseCase(
            _repo, _uow, Substitute.For<ILogger<RemoverClienteDocumentoUseCase>>());

        var result = await useCase.ExecuteAsync(
            new RemoverClienteDocumentoCommand(empresa, cliente, documento));

        result.Should().BeTrue();
        await _repo.Received(1).RemoveDocumentoAsync(empresa, cliente, documento);
        await _uow.Received(1).CommitAsync();
    }
}
