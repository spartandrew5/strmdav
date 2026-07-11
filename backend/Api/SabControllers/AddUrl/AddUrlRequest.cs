using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.SabControllers.AddUrl;

public class AddUrlRequest
{
    public required string Url { get; init; }
    public required string Category { get; init; }
    public string? FileNameHint { get; init; }
    public string? UserAgent { get; init; }
    public string? Referer { get; init; }
    public string? Cookie { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new();
    public CancellationToken CancellationToken { get; init; }

    public static Task<AddUrlRequest> New(HttpContext context, ConfigManager configManager)
    {
        var url = context.GetRequestParam("name");
        if (string.IsNullOrWhiteSpace(url))
            throw new BadHttpRequestException("Missing required parameter: name (direct media URL)");

        var request = new AddUrlRequest
        {
            Url = url,
            FileNameHint = context.GetRequestParam("nzbname") ?? context.GetRequestParam("filename"),
            Category = context.GetRequestParam("cat") ?? configManager.GetManualUploadCategory(),
            UserAgent = context.GetRequestParam("useragent") ?? configManager.GetUserAgent(),
            Referer = context.GetRequestParam("referer"),
            Cookie = context.GetRequestParam("cookie"),
            Headers = ParseHeaders(context.GetRequestParam("url_headers")),
            CancellationToken = context.RequestAborted,
        };

        return Task.FromResult(request);
    }

    public DirectLinkBlob ToDirectLinkBlob()
    {
        return new DirectLinkBlob
        {
            Url = Url,
            UserAgent = UserAgent,
            Referer = Referer,
            Cookie = Cookie,
            Headers = Headers,
        };
    }

    private static Dictionary<string, string> ParseHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return dict ?? new Dictionary<string, string>();
        }
        catch (JsonException ex)
        {
            throw new BadHttpRequestException($"Invalid url_headers JSON: {ex.Message}");
        }
    }
}
