using Microsoft.AspNetCore.Http;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Streams;
using NzbWebDAV.WebDav.Base;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreDirectLinkFile(
    DavItem davDirectLinkFile,
    HttpContext httpContext,
    DavDatabaseClient dbClient
) : BaseStoreStreamFile(httpContext)
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
    });

    public override string Name => davDirectLinkFile.Name;
    public override string UniqueKey => davDirectLinkFile.Id.ToString();
    public override long FileSize => davDirectLinkFile.FileSize!.Value;
    public override DateTime CreatedAt => davDirectLinkFile.CreatedAt;

    protected override async Task<Stream> GetStreamAsync(CancellationToken ct)
    {
        httpContext.Items["DavItem"] = davDirectLinkFile;
        var linkBlob = await dbClient.GetDirectLinkBlobAsync(davDirectLinkFile, ct).ConfigureAwait(false);
        if (linkBlob is null)
            throw new FileNotFoundException($"Could not find direct-link blob with id: {davDirectLinkFile.Id}");

        return new HttpRangeStream(HttpClient, linkBlob, FileSize);
    }
}