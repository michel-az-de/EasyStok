using EasyStock.Api.Mobile.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.UnitTests.Mobile;

// #2317 (SYNC-9): erro transitório no push não pode virar rejeição permanente.
// A mutação precisa ficar fora de accepted e de rejected, para o aparelho reenviar.
public sealed class SyncPushErroTransitorioTests
{
    [Fact]
    public void ConcorrenciaDoBancoEhTransitoria()
        => SyncController.EhErroTransitorio(new DbUpdateConcurrencyException("linha alterada")).Should().BeTrue();

    [Fact]
    public void TimeoutEhTransitorio()
        => SyncController.EhErroTransitorio(new TimeoutException("tempo esgotado")).Should().BeTrue();

    [Fact]
    public void QuedaDeConexaoEhTransitoria()
        => SyncController.EhErroTransitorio(new IOException("conexão encerrada")).Should().BeTrue();

    [Fact]
    public void ErroDeValidacaoEhPermanente()
        => SyncController.EhErroTransitorio(new InvalidOperationException("payload inválido")).Should().BeFalse();
}
