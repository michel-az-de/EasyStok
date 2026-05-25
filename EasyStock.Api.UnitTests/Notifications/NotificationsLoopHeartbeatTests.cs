using EasyStock.Application.Services.Notifications;
using EasyStock.Infra.Notifications.Hosting;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Notifications;

public class NotificationsLoopHeartbeatTests
{
    [Fact]
    public void LastBeat_retorna_null_antes_do_primeiro_heartbeat()
    {
        var sut = new NotificationsLoopHeartbeat();

        sut.LastBeat(NotificationsLoops.Dispatcher).Should().BeNull();
        sut.LastBeat(NotificationsLoops.Avaliador).Should().BeNull();
        sut.LastBeat(NotificationsLoops.Coletor).Should().BeNull();
    }

    [Fact]
    public void Heartbeat_grava_timestamp_do_TimeProvider()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero));
        var sut = new NotificationsLoopHeartbeat(time);

        sut.Heartbeat(NotificationsLoops.Dispatcher);

        sut.LastBeat(NotificationsLoops.Dispatcher).Should().Be(time.GetUtcNow());
    }

    [Fact]
    public void Heartbeat_sobrescreve_valor_anterior()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero));
        var sut = new NotificationsLoopHeartbeat(time);

        sut.Heartbeat(NotificationsLoops.Dispatcher);
        var primeiro = sut.LastBeat(NotificationsLoops.Dispatcher);

        time.Advance(TimeSpan.FromMinutes(1));
        sut.Heartbeat(NotificationsLoops.Dispatcher);
        var segundo = sut.LastBeat(NotificationsLoops.Dispatcher);

        segundo.Should().BeAfter(primeiro!.Value);
        (segundo!.Value - primeiro.Value).Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Snapshot_inclui_apenas_loops_que_bateram()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero));
        var sut = new NotificationsLoopHeartbeat(time);

        sut.Heartbeat(NotificationsLoops.Dispatcher);
        sut.Heartbeat(NotificationsLoops.Coletor);

        var snap = sut.Snapshot();

        snap.Should().ContainKey(NotificationsLoops.Dispatcher)
            .And.ContainKey(NotificationsLoops.Coletor)
            .And.NotContainKey(NotificationsLoops.Avaliador);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public TestTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
