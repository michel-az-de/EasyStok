using EasyStock.Domain.Entities.Financeiro;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Financeiro;

public class CadastrosFinanceirosTests
{
    [Fact]
    public void Conta_ExigeIdentidadeECamposSemCredenciais()
    {
        var criar = () => ContaBancaria.Criar(Guid.Empty, Guid.NewGuid(), "Loja", "corrente", "Banco", "", "123-4", "Casa", true, Guid.NewGuid());
        criar.Should().Throw<ArgumentException>();
        criar = () => ContaBancaria.Criar(Guid.NewGuid(), Guid.NewGuid(), "Loja", "inexistente", "Banco", "", "123-4", "Casa", true, Guid.NewGuid());
        criar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Conta_RepeticaoNaoAvancaRevisao_EEdicaoObsoletaNaoSobrescreve()
    {
        var tentativa = Guid.NewGuid();
        var conta = ContaBancaria.Criar(Guid.NewGuid(), tentativa, "Loja", "corrente", "Banco", "", "123-4", "Casa", true, Guid.NewGuid());
        var editar = Guid.NewGuid();
        conta.Atualizar(1, editar, "Operação", "corrente", "Banco", "", "123-4", "Casa", false, Guid.NewGuid());
        conta.Atualizar(1, editar, "Operação", "corrente", "Banco", "", "123-4", "Casa", false, Guid.NewGuid());
        conta.Revisao.Should().Be(2);
        conta.Ativo.Should().BeFalse();
        var obsoleta = () => conta.Atualizar(1, Guid.NewGuid(), "Outro", "corrente", "Banco", "", "123-4", "Casa", true, Guid.NewGuid());
        obsoleta.Should().Throw<InvalidOperationException>();
        conta.Nome.Should().Be("Operação");
    }

    [Fact]
    public void Metodo_PreservaCodigoERecusaReutilizarIntencaoComOutroConteudo()
    {
        var metodo = MetodoPagamentoEmpresa.Criar(Guid.NewGuid(), "pix");
        var tentativa = Guid.NewGuid();
        metodo.Configurar(0, tentativa, false, Guid.NewGuid());
        metodo.Configurar(0, tentativa, false, Guid.NewGuid());
        metodo.Revisao.Should().Be(1);
        var reutilizada = () => metodo.Configurar(0, tentativa, true, Guid.NewGuid());
        reutilizada.Should().Throw<InvalidOperationException>();
        metodo.Codigo.Should().Be("pix");
        metodo.Ativo.Should().BeFalse();
        var invalido = () => MetodoPagamentoEmpresa.Criar(Guid.NewGuid(), "bitcoin");
        invalido.Should().Throw<ArgumentException>();
    }
}
