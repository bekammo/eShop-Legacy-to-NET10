using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace eShop.Catalog.Api.IntegrationTests.Contract;

[Trait("Category", "Docker")]
public sealed partial class LegacyContractTests(CatalogApiFactory factory, MockModeCatalogApiFactory mockMode)
    : IClassFixture<CatalogApiFactory>, IClassFixture<MockModeCatalogApiFactory>
{
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new() { AllowAutoRedirect = false, HandleCookies = false };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Exchanges => LegacyContract.ExchangeNames;

    [Theory]
    [MemberData(nameof(Exchanges))]
    public async Task Database_mode_answers_the_golden_exchange(string exchange, string delta)
    {
        using var client = factory.CreateClient(ClientOptions);

        await LegacyContract.ReplayAsync(client, exchange, delta, CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Exchanges))]
    public async Task Mock_mode_answers_the_golden_exchange(string exchange, string delta)
    {
        using var client = mockMode.CreateClient(ClientOptions);

        await LegacyContract.ReplayAsync(client, exchange, delta, CancellationToken);
    }

    [Fact]
    public void Deltas_are_recorded_in_the_register()
    {
        var register = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contract", "behavior-changes.md"));
        var entries = EntryHeading().Split(EntriesSection().Match(register).Groups["entries"].Value)
            .Skip(1)
            .Select(static entry => entry.Split(':', 2))
            .ToDictionary(static entry => entry[0], static entry => entry[1]);

        Assert.All(LegacyContract.Deltas, delta =>
        {
            Assert.True(LegacyContract.Exchanges.ContainsKey(delta.Key), delta.Key);
            var exchange = LegacyContract.Exchanges[delta.Key];
            var kinds = (delta.Value.AnswerOf is null ? 0 : 1) + (delta.Value.Status is null ? 0 : 1) + (delta.Value.Partial ? 1 : 0);
            Assert.True(kinds == 1, $"{delta.Key} needs one of an exchange, a status or Partial.");
            Assert.True(delta.Value.AnswerOf is null || LegacyContract.Exchanges.ContainsKey(delta.Value.AnswerOf), delta.Value.AnswerOf);

            Assert.True(
                delta.Value.Status is null or (>= 400 and not 405) && delta.Value.Status != exchange.Status,
                $"{delta.Key}: a delta's own status is an error other than 405 and other than the recorded status.");

            Assert.True(
                !delta.Value.Partial || (exchange.RequestHeader("Range") is not null && exchange.BodyKind == "binary"),
                $"{delta.Key}: a Partial delta needs a Range request and a binary body.");

            Assert.True(entries.TryGetValue(delta.Value.Id, out var entry), $"{delta.Value.Id} is not an entry of the register.");
            Assert.Contains($"`{delta.Key}`", entry, StringComparison.Ordinal);
        });
    }

    [GeneratedRegex(@"^## Entries\r?$(?<entries>.*?)^## ", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex EntriesSection();

    [GeneratedRegex(@"^### ", RegexOptions.Multiline)]
    private static partial Regex EntryHeading();
}
