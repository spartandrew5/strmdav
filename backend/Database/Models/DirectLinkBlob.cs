using MemoryPack;

namespace NzbWebDAV.Database.Models;

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DirectLinkBlob
{
    [MemoryPackOrder(0)]
    public required string Url { get; set; }

    [MemoryPackOrder(1)]
    public string? UserAgent { get; set; }

    [MemoryPackOrder(2)]
    public string? Referer { get; set; }

    [MemoryPackOrder(3)]
    public string? Cookie { get; set; }

    [MemoryPackOrder(4)]
    public Dictionary<string, string> Headers { get; set; } = new();
}