# Enhanced.Testing.Component.Time

[![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.Time)](https://www.nuget.org/packages/Enhanced.Testing.Component.Time)

A time harness for [Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component): replaces the
application's `TimeProvider` with a `FakeTimeProvider` the test controls, so delays, timeouts, retries and timers
complete as soon as the test moves the clock instead of waiting for real time. No Docker needed.

```shell
dotnet add package Enhanced.Testing.Component.Time
```

## Usage

```csharp
var time = new TimeProviderHarness(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

var component = ComponentBuilder.Create<Program>()
                                .AddHarness(time)
                                .AddHarness(http)
                                .Build();
```

In a test, move the clock instead of waiting:

```csharp
await client.PostAsJsonAsync("/subscriptions", new { Plan = "trial" });

time.Advance(TimeSpan.FromDays(15));

var subscription = await client.GetFromJsonAsync<Subscription>("/subscriptions/current");
Assert.True(subscription!.Expired);
```

When code is already waiting on the fake clock (for example, a `Task.Delay(..., timeProvider)` in a request that is
still running), `Advance` completes the wait immediately. Advance only after the code has started waiting, or keep
advancing until the request finishes.

- `Advance(TimeSpan)` and `SetUtcNow(DateTimeOffset)` move the clock forward; `UtcNow` reads it.
- `TimeProvider` is the `FakeTimeProvider` itself, for anything else it supports (for example, `AutoAdvanceAmount`).
- `new TimeProviderHarness()` starts at the `FakeTimeProvider` default of 2000-01-01 UTC. Pass a start time if the
  application checks dates such as certificate or token expiry.

## What it affects

The harness replaces every `TimeProvider` registration in the application's service provider. It affects only code
that resolves `TimeProvider` from there, including framework and library services that do so (for example,
resilience handlers): their timeouts and retry delays no longer elapse on their own, only when the test moves the
clock. `DateTime.UtcNow`, `TimeProvider.System` and the containers keep the real clock.

Register `TimeProvider` in the application and inject it where you need time:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
```
