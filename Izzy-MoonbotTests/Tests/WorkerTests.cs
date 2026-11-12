using Izzy_Moonbot;
using Izzy_Moonbot.Adapters;
using Izzy_Moonbot.Helpers;
using Izzy_Moonbot.Service;
using Izzy_Moonbot.Settings;
using Izzy_Moonbot_Tests.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Izzy_Moonbot_Tests;

[TestClass()]
public class WorkerTests
{
    [TestCleanup]
    public void Cleanup()
    {
        DateTimeHelper.FakeUtcNow = null;
    }

    [TestMethod()]
    public async Task MentionResponse_TemplateBlock_ResolvesBeforeSending()
    {
        var (cfg, _, (izzyHerself, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch;
        cfg.MentionResponseEnabled = true;
        cfg.BooruFilterId = 42;
        cfg.MentionResponses.Add("Hi! {{booru: izzy moonbow, safe}}");
        var state = new TransientState();
        var testLogger = new TestLogger<Worker>();
        var booruClient = new StubBooruClient(
            new BooruSearchResult.Found("https://manebooru.art/images/4023150", false));
        var templateService = new TemplateService(booruClient, cfg, new LoggingService(testLogger));
        var context = await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, $"<@{izzyHerself.Id}> hello");

        await Worker.HandleMentionAsync(context, cfg, state, templateService, testLogger);

        Assert.HasCount(2, generalChannel.Messages);
        Assert.AreEqual(izzyHerself.Id, generalChannel.Messages.Last().AuthorId);
        Assert.AreEqual("Hi! https://manebooru.art/images/4023150", generalChannel.Messages.Last().Content);
        Assert.AreEqual(TestUtils.FiMEpoch, state.LastMentionResponse);
        CollectionAssert.AreEqual(
            new[] { ("izzy moonbow, safe", (int?)42) },
            booruClient.Calls);
    }

    [TestMethod()]
    public async Task MentionResponse_Disabled_IgnoresMentionWithoutCallingBooru()
    {
        var (cfg, _, (izzyHerself, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        cfg.MentionResponseEnabled = false;
        cfg.MentionResponses.Add("Hi! {{booru: izzy moonbow, safe}}");
        var state = new TransientState();
        var testLogger = new TestLogger<Worker>();
        var booruClient = new StubBooruClient();
        var templateService = new TemplateService(booruClient, cfg, new LoggingService(testLogger));
        var context = await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, $"<@{izzyHerself.Id}> hello");

        await Worker.HandleMentionAsync(context, cfg, state, templateService, testLogger);

        Assert.HasCount(1, generalChannel.Messages);
        Assert.IsEmpty(booruClient.Calls);
    }

    [TestMethod()]
    public async Task MentionResponse_SelfMention_IgnoresMentionWithoutCallingBooru()
    {
        var (cfg, _, (izzyHerself, _), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        cfg.MentionResponseEnabled = true;
        cfg.MentionResponses.Add("Hi! {{booru: izzy moonbow, safe}}");
        var state = new TransientState();
        var testLogger = new TestLogger<Worker>();
        var booruClient = new StubBooruClient();
        var templateService = new TemplateService(booruClient, cfg, new LoggingService(testLogger));
        var context = await client.AddMessageAsync(guild.Id, generalChannel.Id, izzyHerself.Id, $"<@{izzyHerself.Id}> hello");

        await Worker.HandleMentionAsync(context, cfg, state, templateService, testLogger);

        Assert.HasCount(1, generalChannel.Messages);
        Assert.IsEmpty(booruClient.Calls);
    }

    [TestMethod()]
    public async Task MentionResponse_WithinCooldown_IgnoresMentionWithoutCallingBooru()
    {
        var (cfg, _, (izzyHerself, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch.AddSeconds(30);
        cfg.MentionResponseEnabled = true;
        cfg.MentionResponseCooldown = 60;
        cfg.MentionResponses.Add("Hi! {{booru: izzy moonbow, safe}}");
        var state = new TransientState { LastMentionResponse = TestUtils.FiMEpoch };
        var testLogger = new TestLogger<Worker>();
        var booruClient = new StubBooruClient();
        var templateService = new TemplateService(booruClient, cfg, new LoggingService(testLogger));
        var context = await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, $"<@{izzyHerself.Id}> hello");

        await Worker.HandleMentionAsync(context, cfg, state, templateService, testLogger);

        Assert.HasCount(1, generalChannel.Messages);
        Assert.AreEqual(TestUtils.FiMEpoch, state.LastMentionResponse);
        Assert.IsEmpty(booruClient.Calls);
    }

    [TestMethod()]
    public async Task MentionResponse_EmptyMentionResponses_DoesNotThrowOrUpdateCooldown()
    {
        var (cfg, _, (izzyHerself, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch;
        cfg.MentionResponseEnabled = true;
        var state = new TransientState();
        var testLogger = new TestLogger<Worker>();
        var booruClient = new StubBooruClient();
        var templateService = new TemplateService(booruClient, cfg, new LoggingService(testLogger));
        var context = await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, $"<@{izzyHerself.Id}> hello");

        await Worker.HandleMentionAsync(context, cfg, state, templateService, testLogger);

        Assert.HasCount(1, generalChannel.Messages);
        Assert.AreEqual(DateTimeOffset.MinValue, state.LastMentionResponse);
        Assert.IsEmpty(booruClient.Calls);
    }
}
