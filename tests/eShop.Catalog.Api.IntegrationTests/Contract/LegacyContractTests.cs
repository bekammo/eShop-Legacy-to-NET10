using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace eShop.Catalog.Api.IntegrationTests.Contract;

// The golden exchanges of the ported endpoints, replayed against the new API (ADR-0002), in both modes of
// ADR-0017: on the class's database, and from memory. Each exchange is one case, named with the delta that changes its
// answer, if any. The replay spans every ported endpoint, so it has a folder of its own rather than one that mirrors a
// folder of the API (ADR-0007).
public sealed partial class LegacyContractTests(CatalogApiFactory factory, MockModeCatalogApi mockMode)
    : IClassFixture<CatalogApiFactory>, IClassFixture<MockModeCatalogApi>
{
    // As the exchanges were recorded: a redirect is not followed, which would hide its status, and no cookie is sent.
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

    // Each delta that the replay applies is an entry of the register, and the entry names the exchange. Only the
    // entries count: the table of upcoming deltas, after them, names exchanges too.
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
            Assert.True(LegacyContract.Exchanges.ContainsKey(delta.Value.AnswerOf), delta.Value.AnswerOf);
            Assert.True(entries.TryGetValue(delta.Value.Id, out var entry), $"{delta.Value.Id} is not an entry of the register.");
            Assert.Contains($"`{delta.Key}`", entry, StringComparison.Ordinal);
        });
    }

    [GeneratedRegex(@"^## Entries\r?$(?<entries>.*?)^## ", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex EntriesSection();

    [GeneratedRegex(@"^### ", RegexOptions.Multiline)]
    private static partial Regex EntryHeading();
}
