using Izzy_Moonbot;
using Izzy_Moonbot.Adapters;
using Izzy_Moonbot.EventListeners;
using Izzy_Moonbot.Helpers;
using Izzy_Moonbot.Service;
using Izzy_Moonbot.Settings;
using Izzy_Moonbot_Tests.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Izzy_Moonbot_Tests.EventListeners;

[TestClass()]
public class MessageListenerTests
{
    [TestCleanup]
    public void Cleanup()
    {
        DiscordHelper.DefaultGuildId = null;
        DiscordHelper.DevUserIds = null;
        DiscordHelper.PleaseAwaitEvents = false;
    }

    private static StubBooruClient SetupMessageListener(
        Config cfg, StubGuild guild, StubClient client, BooruSearchResult[]? booruResults = null)
    {
        DiscordHelper.DefaultGuildId = guild.Id;
        DiscordHelper.DevUserIds = new List<ulong>();
        DiscordHelper.PleaseAwaitEvents = true;

        var testLogger = new TestLogger<Worker>();
        var logger = new LoggingService(testLogger);
        var modLog = new ModLoggingService(cfg);
        var state = new TransientState();
        var booruClient = new StubBooruClient(booruResults ?? []);
        var templateService = new TemplateService(booruClient, cfg, logger);
        var ml = new MessageListener(logger, cfg, modLog, state, templateService);

        ml.RegisterEvents(client);
        return booruClient;
    }

    [TestMethod()]
    public async Task Witty_TemplateBlock_ResolvesBeforeSending()
    {
        var (cfg, _, (izzyHerself, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        var booruClient = SetupMessageListener(
            cfg, guild, client, booruResults: [new BooruSearchResult.Found("https://manebooru.art/images/4023150", false)]);
        cfg.WittyChannels.Add(generalChannel.Id);
        cfg.BooruFilterId = 42;
        cfg.Witties["pony pic"] = "Look at this pony: {{booru: izzy moonbow, safe}}!";

        await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, "show me a pony pic please");

        Assert.HasCount(2, generalChannel.Messages);
        Assert.AreEqual(izzyHerself.Id, generalChannel.Messages.Last().AuthorId);
        Assert.AreEqual("Look at this pony: https://manebooru.art/images/4023150!", generalChannel.Messages.Last().Content);
        CollectionAssert.AreEqual(
            new[] { ("izzy moonbow, safe", (int?)42) },
            booruClient.Calls);
    }

    [TestMethod()]
    public async Task Witty_BooruOrQuery_IsNotSplitAsPipeAlternative()
    {
        var (cfg, _, (_, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        var booruClient = SetupMessageListener(
            cfg, guild, client, booruResults: [new BooruSearchResult.Found("https://manebooru.art/images/4023150", false)]);
        cfg.WittyChannels.Add(generalChannel.Id);
        cfg.Witties["best friends"] = "{{booru: izzy moonbow || sunny starscout}}";

        await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, "best friends");

        Assert.AreEqual("https://manebooru.art/images/4023150", generalChannel.Messages.Last().Content);
        CollectionAssert.AreEqual(
            new[] { ("izzy moonbow || sunny starscout", (int?)null) },
            booruClient.Calls);
    }

    [TestMethod()]
    public async Task Witty_PipeAlternativesWithBooruBlocks_OnlyQueriesSelectedAlternative()
    {
        var (cfg, _, (_, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        var booruClient = SetupMessageListener(
            cfg, guild, client, booruResults: [new BooruSearchResult.Found("https://manebooru.art/images/4023150", false)]);
        cfg.WittyChannels.Add(generalChannel.Id);
        cfg.Witties["either"] = "{{booru: pipp petals}}|{{booru: zipp storm}}";

        await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, "either");

        Assert.AreEqual("https://manebooru.art/images/4023150", generalChannel.Messages.Last().Content);
        Assert.HasCount(1, booruClient.Calls);
        CollectionAssert.Contains(
            new[] { ("pipp petals", (int?)null), ("zipp storm", (int?)null) },
            booruClient.Calls.Single());
    }

    [TestMethod()]
    public async Task Witty_PipeAlternativesContainingBooruOrQueries_SplitsOnlyOuterPipe()
    {
        var (cfg, _, (_, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        var booruClient = SetupMessageListener(
            cfg, guild, client, booruResults: [new BooruSearchResult.Found("https://manebooru.art/images/4023150", false)]);
        cfg.WittyChannels.Add(generalChannel.Id);
        cfg.Witties["duos"] = "{{booru: izzy moonbow || sunny starscout}}|{{booru: pipp petals || zipp storm}}";

        await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, "duos");

        Assert.AreEqual("https://manebooru.art/images/4023150", generalChannel.Messages.Last().Content);
        Assert.HasCount(1, booruClient.Calls);
        CollectionAssert.Contains(
            new[]
            {
                ("izzy moonbow || sunny starscout", (int?)null),
                ("pipp petals || zipp storm", (int?)null)
            },
            booruClient.Calls.Single());
    }
}
