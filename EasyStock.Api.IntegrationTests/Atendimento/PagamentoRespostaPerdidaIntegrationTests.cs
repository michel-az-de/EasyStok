using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task BaixaParcial_RespostaPerdidaAposCommit_RetryAposLeaseNaoDuplica(bool conexaoAbortada, bool leaseConcorrente)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var perda = new PerderPrimeiraRespostaPagamento(conexaoAbortada);
        var lease = new LeaseDuranteRecebimento();
        await using var factory = CriarFactory(Provedor(new())).WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IStartupFilter>(perda);
                if (leaseConcorrente) s.AddScoped<IIdempotencyKeyRepository>(sp =>
                    new ReciboPausado(new IdempotencyKeyRepository(sp.GetRequiredService<EasyStockDbContext>()), lease));
            }));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var pedidoId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var pedido = Pedido.Criar(EmpresaId, origem: "whatsapp");
            pedido.Id = pedidoId;
            pedido.ClienteId = c.ClienteId;
            pedido.Status = StatusPedidoMapper.Aguardando;
            pedido.Itens.Add(new PedidoItem
            {
                Id = Guid.NewGuid(), PedidoId = pedidoId, Nome = "Pedido de teste",
                Quantidade = 1, PrecoUnitario = 100m, Subtotal = 100m, CriadoEm = DateTime.UtcNow,
            });
            pedido.RecalcularTotal();
            db.Pedidos.Add(pedido);
            db.CobrancasPedido.Add(CobrancaPedido.CriarNaEntrega(EmpresaId, pedidoId, 100m, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        var chave = Guid.NewGuid().ToString();
        var primeiraTask = Receber(chave, 10m);
        Task<HttpResponseMessage>? concorrente = null;
        if (leaseConcorrente)
        {
            await lease.Entrou.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                await db.IdempotencyKeys.Where(k => k.Key == chave)
                    .ExecuteUpdateAsync(s => s.SetProperty(k => k.LockedAtUtc, DateTime.UtcNow.AddMinutes(-2)));
            }
            concorrente = Receber(chave, 10m);
            try { await lease.Retomou.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
            finally { lease.Liberar.TrySetResult(); }
        }
        try
        {
            using var primeira = await primeiraTask;
            var corpo = await primeira.Content.ReadAsStringAsync();
            primeira.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.InternalServerError, (HttpStatusCode)499], corpo);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            // A queda está no transporte da resposta, depois da gravação real no banco.
        }
        if (concorrente is not null)
        {
            using var segunda = await concorrente;
            segunda.StatusCode.Should().Be(HttpStatusCode.OK, await segunda.Content.ReadAsStringAsync());
        }
        perda.Ocorreu.Should().BeTrue();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            (await db.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId)).TotalPago.Should().Be(10m);
            (await db.Set<PedidoPagamento>().CountAsync(p => p.PedidoId == pedidoId)).Should().Be(1);
            (await db.IdempotencyKeys.SingleAsync(k => k.Key == chave)).Status
                .Should().Be(EasyStock.Domain.Enums.StatusIdempotencyKey.Concluido);
            await db.IdempotencyKeys.Where(k => k.Key == chave)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LockedAtUtc, DateTime.UtcNow.AddMinutes(-2)));
        }

        using var repetida = await Receber(chave, 10m);
        repetida.StatusCode.Should().Be(HttpStatusCode.OK, await repetida.Content.ReadAsStringAsync());
        repetida.Headers.Contains("X-Idempotent-Replay").Should().BeTrue();
        using var divergente = await Receber(chave, 11m);
        divergente.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            (await db.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId)).TotalPago.Should().Be(10m,
                "a resposta perdida não autoriza repetir o recebimento parcial");
            (await db.Set<PedidoPagamento>().CountAsync(p => p.PedidoId == pedidoId)).Should().Be(1);
        }

        Task<HttpResponseMessage> Receber(string key, decimal valor)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/pedidos/{pedidoId}/pagamentos")
            {
                Content = JsonContent.Create(new { EmpresaId, PedidoId = pedidoId, Metodo = "pix", Valor = valor }),
            };
            request.Headers.Add("Idempotency-Key", key);
            return client.SendAsync(request);
        }
    }

    [SkippableFact]
    public Task BaixaParcial_LeaseExpiradoComPrimeiroRecebimentoAindaAtivo_NaoDuplica() =>
        BaixaParcial_RespostaPerdidaAposCommit_RetryAposLeaseNaoDuplica(false, true);

    private sealed class LeaseDuranteRecebimento
    {
        public TaskCompletionSource Entrou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Retomou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Primeira;
    }

    private sealed class ReciboPausado(IIdempotencyKeyRepository repo, LeaseDuranteRecebimento lease) : IIdempotencyKeyRepository
    {
        public Task<IdempotencyKey?> ObterConcluidoAsync(Guid id, Guid empresaId, CancellationToken ct = default) =>
            repo.ObterConcluidoAsync(id, empresaId, ct);
        public Task<(IdempotencyKey Entry, bool Reservado)> TryBeginAsync(string key, Guid empresaId,
            string metodoRecurso, string? payloadHash, TimeSpan ttl, CancellationToken ct = default) =>
            repo.TryBeginAsync(key, empresaId, metodoRecurso, payloadHash, ttl, ct);
        public async Task<bool> TryTakeoverAsync(Guid id, DateTime esperado, DateTime novoLock, CancellationToken ct = default)
        {
            var resultado = await repo.TryTakeoverAsync(id, esperado, novoLock, ct);
            if (resultado) lease.Retomou.TrySetResult();
            return resultado;
        }
        public Task<bool> TryReabrirAsync(Guid id, string? payloadHash, DateTime novoLock, CancellationToken ct = default) =>
            repo.TryReabrirAsync(id, payloadHash, novoLock, ct);
        public async Task MarcarConcluidoAsync(Guid id, int status, string? json, CancellationToken ct = default)
        {
            if (Interlocked.CompareExchange(ref lease.Primeira, 1, 0) == 0)
            {
                lease.Entrou.TrySetResult();
                await lease.Liberar.Task.WaitAsync(TimeSpan.FromSeconds(45), ct);
            }
            await repo.MarcarConcluidoAsync(id, status, json, ct);
        }
        public Task MarcarFalhouAsync(Guid id, CancellationToken ct = default) => repo.MarcarFalhouAsync(id, ct);
        public Task<int> CleanupExpiredAsync(DateTime referencia, CancellationToken ct = default) => repo.CleanupExpiredAsync(referencia, ct);
    }

    private sealed class PerderPrimeiraRespostaPagamento(bool conexaoAbortada) : IStartupFilter
    {
        private int ocorreu;
        public bool Ocorreu => ocorreu != 0;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, proximo) =>
            {
                if (HttpMethods.IsPost(context.Request.Method)
                    && context.Request.Path.Value?.EndsWith("/pagamentos", StringComparison.Ordinal) == true
                    && Interlocked.CompareExchange(ref ocorreu, 1, 0) == 0)
                {
                    using var queda = new CancellationTokenSource();
                    context.RequestAborted = queda.Token;
                    var original = context.Response.Body;
                    context.Response.Body = new RespostaPerdidaStream(original, queda, () => context.Response.StatusCode, conexaoAbortada);
                    try { await proximo(context); }
                    finally { context.Response.Body = original; }
                }
                else await proximo(context);
            });
            next(app);
        };
    }

    private sealed class RespostaPerdidaStream(Stream original, CancellationTokenSource queda, Func<int> status, bool conexaoAbortada) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => original.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => original.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (status() is >= 200 and < 300) Perder();
            original.Write(buffer, offset, count);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (status() is >= 200 and < 300) Perder();
            return original.WriteAsync(buffer, offset, count, cancellationToken);
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (status() is >= 200 and < 300) Perder();
            return original.WriteAsync(buffer, cancellationToken);
        }
        private void Perder()
        {
            if (conexaoAbortada) queda.Cancel();
            throw new IOException("Resposta perdida pelo transporte após gravar pagamento de teste.");
        }
    }
}
