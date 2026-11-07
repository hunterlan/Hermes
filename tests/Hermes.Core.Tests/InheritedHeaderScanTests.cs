using Hermes.Core.Headers;
using Hermes.Core.Models;

namespace Hermes.Core.Tests;

public class InheritedHeaderScanTests
{
    [Fact]
    public void A_collection_without_headers_has_nothing_to_copy()
    {
        var collection = new CollectionModel { Items = [new HttpRequestModel { Name = "r", Url = "http://x" }] };

        Assert.False(InheritedHeaderScan.HasTickedHeaders(collection));
    }

    [Fact]
    public void Unticked_or_unnamed_rows_do_not_count()
    {
        var collection = new CollectionModel
        {
            Headers = [new KeyValueItem("X-Off", "1", enabled: false), new KeyValueItem("  ", "2")],
            Items = [new FolderModel { Headers = [new KeyValueItem("", "3")] }],
        };

        Assert.False(InheritedHeaderScan.HasTickedHeaders(collection));
    }

    [Fact]
    public void A_ticked_named_collection_header_counts()
    {
        var collection = new CollectionModel { Headers = [new KeyValueItem("X-Tenant", "t")] };

        Assert.True(InheritedHeaderScan.HasTickedHeaders(collection));
    }

    [Fact]
    public void A_ticked_named_header_in_a_nested_folder_counts()
    {
        var deepest = new FolderModel { Headers = [new KeyValueItem("X-Deep", "d")] };
        var outer = new FolderModel { Items = [new FolderModel { Items = [deepest] }] };
        var collection = new CollectionModel { Items = [new HttpRequestModel { Name = "r" }, outer] };

        Assert.True(InheritedHeaderScan.HasTickedHeaders(collection));
    }

    [Fact]
    public void Request_headers_alone_do_not_count()
    {
        var request = new HttpRequestModel { Name = "r", Headers = [new KeyValueItem("X-Own", "1")] };
        var collection = new CollectionModel { Items = [new FolderModel { Items = [request] }] };

        Assert.False(InheritedHeaderScan.HasTickedHeaders(collection));
    }
}
