using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Flurl.Http;
using Izzy_Moonbot.Service;
using Izzy_Moonbot.Settings;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Izzy_Moonbot.Adapters;

public abstract record BooruSearchResult
{
    private BooruSearchResult() { }

    public sealed record Found(string Url, bool Spoilered) : BooruSearchResult;
    public sealed record NotFound : BooruSearchResult;
    public sealed record TimedOut : BooruSearchResult;
    public sealed record HttpError(int StatusCode) : BooruSearchResult;
    public sealed record Error : BooruSearchResult;
}

public class BooruException : Exception
{
    public BooruException(string message) : base(message) { }
}

public interface IBooruClient
{
    Task<BooruImage> GetFeaturedImageAsync();
    Task<Stream> OpenImageStreamAsync(string url);
    Task<BooruSearchResult> SearchRandomImageAsync(string query, int? filterId = null);
}

public class BooruClient : IBooruClient
{
    private const string UserAgent = "Izzy-Moonbot (Linux x86_64) Flurl.Http/3.2.4 DotNET/8.0";

    private readonly BooruSettings _booruSettings;
    private readonly LoggingService _logger;

    public BooruClient(IOptions<BooruSettings> booruSettings, LoggingService logger)
    {
        _booruSettings = booruSettings.Value;
        _logger = logger;
    }

    public async Task<BooruImage> GetFeaturedImageAsync()
    {
        try
        {
            var request = $"{_booruSettings.Endpoint}/api/{_booruSettings.Version}/json/images/featured"
                .WithHeader("user-agent", UserAgent);

            if (!string.IsNullOrWhiteSpace(_booruSettings.Token))
                request = request.SetQueryParam("key", _booruSettings.Token);

            var results = await request
                .GetAsync()
                .ReceiveJson<BooruFeaturedResponse>();

            if (results?.Image == null)
                throw new BooruException("Booru featured image response was empty.");

            var format = BooruImageFormat.Parse(results.Image.Format);

            return new BooruImage
            {
                CreatedAt = results.Image.CreatedAt,
                Id = results.Image.Id,
                Format = format,
                Spoilered = results.Image.Spoilered,
                ThumbnailsGenerated = results.Image.ThumbnailsGenerated,
                Representations = new BooruImagesRepresentations(results.Image.Id, format, results.Image.CreatedAt)
            };
        }
        catch (FlurlHttpException ex)
        {
            throw new BooruException(RedactSecrets($"[{ex.GetType().Name}] {ex.Message}"));
        }
    }

    public async Task<Stream> OpenImageStreamAsync(string url)
    {
        try
        {
            return await url
                .WithHeader("user-agent", UserAgent)
                .GetStreamAsync();
        }
        catch (FlurlHttpException ex)
        {
            throw new BooruException(RedactSecrets($"[{ex.GetType().Name}] {ex.Message}"));
        }
    }

    public async Task<BooruSearchResult> SearchRandomImageAsync(string query, int? filterId = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new BooruSearchResult.NotFound();

        try
        {
            var request = $"{_booruSettings.Endpoint}/api/{_booruSettings.Version}/json/search/images"
                .WithHeader("user-agent", UserAgent)
                .WithTimeout(TimeSpan.FromSeconds(3))
                .SetQueryParam("q", query.Trim())
                .SetQueryParam("sf", "random")
                .SetQueryParam("per_page", 1);

            if (!string.IsNullOrWhiteSpace(_booruSettings.Token))
                request = request.SetQueryParam("key", _booruSettings.Token);

            if (filterId.HasValue)
                request = request.SetQueryParam("filter_id", filterId.Value);

            var results = await request
                .GetAsync()
                .ReceiveJson<BooruSearchResponse>();

            var chosen = results?.Images.FirstOrDefault();
            if (chosen == null)
                return new BooruSearchResult.NotFound();

            return new BooruSearchResult.Found($"{_booruSettings.Endpoint}/images/{chosen.Id}", chosen.Spoilered);
        }
        catch (FlurlHttpTimeoutException ex)
        {
            _logger.Log(RedactSecrets(
                $"Timed out querying booru for \"{query}\": [{ex.GetType().Name}] {ex.Message}"));
            return new BooruSearchResult.TimedOut();
        }
        catch (FlurlHttpException ex) when (ex is not FlurlParsingException && ex.StatusCode is int statusCode)
        {
            _logger.Log(RedactSecrets(
                $"HTTP {statusCode} error querying booru for \"{query}\": [{ex.GetType().Name}] {ex.Message}"));
            return new BooruSearchResult.HttpError(statusCode);
        }
        catch (FlurlHttpException ex)
        {
            _logger.Log(RedactSecrets(
                $"Failed to query booru for \"{query}\": [{ex.GetType().Name}] {ex.Message}"));
            return new BooruSearchResult.Error();
        }
    }

    private string RedactSecrets(string text) =>
        string.IsNullOrWhiteSpace(_booruSettings.Token)
            ? text
            : text.Replace(_booruSettings.Token, "<REDACTED SECRET>");

    private class BooruFeaturedResponse
    {
        [JsonProperty("image", Required = Required.Always)]
        public BooruFeaturedImageDto Image { get; set; } = null!;
    }

    private class BooruFeaturedImageDto
    {
        [JsonProperty("id", Required = Required.Always)]
        public long Id { get; set; }

        [JsonProperty("created_at", Required = Required.Always)]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonProperty("format", Required = Required.Always)]
        public string Format { get; set; } = string.Empty;

        [JsonProperty("spoilered")]
        public bool Spoilered { get; set; }

        [JsonProperty("thumbnails_generated")]
        public bool ThumbnailsGenerated { get; set; }
    }

    private class BooruSearchResponse
    {
        [JsonProperty("images", Required = Required.Always)]
        public List<BooruSearchImageDto> Images { get; set; } = new();
    }

    private class BooruSearchImageDto
    {
        [JsonProperty("id", Required = Required.Always)]
        public long Id { get; set; }

        [JsonProperty("spoilered")]
        public bool Spoilered { get; set; }
    }
}

public class BooruImage
{
    public DateTimeOffset CreatedAt { get; set; }
    public BooruImageFormat Format { get; set; }
    public long Id { get; set; }
    public BooruImagesRepresentations? Representations { get; set; }
    public bool Spoilered { get; set; }
    public bool ThumbnailsGenerated { get; set; }

    public BooruImage() { }
}

public class BooruImagesRepresentations
{
    public string Full { get; }
    private string _rawUrl;
    private BooruImageFormat _format;

    public BooruImagesRepresentations(long id, BooruImageFormat format, DateTimeOffset createdAt)
    {
        Full =
            $"https://static.manebooru.art/img/view/{createdAt.Year}/{createdAt.Month}/{createdAt.Day}/{id}.{format.ToFileExtension()}";
        _rawUrl = $"https://static.manebooru.art/img/{createdAt.Year}/{createdAt.Month}/{createdAt.Day}/{id}";
        _format = format;
    }

    private string _getRepresentation(string key)
    {
        return $"{_rawUrl}/{key}.{_format.ToFileExtension()}";
    }

    public string Large => _getRepresentation("large");
    public string Medium => _getRepresentation("medium");
    public string Small => _getRepresentation("small");
    public string Tall => _getRepresentation("tall");
    public string Thumbnail => _getRepresentation("thumb");
    public string ThumbnailSmall => _getRepresentation("thumb_small");
    public string ThumbnailTiny => _getRepresentation("thumb_tiny");
}

public enum BooruImageFormat
{
    PNG,
    JPG,
    JPEG,
    SVG,
    WebM,
    GIF
}

public static class BooruImageFormatExtensions
{
    extension(BooruImageFormat format)
    {
        public static BooruImageFormat Parse(string value) => value switch
        {
            "png" => BooruImageFormat.PNG,
            "jpg" => BooruImageFormat.JPG,
            "jpeg" => BooruImageFormat.JPEG,
            "svg" => BooruImageFormat.SVG,
            "webm" => BooruImageFormat.WebM,
            "gif" => BooruImageFormat.GIF,
            _ => throw new BooruException($"Unsupported booru image format \"{value}\".")
        };

        public string ToFileExtension() => format switch
        {
            BooruImageFormat.PNG => "png",
            BooruImageFormat.JPG => "jpg",
            BooruImageFormat.JPEG => "jpeg",
            BooruImageFormat.SVG => "svg",
            BooruImageFormat.WebM => "webm",
            BooruImageFormat.GIF => "gif",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }
}
