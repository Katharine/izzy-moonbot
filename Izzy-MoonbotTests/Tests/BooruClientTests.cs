using Flurl.Http.Testing;
using Izzy_Moonbot.Adapters;
using Izzy_Moonbot.Settings;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Izzy_Moonbot_Tests.Adapters;

[TestClass()]
public class BooruClientTests
{
    private static BooruClient SetupBooruClient(string token = "super-secret-booru-token")
    {
        var settings = Options.Create(new BooruSettings
        {
            Endpoint = "https://manebooru.art",
            Version = "v1",
            Token = token
        });
        return new BooruClient(settings);
    }

    [TestMethod()]
    public async Task GetFeaturedImage_ValidPayload_ParsesImageAndRepresentations()
    {
        var client = SetupBooruClient();
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
        var client = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWith("Internal Server Error", 500);

        var ex = await Assert.ThrowsExactlyAsync<BooruException>(() => client.GetFeaturedImageAsync());

        Assert.DoesNotContain("super-secret-booru-token", ex.Message);
        StringAssert.Contains(ex.Message, "<REDACTED SECRET>");
    }

    [TestMethod()]
    public async Task GetFeaturedImage_InvalidJson_ThrowsBooruException()
    {
        var client = SetupBooruClient();
        using var httpTest = new HttpTest();
        httpTest.RespondWith("<html>not json</html>", 200);

        await Assert.ThrowsExactlyAsync<BooruException>(() => client.GetFeaturedImageAsync());
    }

    [TestMethod()]
    public async Task OpenImageStream_ValidUrl_ReturnsResponseStream()
    {
        var client = SetupBooruClient();
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
