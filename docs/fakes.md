# HTTP and time fakes

[← Back to README](../README.md)

## External HTTP APIs

`WireMockHarness` (`Enhanced.Testing.Component.WireMock`) hosts an in-process WireMock.Net server instead of a real
external HTTP service and passes its URL to the application (`ConfigurationKey`). Stub and verify calls through
`Server`, and call `Reset` between tests.

## Time

`TimeProviderHarness` (`Enhanced.Testing.Component.Time`) replaces the application's `TimeProvider` with a
`FakeTimeProvider` (`TimeProvider`), so delays, timeouts and timers complete as soon as the test moves the clock
(`Advance`, `SetUtcNow`) instead of waiting for real time. It affects only code that resolves `TimeProvider` from
the service provider, including framework and library services that do so (for example, resilience handlers): their
timeouts and retry delays no longer elapse on their own. `DateTime.UtcNow`, `TimeProvider.System` and the containers
keep the real clock.

```csharp
var time = new TimeProviderHarness(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
```
