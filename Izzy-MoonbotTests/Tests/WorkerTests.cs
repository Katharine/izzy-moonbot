using Izzy_Moonbot;
using Izzy_Moonbot.Helpers;
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
    public async Task MentionResponse_ValidMention_SendsResponseAndUpdatesCooldown()
    {
        var (cfg, _, (izzyHerself, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch;
        cfg.MentionResponseEnabled = true;
        cfg.MentionResponses.Add("Hi!");
        var state = new TransientState();
        var testLogger = new TestLogger<Worker>();
        var context = await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, $"<@{izzyHerself.Id}> hello");

        await Worker.HandleMentionAsync(context, cfg, state, testLogger);

        Assert.HasCount(2, generalChannel.Messages);
        Assert.AreEqual(izzyHerself.Id, generalChannel.Messages.Last().AuthorId);
        Assert.AreEqual("Hi!", generalChannel.Messages.Last().Content);
        Assert.AreEqual(TestUtils.FiMEpoch, state.LastMentionResponse);
    }

    [TestMethod()]
    public async Task MentionResponse_Disabled_IgnoresMention()
    {
        var (cfg, _, (izzyHerself, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        cfg.MentionResponseEnabled = false;
        cfg.MentionResponses.Add("Hi!");
        var state = new TransientState();
        var testLogger = new TestLogger<Worker>();
        var context = await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, $"<@{izzyHerself.Id}> hello");

        await Worker.HandleMentionAsync(context, cfg, state, testLogger);

        Assert.HasCount(1, generalChannel.Messages);
    }

    [TestMethod()]
    public async Task MentionResponse_SelfMention_IgnoresMention()
    {
        var (cfg, _, (izzyHerself, _), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        cfg.MentionResponseEnabled = true;
        cfg.MentionResponses.Add("Hi!");
        var state = new TransientState();
        var testLogger = new TestLogger<Worker>();
        var context = await client.AddMessageAsync(guild.Id, generalChannel.Id, izzyHerself.Id, $"<@{izzyHerself.Id}> hello");

        await Worker.HandleMentionAsync(context, cfg, state, testLogger);

        Assert.HasCount(1, generalChannel.Messages);
    }

    [TestMethod()]
    public async Task MentionResponse_WithinCooldown_IgnoresMention()
    {
        var (cfg, _, (izzyHerself, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch.AddSeconds(30);
        cfg.MentionResponseEnabled = true;
        cfg.MentionResponseCooldown = 60;
        cfg.MentionResponses.Add("Hi!");
        var state = new TransientState { LastMentionResponse = TestUtils.FiMEpoch };
        var testLogger = new TestLogger<Worker>();
        var context = await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, $"<@{izzyHerself.Id}> hello");

        await Worker.HandleMentionAsync(context, cfg, state, testLogger);

        Assert.HasCount(1, generalChannel.Messages);
        Assert.AreEqual(TestUtils.FiMEpoch, state.LastMentionResponse);
    }

    [TestMethod()]
    public async Task MentionResponse_EmptyMentionResponses_DoesNotThrowOrUpdateCooldown()
    {
        var (cfg, _, (izzyHerself, sunny), _, (generalChannel, _, _), guild, client) = TestUtils.DefaultStubs();
        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch;
        cfg.MentionResponseEnabled = true;
        var state = new TransientState();
        var testLogger = new TestLogger<Worker>();
        var context = await client.AddMessageAsync(guild.Id, generalChannel.Id, sunny.Id, $"<@{izzyHerself.Id}> hello");

        await Worker.HandleMentionAsync(context, cfg, state, testLogger);

        Assert.HasCount(1, generalChannel.Messages);
        Assert.AreEqual(DateTimeOffset.MinValue, state.LastMentionResponse);
    }
}
