using Flurl.Http.Testing;
using Izzy_Moonbot;
using Izzy_Moonbot.Adapters;
using Izzy_Moonbot.Service;
using Izzy_Moonbot.Settings;
using Izzy_Moonbot_Tests.Services;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Izzy_Moonbot_Tests.Adapters;

[TestClass()]
public class BooruClientTests
{
    private static (BooruClient Client, TestLogger<Worker> TestLogger) SetupBooruClient(
        string token = "super-secret-booru-token")
    {
        var settings = Options.Create(new BooruSettings
        {
            Endpoint = "https://manebooru.art",
            Version = "v1",
            Token = token
        });
        var testLogger = new TestLogger<Worker>();
        var logger = new LoggingService(testLogger);
        return (new BooruClient(settings, logger), testLogger);
    }

    [TestMethod()]
    public async Task SearchRandomImage_MatchingImage_ReturnsUrlAndSendsExpectedQueryParams()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWithJson(new
        {
            images = new[]
            {
                new { id = 4023150L, spoilered = false }
            }
        });

        var result = await client.SearchRandomImageAsync("izzy moonbow, safe", filterId: 42);

        Assert.AreEqual(new BooruSearchResult.Found("https://manebooru.art/images/4023150", false), result);
        httpTest.ShouldHaveCalled("https://manebooru.art/api/v1/json/search/images*")
            .WithVerb(HttpMethod.Get)
            .WithQueryParam("q", "izzy moonbow, safe")
            .WithQueryParam("sf", "random")
            .WithQueryParam("key", "super-secret-booru-token")
            .WithQueryParam("filter_id", "42")
            .Times(1);
    }

    [TestMethod()]
    public async Task SearchRandomImage_WithoutFilterId_OmitsFilterIdQueryParam()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWithJson(new { images = new[] { new { id = 4023150L } } });

        await client.SearchRandomImageAsync("derpy hooves");

        httpTest.ShouldHaveCalled("https://manebooru.art/api/v1/json/search/images*")
            .WithoutQueryParam("filter_id")
            .Times(1);
    }

    [TestMethod()]
    public async Task SearchRandomImage_EmptyToken_OmitsKeyQueryParam()
    {
        var (client, _) = SetupBooruClient(token: "");
        using var httpTest = new HttpTest();
        httpTest.RespondWithJson(new { images = new[] { new { id = 4023150L } } });

        var result = await client.SearchRandomImageAsync("izzy moonbow");

        Assert.AreEqual(new BooruSearchResult.Found("https://manebooru.art/images/4023150", false), result);
        httpTest.ShouldHaveCalled("https://manebooru.art/api/v1/json/search/images*")
            .WithoutQueryParam("key")
            .Times(1);
    }

    [TestMethod()]
    public async Task SearchRandomImage_EmptyTokenOnFailure_ReturnsErrorWithoutThrowing()
    {
        var (client, _) = SetupBooruClient(token: "");
        using var httpTest = new HttpTest();
        httpTest.RespondWith("Internal Server Error", 500);

        var result = await client.SearchRandomImageAsync("server error");

        Assert.AreEqual(new BooruSearchResult.HttpError(500), result);
    }

    [TestMethod()]
    public async Task SearchRandomImage_SpoileredImage_ReturnsSpoileredTrue()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWithJson(new
        {
            images = new[]
            {
                new { id = 4023150L, spoilered = true }
            }
        });

        var result = await client.SearchRandomImageAsync("pony");

        Assert.AreEqual(new BooruSearchResult.Found("https://manebooru.art/images/4023150", true), result);
    }

    [TestMethod()]
    public async Task SearchRandomImage_EmptyResults_ReturnsNotFound()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWithJson(new { images = Array.Empty<object>(), total = 0 });

        var result = await client.SearchRandomImageAsync("nonexistent_tag_12345");

        Assert.AreEqual(new BooruSearchResult.NotFound(), result);
    }

    [TestMethod()]
    public async Task SearchRandomImage_WhitespaceQuery_ReturnsNotFoundWithoutCallingApi()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();

        var result = await client.SearchRandomImageAsync("   ");

        Assert.AreEqual(new BooruSearchResult.NotFound(), result);
        httpTest.ShouldNotHaveMadeACall();
    }

    [TestMethod()]
    public async Task SearchRandomImage_HttpError_ReturnsHttpErrorWithStatusCode()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWith("Service Unavailable", 503);

        var result = await client.SearchRandomImageAsync("server error");

        Assert.AreEqual(new BooruSearchResult.HttpError(503), result);
    }

    [TestMethod()]
    public async Task SearchRandomImage_Timeout_ReturnsTimedOut()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.SimulateTimeout();

        var result = await client.SearchRandomImageAsync("slow query");

        Assert.AreEqual(new BooruSearchResult.TimedOut(), result);
    }

    [TestMethod()]
    public async Task SearchRandomImage_NetworkException_ReturnsError()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.SimulateException(new HttpRequestException("Connection refused"));

        var result = await client.SearchRandomImageAsync("network down");

        Assert.AreEqual(new BooruSearchResult.Error(), result);
    }

    [TestMethod()]
    public async Task SearchRandomImage_InvalidJson_ReturnsError()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWith("<html>not json</html>", 200);

        var result = await client.SearchRandomImageAsync("malformed response");

        Assert.AreEqual(new BooruSearchResult.Error(), result);
    }

    [TestMethod()]
    public async Task SearchRandomImage_ApiFailure_RedactsSecretInLogs()
    {
        var (client, testLogger) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWith("Internal Server Error", 500);

        await client.SearchRandomImageAsync("server error");

        foreach (var log in testLogger.Logs)
        {
            Assert.DoesNotContain("super-secret-booru-token", log);
        }
        Assert.IsTrue(testLogger.Logs.Any(l => l.Contains("<REDACTED SECRET>")));
    }

    [TestMethod()]
    public async Task GetFeaturedImage_ValidPayload_ParsesImageAndRepresentations()
    {
        var (client, _) = SetupBooruClient();
        var createdAt = new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        using var httpTest = new HttpTest();
        httpTest.RespondWithJson(new
        {
            image = new
            {
                id = 4023150L,
                created_at = createdAt,
                format = "png",
                spoilered = false,
                thumbnails_generated = true
            }
        });

        var image = await client.GetFeaturedImageAsync();

        Assert.AreEqual(4023150L, image.Id);
        Assert.AreEqual(BooruImageFormat.PNG, image.Format);
        Assert.IsFalse(image.Spoilered);
        Assert.IsTrue(image.ThumbnailsGenerated);
        Assert.AreEqual("https://static.manebooru.art/img/2026/4/10/4023150/thumb.png", image.Representations?.Thumbnail);
        httpTest.ShouldHaveCalled("https://manebooru.art/api/v1/json/images/featured*")
            .WithVerb(HttpMethod.Get)
            .WithQueryParam("key", "super-secret-booru-token")
            .Times(1);
    }

    [TestMethod()]
    public async Task GetFeaturedImage_ApiFailure_ThrowsBooruExceptionWithRedactedSecret()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWith("Internal Server Error", 500);

        var ex = await Assert.ThrowsExactlyAsync<BooruException>(() => client.GetFeaturedImageAsync());

        Assert.DoesNotContain("super-secret-booru-token", ex.Message);
        StringAssert.Contains(ex.Message, "<REDACTED SECRET>");
    }

    [TestMethod()]
    public async Task GetFeaturedImage_InvalidJson_ThrowsBooruException()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWith("<html>not json</html>", 200);

        await Assert.ThrowsExactlyAsync<BooruException>(() => client.GetFeaturedImageAsync());
    }

    [TestMethod()]
    public async Task OpenImageStream_ValidUrl_ReturnsResponseStream()
    {
        var (client, _) = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWith("fake-image-bytes", 200);

        using var stream = await client.OpenImageStreamAsync("https://static.manebooru.art/img/2026/4/10/4023150/thumb.png");

        using var reader = new StreamReader(stream);
        Assert.AreEqual("fake-image-bytes", await reader.ReadToEndAsync());
        httpTest.ShouldHaveCalled("https://static.manebooru.art/img/2026/4/10/4023150/thumb.png")
            .WithVerb(HttpMethod.Get)
            .Times(1);
    }
}
