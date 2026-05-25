using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>
/// Cobertura "smoke" de entities sem teste dedicado: garante que construcao,
/// state-changing methods e propriedades calculadas executam sem erro e
/// produzem o estado esperado.
/// </summary>
public class PlanoTests
{
    [Fact]
    public void Construtor_default_aceita_propriedades_publicas_via_setter()
    {
        var plano = new Plano
        {
            Id = Guid.NewGuid(),
            Nome = "Premium",
            Descricao = "Tudo liberado",
            LimiteLojas = 5,
            LimiteUsuarios = 20,
            LimiteProdutos = 1000,
            LimiteGeracoesIaMensais = 500,
            PrecoMensal = 199m,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };

        plano.Nome.Should().Be("Premium");
        plano.LimiteLojas.Should().Be(5);
        plano.LojasSaoIlimitadas.Should().BeFalse();
        plano.UsuariosSaoIlimitados.Should().BeFalse();
        plano.ProdutosSaoIlimitados.Should().BeFalse();
        plano.GeracoesIaSaoIlimitadas.Should().BeFalse();
    }

    [Fact]
    public void SemLimite_eh_marcador_para_limites_ilimitados()
    {
        Plano.SemLimite.Should().Be(-1);

        var plano = new Plano
        {
            LimiteLojas = Plano.SemLimite,
            LimiteUsuarios = Plano.SemLimite,
            LimiteProdutos = Plano.SemLimite,
            LimiteGeracoesIaMensais = Plano.SemLimite
        };

        plano.LojasSaoIlimitadas.Should().BeTrue();
        plano.UsuariosSaoIlimitados.Should().BeTrue();
        plano.ProdutosSaoIlimitados.Should().BeTrue();
        plano.GeracoesIaSaoIlimitadas.Should().BeTrue();
    }
}

public class CupomTests
{
    [Fact]
    public void Criar_normaliza_codigo_para_upper_e_inicia_ativo_com_zero_usos()
    {
        var cupom = Cupom.Criar("desconto10", TipoDesconto.Percentual, 10m, 100, null, null);

        cupom.Codigo.Should().Be("DESCONTO10");
        cupom.TipoDesconto.Should().Be(TipoDesconto.Percentual);
        cupom.Valor.Should().Be(10m);
        cupom.LimiteUsos.Should().Be(100);
        cupom.TotalUsos.Should().Be(0);
        cupom.Ativo.Should().BeTrue();
        cupom.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void PodeUsarEm_retorna_true_quando_ativo_dentro_do_prazo_e_abaixo_do_limite()
    {
        var cupom = Cupom.Criar("X", TipoDesconto.Percentual, 5m, 10, DateTime.UtcNow.AddDays(7), null);
        cupom.PodeUsarEm(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void PodeUsarEm_retorna_false_quando_inativo()
    {
        var cupom = Cupom.Criar("X", TipoDesconto.Percentual, 5m, 10, null, null);
        cupom.Toggle();
        cupom.PodeUsarEm(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void PodeUsarEm_retorna_false_quando_atinge_limite_de_usos()
    {
        var cupom = Cupom.Criar("X", TipoDesconto.Percentual, 5m, 1, null, null);
        cupom.IncrementarUso();
        cupom.PodeUsarEm(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void PodeUsarEm_retorna_false_quando_expirado()
    {
        var cupom = Cupom.Criar("X", TipoDesconto.Percentual, 5m, null, DateTime.UtcNow.AddDays(-1), null);
        cupom.PodeUsarEm(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IncrementarUso_aumenta_total_usos()
    {
        var cupom = Cupom.Criar("X", TipoDesconto.Percentual, 5m, null, null, null);
        cupom.IncrementarUso();
        cupom.IncrementarUso();
        cupom.TotalUsos.Should().Be(2);
    }

    [Fact]
    public void Toggle_inverte_estado_ativo()
    {
        var cupom = Cupom.Criar("X", TipoDesconto.Percentual, 5m, null, null, null);
        cupom.Ativo.Should().BeTrue();
        cupom.Toggle();
        cupom.Ativo.Should().BeFalse();
        cupom.Toggle();
        cupom.Ativo.Should().BeTrue();
    }

    [Fact]
    public void Atualizar_atualiza_apenas_campos_nao_nulos_para_codigo_tipo_valor()
    {
        var cupom = Cupom.Criar("X", TipoDesconto.Percentual, 5m, 10, null, null);
        var planoId = Guid.NewGuid();

        cupom.Atualizar("novo", TipoDesconto.ValorFixo, 25m, 50, DateTime.UtcNow.AddDays(30), planoId);

        cupom.Codigo.Should().Be("NOVO");
        cupom.TipoDesconto.Should().Be(TipoDesconto.ValorFixo);
        cupom.Valor.Should().Be(25m);
        cupom.LimiteUsos.Should().Be(50);
        cupom.PlanoId.Should().Be(planoId);
    }

    [Fact]
    public void Atualizar_mantem_codigo_quando_argumento_nulo()
    {
        var cupom = Cupom.Criar("X", TipoDesconto.Percentual, 5m, 10, null, null);

        cupom.Atualizar(null, null, null, null, null, null);

        cupom.Codigo.Should().Be("X");
        cupom.TipoDesconto.Should().Be(TipoDesconto.Percentual);
        cupom.Valor.Should().Be(5m);
    }
}

public class ListaComprasTests
{
    [Fact]
    public void Criar_normaliza_nome_e_inicia_aberta_com_data_atualizada()
    {
        var empresaId = Guid.NewGuid();
        var lista = ListaCompras.Criar(empresaId, "  Lista Semanal  ");

        lista.Id.Should().NotBe(Guid.Empty);
        lista.EmpresaId.Should().Be(empresaId);
        lista.Nome.Should().Be("Lista Semanal");
        lista.Status.Should().Be("aberta");
        lista.Origem.Should().Be("web");
        lista.EstaArquivada.Should().BeFalse();
        lista.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        lista.AlteradoEm.Should().Be(lista.CriadoEm);
    }

    [Fact]
    public void Arquivar_muda_status_e_seta_arquivado_em()
    {
        var lista = ListaCompras.Criar(Guid.NewGuid(), "X");

        lista.Arquivar();

        lista.Status.Should().Be("arquivada");
        lista.EstaArquivada.Should().BeTrue();
        lista.ArquivadoEm.Should().NotBeNull();
    }

    [Fact]
    public void Reabrir_volta_status_aberta_e_limpa_arquivado_em()
    {
        var lista = ListaCompras.Criar(Guid.NewGuid(), "X");
        lista.Arquivar();

        lista.Reabrir();

        lista.Status.Should().Be("aberta");
        lista.EstaArquivada.Should().BeFalse();
        lista.ArquivadoEm.Should().BeNull();
    }

    [Fact]
    public void Contadores_refletem_estado_dos_itens()
    {
        var lista = ListaCompras.Criar(Guid.NewGuid(), "X");
        lista.Itens.Add(new ItemListaCompras { Texto = "leite", Done = false });
        lista.Itens.Add(new ItemListaCompras { Texto = "ovos", Done = true });
        lista.Itens.Add(new ItemListaCompras { Texto = "pao", Done = true });

        lista.TotalItens.Should().Be(3);
        lista.ItensFeitos.Should().Be(2);
        lista.ItensPendentes.Should().Be(1);
    }
}

public class ItemListaComprasTests
{
    [Fact]
    public void MarcarDone_seta_done_com_dados_de_quem_fez_e_data()
    {
        var item = new ItemListaCompras { Texto = "leite", Done = false };
        var userId = Guid.NewGuid();

        item.MarcarDone(userId, "Maria");

        item.Done.Should().BeTrue();
        item.DonePorUserId.Should().Be(userId);
        item.DonePorNome.Should().Be("Maria");
        item.DoneEm.Should().NotBeNull();
        item.AlteradoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Desmarcar_zera_estado_done_preservando_texto_e_id()
    {
        var item = new ItemListaCompras { Texto = "leite" };
        item.MarcarDone(Guid.NewGuid(), "Maria");

        item.Desmarcar();

        item.Done.Should().BeFalse();
        item.DoneEm.Should().BeNull();
        item.DonePorUserId.Should().BeNull();
        item.DonePorNome.Should().BeNull();
    }
}

public class EmailConfirmationTokenTests
{
    [Fact]
    public void Criar_gera_id_com_expiracao_em_24h_e_estado_inicial()
    {
        var usuarioId = Guid.NewGuid();

        var token = EmailConfirmationToken.Criar(usuarioId, "hash-do-token", "10.0.0.1", "Test/1.0");

        token.Id.Should().NotBe(Guid.Empty);
        token.UsuarioId.Should().Be(usuarioId);
        token.TokenHash.Should().Be("hash-do-token");
        token.IpCriacao.Should().Be("10.0.0.1");
        token.UserAgent.Should().Be("Test/1.0");
        token.Confirmado.Should().BeFalse();
        token.ConfirmadoEm.Should().BeNull();
        token.ExpiraEm.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EstaValido_true_quando_nao_confirmado_e_dentro_do_prazo()
    {
        var token = EmailConfirmationToken.Criar(Guid.NewGuid(), "h", null, null);
        token.EstaValido().Should().BeTrue();
    }

    [Fact]
    public void EstaValido_false_quando_ja_confirmado()
    {
        var token = EmailConfirmationToken.Criar(Guid.NewGuid(), "h", null, null);
        token.MarcarComoConfirmado();
        token.EstaValido().Should().BeFalse();
    }

    [Fact]
    public void EstaValido_false_quando_expirado()
    {
        var token = EmailConfirmationToken.Criar(Guid.NewGuid(), "h", null, null);
        token.ExpiraEm = DateTime.UtcNow.AddSeconds(-1);
        token.EstaValido().Should().BeFalse();
    }

    [Fact]
    public void MarcarComoConfirmado_seta_flag_e_data()
    {
        var token = EmailConfirmationToken.Criar(Guid.NewGuid(), "h", null, null);

        token.MarcarComoConfirmado();

        token.Confirmado.Should().BeTrue();
        token.ConfirmadoEm.Should().NotBeNull();
    }
}

public class ConfiguracaoSistemaTests
{
    [Fact]
    public void Criar_inicia_com_alterado_por_system()
    {
        var cfg = ConfiguracaoSistema.Criar("MaxUsuarios", "50", "Limite de usuarios global");

        cfg.Chave.Should().Be("MaxUsuarios");
        cfg.Valor.Should().Be("50");
        cfg.Descricao.Should().Be("Limite de usuarios global");
        cfg.AlteradoPor.Should().Be("system");
        cfg.AlteradoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Atualizar_troca_valor_e_registra_admin()
    {
        var cfg = ConfiguracaoSistema.Criar("MaxUsuarios", "50", "x");
        var antesAlteradoEm = cfg.AlteradoEm;

        // Garantir delta perceptivel — ConfiguracaoSistema usa DateTime.UtcNow.
        Thread.Sleep(5);
        cfg.Atualizar("100", "admin@x.com");

        cfg.Valor.Should().Be("100");
        cfg.AlteradoPor.Should().Be("admin@x.com");
        cfg.AlteradoEm.Should().BeOnOrAfter(antesAlteradoEm);
    }
}

public class CobrancaAssinaturaTests
{
    private static CobrancaAssinatura Sample() =>
        CobrancaAssinatura.Criar(
            Guid.NewGuid(), Guid.NewGuid(), "txid-1", 99.90m,
            "00020126...", "iVBORw0KGgo=",
            DateTime.UtcNow.AddDays(1));

    [Fact]
    public void Criar_inicia_pendente_com_dados_pix_e_expiracao()
    {
        var cob = Sample();

        cob.Id.Should().NotBe(Guid.Empty);
        cob.Status.Should().Be(StatusCobranca.Pendente);
        cob.Valor.Should().Be(99.90m);
        cob.Txid.Should().Be("txid-1");
        cob.PixCopiaCola.Should().Be("00020126...");
        cob.QrCodeBase64.Should().Be("iVBORw0KGgo=");
        cob.PagoEm.Should().BeNull();
        cob.TentativasLembrete.Should().Be(0);
    }

    [Fact]
    public void MarcarComoPaga_muda_status_e_seta_pago_em()
    {
        var cob = Sample();
        cob.MarcarComoPaga();

        cob.Status.Should().Be(StatusCobranca.Paga);
        cob.PagoEm.Should().NotBeNull();
    }

    [Fact]
    public void MarcarComoFalhada_e_Expirar_atualizam_status()
    {
        var cob = Sample();
        cob.MarcarComoFalhada();
        cob.Status.Should().Be(StatusCobranca.Falhada);

        var outra = Sample();
        outra.Expirar();
        outra.Status.Should().Be(StatusCobranca.Expirada);
    }

    [Fact]
    public void AtualizarDadosPix_substitui_pix_qr_e_expiracao()
    {
        var cob = Sample();
        var novaExp = DateTime.UtcNow.AddHours(2);

        cob.AtualizarDadosPix("novo-pix", "novo-qr", novaExp);

        cob.PixCopiaCola.Should().Be("novo-pix");
        cob.QrCodeBase64.Should().Be("novo-qr");
        cob.ExpiracaoEm.Should().Be(novaExp);
    }

    [Fact]
    public void RegistrarLembrete_incrementa_tentativas_e_seta_ultimo()
    {
        var cob = Sample();
        cob.RegistrarLembrete();
        cob.RegistrarLembrete();

        cob.TentativasLembrete.Should().Be(2);
        cob.UltimoLembreteEm.Should().NotBeNull();
    }

    [Fact]
    public void AtualizarDadosBoleto_seta_metodo_pagamento_e_codigo()
    {
        var cob = Sample();

        cob.AtualizarDadosBoleto("00190.00009...", "https://boleto.url/xyz");

        cob.MetodoPagamento.Should().Be("Boleto");
        cob.BoletoCodigo.Should().Be("00190.00009...");
        cob.BoletoUrl.Should().Be("https://boleto.url/xyz");
    }
}
