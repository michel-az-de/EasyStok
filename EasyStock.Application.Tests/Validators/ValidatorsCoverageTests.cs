using EasyStock.Application.UseCases.AlterarSenha;
using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.CadastrarUsuario;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Application.Validators;
using EasyStock.Domain.Enums;
using FluentAssertions;

namespace EasyStock.Application.Tests.Validators;

/// <summary>
/// Cobertura dos validators FluentValidation. Cada validator estava em 0% pois
/// a invocacao acontece via DI no pipeline; estes testes constroem a regra
/// diretamente e validam happy path + cenarios de falha por campo.
/// </summary>
public class AlterarSenhaCommandValidatorTests
{
    private readonly AlterarSenhaCommandValidator _v = new();

    [Fact]
    public void IsValid_quando_senha_atual_e_nova_atendem_politica()
    {
        var cmd = new AlterarSenhaCommand("OldP@ssw0rd", "Nov@Senha2024");
        _v.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Falha_quando_SenhaAtual_vazia()
    {
        var r = _v.Validate(new AlterarSenhaCommand("", "Nov@Senha2024"));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "SenhaAtual");
    }

    [Theory]
    [InlineData("curta1!")]            // < 8
    [InlineData("MINUSCULAFALTA1!")]   // sem minuscula
    [InlineData("maiusculafalta1!")]   // sem maiuscula
    [InlineData("SemNumero!")]          // sem numero
    [InlineData("SemEspecial1A")]       // sem especial
    public void Falha_quando_NovaSenha_nao_atende_politica(string senha)
    {
        var r = _v.Validate(new AlterarSenhaCommand("OldP@ss123", senha));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Falha_quando_NovaSenha_igual_atual()
    {
        var r = _v.Validate(new AlterarSenhaCommand("MinhaS3nh@!", "MinhaS3nh@!"));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "NovaSenha" && e.ErrorMessage.Contains("diferente"));
    }
}

public class CadastrarUsuarioCommandValidatorTests
{
    private readonly CadastrarUsuarioCommandValidator _v = new();

    [Fact]
    public void IsValid_quando_todos_os_campos_corretos()
    {
        var cmd = new CadastrarUsuarioCommand("Joao Silva", "joao@x.com", "Senh@1234567");
        _v.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "x@x.com", "Senh@1234567", "Nome")]
    [InlineData("J", "nao-eh-email", "Senh@1234567", "Email")]
    [InlineData("J", "x@x.com", "curta1!A", "Senha")]
    public void Falha_quando_campo_obrigatorio_invalido(string nome, string email, string senha, string campoComErro)
    {
        var r = _v.Validate(new CadastrarUsuarioCommand(nome, email, senha));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == campoComErro);
    }

    [Fact]
    public void Falha_quando_Nome_excede_150_caracteres()
    {
        var nomeGigante = new string('a', 151);
        var r = _v.Validate(new CadastrarUsuarioCommand(nomeGigante, "x@x.com", "Senh@1234567"));
        r.IsValid.Should().BeFalse();
    }
}

public class EsqueciSenhaCommandValidatorTests
{
    private readonly EsqueciSenhaCommandValidator _v = new();

    [Fact]
    public void IsValid_quando_email_valido() =>
        _v.Validate(new EsqueciSenhaCommand("a@b.com")).IsValid.Should().BeTrue();

    [Fact]
    public void Falha_quando_email_vazio() =>
        _v.Validate(new EsqueciSenhaCommand("")).IsValid.Should().BeFalse();

    [Fact]
    public void Falha_quando_email_invalido() =>
        _v.Validate(new EsqueciSenhaCommand("nao-eh-email")).IsValid.Should().BeFalse();
}

public class LogoutCommandValidatorTests
{
    private readonly LogoutCommandValidator _v = new();

    [Fact]
    public void IsValid_quando_refresh_token_presente() =>
        _v.Validate(new LogoutCommand("token")).IsValid.Should().BeTrue();

    [Fact]
    public void Falha_quando_refresh_token_vazio() =>
        _v.Validate(new LogoutCommand("")).IsValid.Should().BeFalse();
}

public class RefreshTokenCommandValidatorTests
{
    private readonly RefreshTokenCommandValidator _v = new();

    [Fact]
    public void IsValid_quando_refresh_token_presente() =>
        _v.Validate(new RefreshTokenCommand("token")).IsValid.Should().BeTrue();

    [Fact]
    public void Falha_quando_refresh_token_vazio() =>
        _v.Validate(new RefreshTokenCommand("")).IsValid.Should().BeFalse();
}

public class ResetarSenhaCommandValidatorTests
{
    private readonly ResetarSenhaCommandValidator _v = new();

    [Fact]
    public void IsValid_quando_token_e_senha_corretos()
    {
        _v.Validate(new ResetarSenhaCommand("tok", "Senh@1234567")).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Senh@1234567", "Token")]
    [InlineData("tok", "curta1!", "NovaSenha")]
    [InlineData("tok", "SEMMINUSC1!", "NovaSenha")]
    public void Falha_quando_campo_invalido(string token, string senha, string campoComErro)
    {
        var r = _v.Validate(new ResetarSenhaCommand(token, senha));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == campoComErro);
    }
}

public class AtualizarUsuarioAtualCommandValidatorTests
{
    private readonly AtualizarUsuarioAtualCommandValidator _v = new();

    [Fact]
    public void IsValid_quando_tudo_null()
    {
        // Nome/Email opcionais — TemaPreferido default null e aceito.
        _v.Validate(new AtualizarUsuarioAtualCommand(Nome: null, Email: null, TemaPreferido: null))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_quando_tema_e_light_ou_dark()
    {
        _v.Validate(new AtualizarUsuarioAtualCommand("Joao", "j@x.com", "light")).IsValid.Should().BeTrue();
        _v.Validate(new AtualizarUsuarioAtualCommand("Joao", "j@x.com", "dark")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Falha_quando_tema_invalido()
    {
        _v.Validate(new AtualizarUsuarioAtualCommand("Joao", "j@x.com", "purple"))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Falha_quando_email_provido_mas_invalido()
    {
        _v.Validate(new AtualizarUsuarioAtualCommand(Nome: null, Email: "nao-email", TemaPreferido: null))
            .IsValid.Should().BeFalse();
    }
}

public class CadastrarProdutoCommandValidatorTests
{
    private readonly CadastrarProdutoCommandValidator _v = new();

    private static CadastrarProdutoCommand CmdValido() =>
        new(
            EmpresaId: Guid.NewGuid(),
            CategoriaId: Guid.NewGuid(),
            SubcategoriaId: null,
            Nome: "Galaxy Buds",
            DescricaoBase: null,
            Marca: null,
            Tipo: TipoProduto.Fisico,
            SkuBase: null,
            CodigoBarras: null,
            ControlaValidade: false,
            Dimensoes: null,
            CustoReferencia: null,
            PrecoReferencia: 100m,
            MargemEstimada: null,
            AtributosJson: null,
            FotosJson: null,
            Caracteristicas: null,
            Embalagens: null,
            Variacoes: null);

    [Fact]
    public void IsValid_quando_minimo_obrigatorio_atendido()
    {
        _v.Validate(CmdValido()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Falha_quando_EmpresaId_vazio()
    {
        var cmd = CmdValido() with { EmpresaId = Guid.Empty };
        _v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Falha_quando_CategoriaId_vazio()
    {
        var cmd = CmdValido() with { CategoriaId = Guid.Empty };
        _v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Falha_quando_Nome_vazio()
    {
        var cmd = CmdValido() with { Nome = "" };
        _v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Falha_quando_Nome_excede_200_caracteres()
    {
        var cmd = CmdValido() with { Nome = new string('a', 201) };
        _v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Falha_quando_PrecoReferencia_zero_ou_negativo()
    {
        var cmdZero = CmdValido() with { PrecoReferencia = 0m };
        _v.Validate(cmdZero).IsValid.Should().BeFalse();

        var cmdNeg = CmdValido() with { PrecoReferencia = -5m };
        _v.Validate(cmdNeg).IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_quando_PrecoReferencia_null()
    {
        var cmd = CmdValido() with { PrecoReferencia = null };
        _v.Validate(cmd).IsValid.Should().BeTrue();
    }
}
