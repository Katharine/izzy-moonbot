using Izzy_Moonbot;
using Izzy_Moonbot.Adapters;
using Izzy_Moonbot.Service;
using Izzy_Moonbot.Settings;
using Izzy_Moonbot_Tests.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Izzy_Moonbot_Tests.Services;

[TestClass()]
public class TemplateServiceTests
{
    private static (Config Config, StubBooruClient BooruClient, TemplateService Service) SetupTemplateService(
        BooruSearchResult[]? booruResults = null)
    {
        var cfg = new Config();
        var booruClient = new StubBooruClient(booruResults ?? []);
        var logger = new LoggingService(new TestLogger<Worker>());
        var service = new TemplateService(booruClient, cfg, logger);
        return (cfg, booruClient, service);
    }

    [TestMethod()]
    public async Task ResolveTemplates_PlainText_DoesNotCallBooruClient()
    {
        var (_, booruClient, service) = SetupTemplateService();

        var result = await service.ResolveTemplatesAsync("just plain text!");

        Assert.AreEqual("just plain text!", result);
        Assert.IsEmpty(booruClient.Calls);
    }

    [TestMethod()]
    public async Task ResolveTemplates_BooruInlineBlock_ReplacesBlockWithImageLink()
    {
        var (_, booruClient, service) = SetupTemplateService(
            booruResults: [new BooruSearchResult.Found("https://manebooru.art/images/4023150", false)]);

        var result = await service.ResolveTemplatesAsync(
            "Look at this pony: {{booru: izzy moonbow, safe}}!");

        Assert.AreEqual("Look at this pony: https://manebooru.art/images/4023150!", result);
        CollectionAssert.AreEqual(
            new[] { ("izzy moonbow, safe", (int?)null) },
            booruClient.Calls);
    }

    [TestMethod()]
    public async Task ResolveTemplates_MultipleBooruBlocks_QueriesEachBlockIndependently()
    {
        var (_, booruClient, service) = SetupTemplateService(
            booruResults:
            [
                new BooruSearchResult.Found("https://manebooru.art/images/101", false),
                new BooruSearchResult.Found("https://manebooru.art/images/202", false)
            ]);

        var result = await service.ResolveTemplatesAsync(
            "First: {{booru: izzy}} Second: {{booru: izzy}}");

        Assert.AreEqual("First: https://manebooru.art/images/101 Second: https://manebooru.art/images/202", result);
        CollectionAssert.AreEqual(
            new[] { ("izzy", (int?)null), ("izzy", (int?)null) },
            booruClient.Calls);
    }

    [TestMethod()]
    public async Task ResolveTemplates_GlobalFilterId_PassesFilterIdToBooruClient()
    {
        var (cfg, booruClient, service) = SetupTemplateService(
            booruResults: [new BooruSearchResult.Found("https://manebooru.art/images/4023150", false)]);
        cfg.BooruFilterId = 42;

        await service.ResolveTemplatesAsync("{{booru: derpy hooves}}");

        CollectionAssert.AreEqual(
            new[] { ("derpy hooves", (int?)42) },
            booruClient.Calls);
    }

    [TestMethod()]
    public async Task ResolveTemplates_BooruSpoileredImage_FormatsAsDiscordSpoiler()
    {
        var (_, _, service) = SetupTemplateService(
            booruResults: [new BooruSearchResult.Found("https://manebooru.art/images/4023150", true)]);

        var result = await service.ResolveTemplatesAsync("Result: {{booru: pony}}");

        Assert.AreEqual("Result: ||https://manebooru.art/images/4023150||", result);
    }

    [TestMethod()]
    public async Task ResolveTemplates_BooruNotFound_UsesFallbackText()
    {
        var (_, _, service) = SetupTemplateService(booruResults: [new BooruSearchResult.NotFound()]);

        var result = await service.ResolveTemplatesAsync("Image: {{booru: nonexistent_tag_12345}}");

        Assert.AreEqual("Image: [no image found]", result);
    }

    [TestMethod()]
    public async Task ResolveTemplates_BooruEmptyQuery_UsesFallbackWithoutCallingBooruClient()
    {
        var (_, booruClient, service) = SetupTemplateService();

        var result = await service.ResolveTemplatesAsync("Blank: {{booru:   }}");

        Assert.AreEqual("Blank: [no image found]", result);
        Assert.IsEmpty(booruClient.Calls);
    }

    [TestMethod()]
    public async Task ResolveTemplates_BooruHttpError_IncludesStatusCodeInFallback()
    {
        var (_, _, service) = SetupTemplateService(booruResults: [new BooruSearchResult.HttpError(503)]);

        var result = await service.ResolveTemplatesAsync("Broken: {{booru: server error}}!");

        Assert.AreEqual("Broken: [booru HTTP 503 error]!", result);
    }

    [TestMethod()]
    public async Task ResolveTemplates_BooruTimedOut_UsesTimeoutFallbackText()
    {
        var (_, _, service) = SetupTemplateService(booruResults: [new BooruSearchResult.TimedOut()]);

        var result = await service.ResolveTemplatesAsync("Slow: {{booru: slow query}}!");

        Assert.AreEqual("Slow: [booru request timed out]!", result);
    }

    [TestMethod()]
    public async Task ResolveTemplates_BooruError_UsesMiscErrorFallbackText()
    {
        var (_, _, service) = SetupTemplateService(booruResults: [new BooruSearchResult.Error()]);

        var result = await service.ResolveTemplatesAsync("Failed: {{booru: network down}}!");

        Assert.AreEqual("Failed: [booru error]!", result);
    }

    [TestMethod()]
    public void GetTemplateRanges_PlainText_ReturnsEmpty()
    {
        var (_, _, service) = SetupTemplateService();

        var ranges = service.GetTemplateRanges("plain text | more text");

        Assert.IsEmpty(ranges);
    }

    [TestMethod()]
    public void GetTemplateRanges_MultipleBlocks_ReturnsStartAndEndOffsets()
    {
        var (_, _, service) = SetupTemplateService();
        const string input = "a {{booru: izzy || sunny}}|{{booru: pipp}} b";

        var ranges = service.GetTemplateRanges(input);

        CollectionAssert.AreEqual(
            new[] { (2, 26), (27, 42) },
            ranges.ToArray());
    }
}
