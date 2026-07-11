using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Api.SabControllers.GetHistory;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.SabControllers.AddUrl;

public class AddUrlController(
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    ConfigManager configManager,
    WebsocketManager websocketManager
) : SabApiController.BaseController(httpContext, configManager)
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
    });

    public async Task<AddUrlResponse> AddUrlAsync(AddUrlRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url))
            throw new BadHttpRequestException("Invalid URL in parameter: name");

        var mediaInfo = await ProbeRemoteMediaAsync(url, request).ConfigureAwait(false);
        var historyId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var categoryFolder = await GetOrCreateCategoryFolder(request.Category, request.CancellationToken)
            .ConfigureAwait(false);
        var jobName = FilenameUtil.GetJobName(mediaInfo.FileName);
        var mountFolder = await CreateUniqueMountFolder(categoryFolder, jobName, historyId, request.CancellationToken)
            .ConfigureAwait(false);

        await BlobStore.WriteBlob(fileId, request.ToDirectLinkBlob()).ConfigureAwait(false);

        var mediaItem = DavItem.New(
            id: fileId,
            parent: mountFolder,
            name: mediaInfo.FileName,
            fileSize: mediaInfo.FileSize,
            type: DavItem.ItemType.UsenetFile,
            subType: DavItem.ItemSubType.DirectLinkFile,
            releaseDate: null,
            lastHealthCheck: null,
            historyItemId: historyId,
            fileBlobId: fileId,
            nzbBlobId: null
        );

        var historyItem = new HistoryItem
        {
            Id = historyId,
            CreatedAt = DateTime.Now,
            FileName = mediaInfo.FileName,
            JobName = jobName,
            Category = request.Category,
            DownloadStatus = HistoryItem.DownloadStatusOption.Completed,
            TotalSegmentBytes = mediaInfo.FileSize,
            DownloadTimeSeconds = 0,
            FailMessage = null,
            DownloadDirId = mountFolder.Id,
            NzbBlobId = null
        };

        dbClient.Ctx.Items.Add(mediaItem);
        dbClient.Ctx.HistoryItems.Add(historyItem);
        await dbClient.Ctx.SaveChangesAsync(request.CancellationToken).ConfigureAwait(false);

        var historySlot = GetHistoryResponse.HistorySlot.FromHistoryItem(historyItem, mountFolder, configManager);
        _ = websocketManager.SendMessage(WebsocketTopic.HistoryItemAdded, historySlot.ToJson());
        _ = DavDatabaseContext.RcloneVfsForget(["/content", "/completed-symlinks", "/.ids"]);

        return new AddUrlResponse
        {
            Status = true,
            NzoIds = [historyId.ToString()]
        };
    }

    protected override async Task<IActionResult> Handle()
    {
        var request = await AddUrlRequest.New(httpContext, configManager).ConfigureAwait(false);
        return Ok(await AddUrlAsync(request).ConfigureAwait(false));
    }

    private async Task<DavItem> GetOrCreateCategoryFolder(string category, CancellationToken ct)
    {
        var existing = await dbClient.GetDirectoryChildAsync(DavItem.ContentFolder.Id, category, ct).ConfigureAwait(false);
        if (existing is not null) return existing;

        var newCategory = DavItem.New(
            id: Guid.NewGuid(),
            parent: DavItem.ContentFolder,
            name: category,
            fileSize: null,
            type: DavItem.ItemType.Directory,
            subType: DavItem.ItemSubType.Directory,
            releaseDate: null,
            lastHealthCheck: null,
            historyItemId: null,
            fileBlobId: null,
            nzbBlobId: null
        );
        dbClient.Ctx.Items.Add(newCategory);
        return newCategory;
    }

    private async Task<DavItem> CreateUniqueMountFolder
    (
        DavItem categoryFolder,
        string preferredName,
        Guid historyItemId,
        CancellationToken ct
    )
    {
        var name = preferredName;
        for (var i = 1; i <= 100; i++)
        {
            var candidate = i == 1 ? name : $"{name} ({i})";
            var exists = await dbClient.GetDirectoryChildAsync(categoryFolder.Id, candidate, ct).ConfigureAwait(false);
            if (exists is not null) continue;

            var folder = DavItem.New(
                id: Guid.NewGuid(),
                parent: categoryFolder,
                name: candidate,
                fileSize: null,
                type: DavItem.ItemType.Directory,
                subType: DavItem.ItemSubType.Directory,
                releaseDate: null,
                lastHealthCheck: null,
                historyItemId: historyItemId,
                fileBlobId: null,
                nzbBlobId: null
            );
            dbClient.Ctx.Items.Add(folder);
            return folder;
        }

        throw new BadHttpRequestException("Could not allocate a unique mount folder name.");
    }

    private async Task<RemoteMediaInfo> ProbeRemoteMediaAsync(Uri url, AddUrlRequest request)
    {
        var metadata = await GetRemoteMetadataAsync(url, request).ConfigureAwait(false);
        var fileName = GetFileName(url, request.FileNameHint, metadata.ResponseFileName, metadata.ContentType);
        return new RemoteMediaInfo(fileName, metadata.ContentLength);
    }

    private static string GetFileName(Uri url, string? hint, string? responseFileName, string? contentType)
    {
        var name = StringUtil.EmptyToNull(hint)
            ?? StringUtil.EmptyToNull(responseFileName)
            ?? Path.GetFileName(url.AbsolutePath);

        name = Uri.UnescapeDataString(name ?? string.Empty);
        name = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(name))
            name = $"direct-link-{Guid.NewGuid():N}.mp4";

        var ext = Path.GetExtension(name);
        if (string.IsNullOrWhiteSpace(ext))
        {
            var inferredExt = GetExtensionFromContentType(contentType);
            if (!string.IsNullOrWhiteSpace(inferredExt))
            {
                name = $"{name}{inferredExt}";
            }
        }

        return name;
    }

    private async Task<RemoteMetadata> GetRemoteMetadataAsync(Uri url, AddUrlRequest request)
    {
        using var head = new HttpRequestMessage(HttpMethod.Head, url);
        ApplyHeaders(head, request);
        using var headResponse = await HttpClient.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, request.CancellationToken)
            .ConfigureAwait(false);

        if (headResponse.IsSuccessStatusCode && headResponse.Content.Headers.ContentLength is long headLength && headLength > 0)
        {
            return new RemoteMetadata
            {
                ContentLength = headLength,
                ContentType = headResponse.Content.Headers.ContentType?.MediaType,
                ResponseFileName = GetResponseFileName(headResponse)
            };
        }

        using var get = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(get, request);
        get.Headers.Range = new RangeHeaderValue(0, 0);
        using var getResponse = await HttpClient.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, request.CancellationToken)
            .ConfigureAwait(false);

        if (getResponse.StatusCode != HttpStatusCode.PartialContent && !getResponse.IsSuccessStatusCode)
            throw new BadHttpRequestException($"Failed to probe media URL. Status code: {(int)getResponse.StatusCode}.");

        var length =
            getResponse.Content.Headers.ContentRange?.Length
            ?? getResponse.Content.Headers.ContentLength
            ?? 0;

        if (length > 0)
        {
            return new RemoteMetadata
            {
                ContentLength = length,
                ContentType = getResponse.Content.Headers.ContentType?.MediaType,
                ResponseFileName = GetResponseFileName(getResponse)
            };
        }

        throw new BadHttpRequestException("Could not determine remote file size. Content-Length is required.");
    }

    private static string? GetResponseFileName(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        return fileName.Trim('"');
    }

    private static string? GetExtensionFromContentType(string? contentType)
    {
        return contentType?.ToLowerInvariant() switch
        {
            "video/mp4" => ".mp4",
            "video/x-matroska" => ".mkv",
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            "video/x-msvideo" => ".avi",
            "video/x-ms-wmv" => ".wmv",
            "video/mpeg" => ".mpeg",
            _ => null,
        };
    }

    private static void ApplyHeaders(HttpRequestMessage requestMessage, AddUrlRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.UserAgent))
            requestMessage.Headers.TryAddWithoutValidation("User-Agent", request.UserAgent);

        if (!string.IsNullOrWhiteSpace(request.Referer)
            && Uri.TryCreate(request.Referer, UriKind.Absolute, out var refererUri))
            requestMessage.Headers.Referrer = refererUri;

        if (!string.IsNullOrWhiteSpace(request.Cookie))
            requestMessage.Headers.TryAddWithoutValidation("Cookie", request.Cookie);

        foreach (var (key, value) in request.Headers)
            requestMessage.Headers.TryAddWithoutValidation(key, value);
    }

    private class RemoteMetadata
    {
        public long ContentLength { get; init; }
        public string? ContentType { get; init; }
        public string? ResponseFileName { get; init; }
    }

    private record RemoteMediaInfo(string FileName, long FileSize);
}
