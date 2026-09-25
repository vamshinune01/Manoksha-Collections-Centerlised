using Manoksha.Application.Abstractions;
using Manoksha.IntegrationTests.Infrastructure;
using Manoksha.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Manoksha.IntegrationTests;

/// <summary>Server idempotency: duplicate clicks/retries produce exactly one effect (SPEC §32 "Repeated Place Order/Pay clicks").</summary>
[Collection(ApiCollection.Name)]
public class IdempotencyTests(ManokshaApiFactory factory)
{
    private sealed record Req(string Item, int Qty);

    private sealed record Res(Guid OperationId, int Qty);

    [Fact]
    public async Task Same_key_same_request_executes_once_and_replays_response()
    {
        var key = Guid.NewGuid().ToString("N");
        var executions = 0;

        async Task<Res> Run()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();
            return await service.ExecuteAsync("test.place_order", key, new Req("SKU-1", 2), _ =>
            {
                Interlocked.Increment(ref executions);
                return Task.FromResult(new Res(Guid.NewGuid(), 2));
            });
        }

        var first = await Run();
        var second = await Run();
        second.Should().Be(first);
        executions.Should().Be(1);
    }

    [Fact]
    public async Task Same_key_different_request_is_rejected()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();
        await service.ExecuteAsync("test.place_order", key, new Req("SKU-1", 1), _ => Task.FromResult(new Res(Guid.NewGuid(), 1)));

        await using var scope2 = factory.Services.CreateAsyncScope();
        var act = () => scope2.ServiceProvider.GetRequiredService<IIdempotencyService>()
            .ExecuteAsync("test.place_order", key, new Req("SKU-1", 5), _ => Task.FromResult(new Res(Guid.NewGuid(), 5)));
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(ErrorCodes.IdempotencyKeyReused);
    }

    [Fact]
    public async Task Concurrent_duplicates_execute_exactly_once()
    {
        var key = Guid.NewGuid().ToString("N");
        var executions = 0;

        var tasks = Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();
            return await service.ExecuteAsync("test.concurrent", key, new Req("SKU-9", 1), async _ =>
            {
                Interlocked.Increment(ref executions);
                await Task.Delay(150, CancellationToken.None);
                return new Res(Guid.NewGuid(), 1);
            });
        }).ToList();

        var results = await Task.WhenAll(tasks);
        executions.Should().Be(1);
        results.Select(r => r.OperationId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Failed_attempt_leaves_no_record_and_can_be_retried()
    {
        var key = Guid.NewGuid().ToString("N");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var act = () => scope.ServiceProvider.GetRequiredService<IIdempotencyService>().ExecuteAsync<Res>(
                "test.retry", key, new Req("SKU-2", 1), _ => throw new BusinessRuleException("INSUFFICIENT_STOCK", "no stock"));
            await act.Should().ThrowAsync<BusinessRuleException>();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IIdempotencyService>().ExecuteAsync(
                "test.retry", key, new Req("SKU-2", 1), _ => Task.FromResult(new Res(Guid.NewGuid(), 1)));
            result.Qty.Should().Be(1);
        }
    }
}
