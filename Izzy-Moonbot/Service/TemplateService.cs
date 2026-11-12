using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Izzy_Moonbot.Adapters;
using Izzy_Moonbot.Settings;

namespace Izzy_Moonbot.Service;

public class TemplateService
{
    private static readonly Regex BooruBlockRegex =
        new(@"\{\{booru:((?:(?!\{\{|\}\}).)*)\}\}", RegexOptions.Singleline);

    private readonly IBooruClient _booruClient;
    private readonly Config _config;
    private readonly LoggingService _logger;

    public TemplateService(IBooruClient booruClient, Config config, LoggingService logger)
    {
        _booruClient = booruClient;
        _config = config;
        _logger = logger;
    }

    public IReadOnlyList<(int Start, int End)> GetTemplateRanges(string text)
    {
        return BooruBlockRegex.Matches(text)
            .Select(m => (Start: m.Index, End: m.Index + m.Length))
            .ToList();
    }

    public async Task<string> ResolveTemplatesAsync(string text)
    {
        return await ResolveBooruBlocksAsync(text, _config.BooruFilterId);
    }

    private async Task<string> ResolveBooruBlocksAsync(string text, int? filterId)
    {
        var matches = BooruBlockRegex.Matches(text);
        if (matches.Count == 0) return text;

        var sb = new StringBuilder();
        var lastIndex = 0;

        foreach (Match blockMatch in matches)
        {
            sb.Append(text, lastIndex, blockMatch.Index - lastIndex);

            var query = blockMatch.Groups[1].Value.Trim();
            string replacement;

            if (string.IsNullOrWhiteSpace(query))
            {
                replacement = "[no image found]";
                _logger.Log($"Booru template block \"{blockMatch.Value}\" had an empty query.");
            }
            else
            {
                var result = await _booruClient.SearchRandomImageAsync(query, filterId);
                switch (result)
                {
                    case BooruSearchResult.Found(var imageUrl, var spoilered):
                        replacement = spoilered ? $"||{imageUrl}||" : imageUrl;
                        _logger.Log($"Resolved booru template block \"{blockMatch.Value}\" (filterId={filterId?.ToString() ?? "default"}, spoilered={spoilered}) to {imageUrl}");
                        break;
                    case BooruSearchResult.NotFound:
                        replacement = "[no image found]";
                        _logger.Log($"Booru template query \"{query}\" (filterId={filterId?.ToString() ?? "default"}) returned no images.");
                        break;
                    case BooruSearchResult.TimedOut:
                        replacement = "[booru request timed out]";
                        break;
                    case BooruSearchResult.HttpError(var statusCode):
                        replacement = $"[booru HTTP {statusCode} error]";
                        break;
                    case BooruSearchResult.Error:
                        replacement = "[booru error]";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(result));
                }
            }

            sb.Append(replacement);
            lastIndex = blockMatch.Index + blockMatch.Length;
        }

        sb.Append(text, lastIndex, text.Length - lastIndex);
        return sb.ToString();
    }
}
