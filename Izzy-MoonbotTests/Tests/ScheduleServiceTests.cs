using Microsoft.VisualStudio.TestTools.UnitTesting;
using Izzy_Moonbot.Adapters;
using Izzy_Moonbot.Settings;
using Izzy_Moonbot.Service;
using Izzy_Moonbot.Helpers;
using Izzy_Moonbot;

namespace Izzy_Moonbot_Tests.Services;

[TestClass()]
public class ScheduleServiceTests
{
    [TestCleanup]
    public void Cleanup()
    {
        DateTimeHelper.FakeUtcNow = null;
        DiscordHelper.DefaultGuildId = null;
    }

    public static ScheduleService SetupScheduleService(
        Config cfg, Dictionary<ulong, User> users, IBooruClient? booruClient = null)
    {
        var scheduledJobs = new List<ScheduledJob>();

        var mod = new ModService(cfg, users);
        var modLog = new ModLoggingService(cfg);
        var logger = new LoggingService(new TestLogger<Worker>());
        var resolvedBooruClient = booruClient ?? new StubBooruClient();
        var templateService = new TemplateService(resolvedBooruClient, cfg, logger);

        return new ScheduleService(cfg, mod, modLog, logger, scheduledJobs, resolvedBooruClient, templateService);
    }

    [TestMethod()]
    public async Task Breathing_Tests()
    {
        var (cfg, _, (_, sunny), _, (generalChannel, modChat, _), guild, client) = TestUtils.DefaultStubs();
        DiscordHelper.DefaultGuildId = guild.Id;
        cfg.ModChannel = modChat.Id;

        // ModService assumes that every user it's asked to silence is already in the users Dictionary
        var users = new Dictionary<ulong, User>();
        users[sunny.Id] = new User();

        var ss = SetupScheduleService(cfg, users);

        Assert.IsEmpty(generalChannel.Messages);

        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch;

        await ss.Unicycle(client);
        Assert.IsEmpty(generalChannel.Messages);

        var action = new ScheduledEchoJob(generalChannel.Id, "test echo");
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow.AddMinutes(2), action));

        DateTimeHelper.FakeUtcNow = DateTimeHelper.FakeUtcNow?.AddMinutes(1);

        await ss.Unicycle(client);
        Assert.IsEmpty(generalChannel.Messages);

        DateTimeHelper.FakeUtcNow = DateTimeHelper.FakeUtcNow?.AddMinutes(1);

        await ss.Unicycle(client);
        Assert.HasCount(1, generalChannel.Messages);
        Assert.AreEqual("test echo", generalChannel.Messages.Last().Content);
    }

    // Shuffle/Rotate BannerRotation isn't worth testing here because DiscordHelper.SetBannerToUrlImage makes direct network calls;
    // ManebooruFeatured BannerRotation is tested below via IBooruClient
    // EndRaid makes little sense to test in isolation from the rest of the raid logic
    [TestMethod()]
    public async Task RunBasicJobTypes_Tests()
    {
        var (cfg, _, (_, sunny), roles, (generalChannel, modChat, _), guild, client) = TestUtils.DefaultStubs();
        DiscordHelper.DefaultGuildId = guild.Id;
        cfg.ModChannel = modChat.Id;

        var alicorn = roles[0];

        ulong sproutId = 10;
        guild.BannedUserIds.Add(sproutId);

        var users = new Dictionary<ulong, User>();
        guild.Users.ForEach(u => users[u.Id] = new User());
        users[sproutId] = new User();

        var ss = SetupScheduleService(cfg, users);

        // assert initial state
        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch;
        TestUtils.AssertListsAreEqual(new List<ulong> { alicorn.Id }, guild.UserRoles[sunny.Id]);
        Assert.IsFalse(client.DirectMessages.ContainsKey(sunny.Id));
        TestUtils.AssertSetsAreEqual(new HashSet<ulong> { sproutId }, guild.BannedUserIds);

        // should be a no-op
        await ss.Unicycle(client);

        TestUtils.AssertListsAreEqual(new List<ulong> { alicorn.Id }, guild.UserRoles[sunny.Id]);
        Assert.IsFalse(client.DirectMessages.ContainsKey(sunny.Id));
        TestUtils.AssertSetsAreEqual(new HashSet<ulong> { sproutId }, guild.BannedUserIds);

        var removeRoleAction = new ScheduledRoleRemovalJob(alicorn.Id, sunny.Id, "somepony stole the crystals");
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow.AddMinutes(1), removeRoleAction));

        var echoAction = new ScheduledEchoJob(sunny.Id, "your sparkle is rainbow!");
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow.AddMinutes(2), echoAction));

        var addRoleAction = new ScheduledRoleAdditionJob(alicorn.Id, sunny.Id, "found the unity crystals");
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow.AddMinutes(3), addRoleAction));

        var unbanAction = new ScheduledUnbanJob(sproutId, null);
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow.AddMinutes(5), unbanAction));

        // still a no-op, no time has passed
        await ss.Unicycle(client);

        TestUtils.AssertListsAreEqual(new List<ulong> { alicorn.Id }, guild.UserRoles[sunny.Id]);
        Assert.IsFalse(client.DirectMessages.ContainsKey(sunny.Id));
        TestUtils.AssertSetsAreEqual(new HashSet<ulong> { sproutId }, guild.BannedUserIds);

        // RemoveRole should happen now
        DateTimeHelper.FakeUtcNow = DateTimeHelper.FakeUtcNow?.AddMinutes(1);
        await ss.Unicycle(client);

        TestUtils.AssertListsAreEqual(new List<ulong>(), guild.UserRoles[sunny.Id]); // role removed
        Assert.IsFalse(client.DirectMessages.ContainsKey(sunny.Id));
        TestUtils.AssertSetsAreEqual(new HashSet<ulong> { sproutId }, guild.BannedUserIds);

        // Echo should happen now
        DateTimeHelper.FakeUtcNow = DateTimeHelper.FakeUtcNow?.AddMinutes(1);
        await ss.Unicycle(client);

        TestUtils.AssertListsAreEqual(new List<ulong>(), guild.UserRoles[sunny.Id]);
        TestUtils.AssertListsAreEqual(new List<string> { "your sparkle is rainbow!" }, client.DirectMessages[sunny.Id].Select(sm => sm.Content).ToList()); // DM added
        TestUtils.AssertSetsAreEqual(new HashSet<ulong> { sproutId }, guild.BannedUserIds);

        // AddRole should happen now
        DateTimeHelper.FakeUtcNow = DateTimeHelper.FakeUtcNow?.AddMinutes(1);
        await ss.Unicycle(client);

        TestUtils.AssertListsAreEqual(new List<ulong> { alicorn.Id }, guild.UserRoles[sunny.Id]); // role added
        TestUtils.AssertListsAreEqual(new List<string> { "your sparkle is rainbow!" }, client.DirectMessages[sunny.Id].Select(sm => sm.Content).ToList());
        TestUtils.AssertSetsAreEqual(new HashSet<ulong> { sproutId }, guild.BannedUserIds);

        // Unban should happen now
        DateTimeHelper.FakeUtcNow = DateTimeHelper.FakeUtcNow?.AddMinutes(2);
        await ss.Unicycle(client);

        TestUtils.AssertListsAreEqual(new List<ulong> { alicorn.Id }, guild.UserRoles[sunny.Id]);
        TestUtils.AssertListsAreEqual(new List<string> { "your sparkle is rainbow!" }, client.DirectMessages[sunny.Id].Select(sm => sm.Content).ToList());
        TestUtils.AssertSetsAreEqual(new HashSet<ulong>(), guild.BannedUserIds); // sprout's back in
    }

    [TestMethod()]
    public async Task DailyEcho_Tests()
    {
        var (cfg, _, (_, sunny), _, (generalChannel, modChat, _), guild, client) = TestUtils.DefaultStubs();
        DiscordHelper.DefaultGuildId = guild.Id;
        cfg.ModChannel = modChat.Id;
        var ss = SetupScheduleService(cfg, new Dictionary<ulong, User>());

        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2010, 10, 10, 0, 0, 0, TimeSpan.Zero);
        await ss.Unicycle(client);
        Assert.IsEmpty(generalChannel.Messages);

        var action = new ScheduledEchoJob(generalChannel.Id, "test echo");
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow, action, ScheduledJobRepeatType.Daily));

        await ss.Unicycle(client);
        Assert.HasCount(1, generalChannel.Messages);
        Assert.AreEqual("test echo", generalChannel.Messages.Last().Content);

        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2010, 10, 10, 23, 59, 59, TimeSpan.Zero);
        await ss.Unicycle(client);
        Assert.HasCount(1, generalChannel.Messages);

        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2010, 10, 11, 0, 0, 0, TimeSpan.Zero);
        await ss.Unicycle(client);
        Assert.HasCount(2, generalChannel.Messages);
        Assert.AreEqual("test echo", generalChannel.Messages.Last().Content);
    }

    [TestMethod()]
    public async Task WeeklyEcho_Tests()
    {
        var (cfg, _, (_, sunny), _, (generalChannel, modChat, _), guild, client) = TestUtils.DefaultStubs();
        DiscordHelper.DefaultGuildId = guild.Id;
        cfg.ModChannel = modChat.Id;
        var ss = SetupScheduleService(cfg, new Dictionary<ulong, User>());

        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2010, 10, 10, 0, 0, 0, TimeSpan.Zero);
        await ss.Unicycle(client);
        Assert.IsEmpty(generalChannel.Messages);

        var action = new ScheduledEchoJob(generalChannel.Id, "test echo");
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow, action, ScheduledJobRepeatType.Weekly));

        await ss.Unicycle(client);
        Assert.HasCount(1, generalChannel.Messages);
        Assert.AreEqual("test echo", generalChannel.Messages.Last().Content);

        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2010, 10, 16, 23, 59, 59, TimeSpan.Zero);
        await ss.Unicycle(client);
        Assert.HasCount(1, generalChannel.Messages);

        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2010, 10, 17, 0, 0, 0, TimeSpan.Zero);
        await ss.Unicycle(client);
        Assert.HasCount(2, generalChannel.Messages);
        Assert.AreEqual("test echo", generalChannel.Messages.Last().Content);
    }

    [TestMethod()]
    public async Task YearlyEcho_Tests()
    {
        var (cfg, _, (_, sunny), _, (generalChannel, modChat, _), guild, client) = TestUtils.DefaultStubs();
        DiscordHelper.DefaultGuildId = guild.Id;
        cfg.ModChannel = modChat.Id;
        var ss = SetupScheduleService(cfg, new Dictionary<ulong, User>());

        // start the yearly echo on Oct 10th 2011
        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2011, 10, 10, 0, 0, 0, TimeSpan.Zero);
        await ss.Unicycle(client);
        Assert.IsEmpty(generalChannel.Messages);

        var action = new ScheduledEchoJob(generalChannel.Id, "test echo");
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow, action, ScheduledJobRepeatType.Yearly));

        await ss.Unicycle(client);
        Assert.HasCount(1, generalChannel.Messages);
        Assert.AreEqual("test echo", generalChannel.Messages.Last().Content);


        // nothing happens on Oct 9th 2012
        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2012, 10, 9, 0, 0, 0, TimeSpan.Zero);
        await ss.Unicycle(client);
        // 2012 was a leap year, so if yearly repeats were naively mis-implemented as
        // 365 days/8760 hours/etc, the second echo would happen here prematurely
        Assert.HasCount(1, generalChannel.Messages);

        // second echo happens on Oct 10th 2012
        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2012, 10, 10, 0, 0, 0, TimeSpan.Zero);
        await ss.Unicycle(client);
        Assert.HasCount(2, generalChannel.Messages);
        Assert.AreEqual("test echo", generalChannel.Messages.Last().Content);


        // nothing happens on Oct 9th 2013
        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2013, 10, 9, 0, 0, 0, TimeSpan.Zero);
        await ss.Unicycle(client);
        Assert.HasCount(2, generalChannel.Messages);

        // third echo happens on Oct 10th 2013
        DateTimeHelper.FakeUtcNow = new DateTimeOffset(2013, 10, 10, 0, 0, 0, TimeSpan.Zero);
        await ss.Unicycle(client);
        Assert.HasCount(3, generalChannel.Messages);
        Assert.AreEqual("test echo", generalChannel.Messages.Last().Content);
    }

    [TestMethod()]
    public async Task BoredCommands_TemplateBlock_ResolvesBeforeSending()
    {
        var (cfg, _, _, _, (generalChannel, modChat, _), guild, client) = TestUtils.DefaultStubs();
        DiscordHelper.DefaultGuildId = guild.Id;
        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch;
        cfg.ModChannel = modChat.Id;
        cfg.BoredChannel = generalChannel.Id;
        cfg.BooruFilterId = 42;
        cfg.BoredCommands.Add("Idle pony: {{booru: izzy moonbow, safe}}!");
        var booruClient = new StubBooruClient(
            new BooruSearchResult.Found("https://manebooru.art/images/4023150", false));
        var ss = SetupScheduleService(cfg, new Dictionary<ulong, User>(), booruClient);
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow, new ScheduledBoredCommandsJob()));

        await ss.Unicycle(client);

        Assert.HasCount(1, generalChannel.Messages);
        Assert.AreEqual("Idle pony: https://manebooru.art/images/4023150!", generalChannel.Messages.Last().Content);
        CollectionAssert.AreEqual(
            new[] { ("izzy moonbow, safe", (int?)42) },
            booruClient.Calls);
    }

    [TestMethod()]
    public async Task BannerRotation_BooruFeatured_FetchesImageAndStreamViaBooruClient()
    {
        var (cfg, _, _, _, (generalChannel, modChat, _), guild, client) = TestUtils.DefaultStubs();
        DiscordHelper.DefaultGuildId = guild.Id;
        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch;
        cfg.ModChannel = modChat.Id;
        cfg.LogChannel = generalChannel.Id;
        cfg.BannerMode = Izzy_Moonbot.EventListeners.ConfigListener.BannerMode.ManebooruFeatured;
        var createdAt = new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        var booruClient = new StubBooruClient
        {
            FeaturedImage = new BooruImage
            {
                Id = 4023150L,
                CreatedAt = createdAt,
                Format = BooruImageFormat.PNG,
                Spoilered = false,
                ThumbnailsGenerated = true,
                Representations = new BooruImagesRepresentations(4023150L, BooruImageFormat.PNG, createdAt)
            }
        };
        var ss = SetupScheduleService(cfg, new Dictionary<ulong, User>(), booruClient);
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow, new ScheduledBannerRotationJob()));

        await ss.Unicycle(client);

        CollectionAssert.AreEqual(
            new[] { "https://static.manebooru.art/img/2026/4/10/4023150/thumb.png" },
            booruClient.StreamUrls);
        Assert.HasCount(1, generalChannel.Messages);
        Assert.AreEqual(
            "Set banner to <https://manebooru.art/images/4023150> for Manebooru featured image.",
            generalChannel.Messages.Last().Content);
    }

    [TestMethod()]
    public async Task BannerRotation_BooruFeaturedError_LogsModErrorWithoutFetchingStream()
    {
        var (cfg, _, _, _, (_, modChat, _), guild, client) = TestUtils.DefaultStubs();
        DiscordHelper.DefaultGuildId = guild.Id;
        DateTimeHelper.FakeUtcNow = TestUtils.FiMEpoch;
        cfg.ModChannel = modChat.Id;
        cfg.BannerMode = Izzy_Moonbot.EventListeners.ConfigListener.BannerMode.ManebooruFeatured;
        var booruClient = new StubBooruClient
        {
            FeaturedImageException = new BooruException("[FlurlHttpException] HTTP 503 (<REDACTED SECRET>)")
        };
        var ss = SetupScheduleService(cfg, new Dictionary<ulong, User>(), booruClient);
        await ss.CreateScheduledJob(new ScheduledJob(DateTimeHelper.UtcNow, DateTimeHelper.UtcNow, new ScheduledBannerRotationJob()));

        await ss.Unicycle(client);

        Assert.IsEmpty(booruClient.StreamUrls);
        Assert.HasCount(1, modChat.Messages);
        StringAssert.Contains(
            modChat.Messages.Last().Content,
            "Failed to change banner to the Manebooru featured image: [BooruException] [FlurlHttpException] HTTP 503 (<REDACTED SECRET>)");
    }
}
