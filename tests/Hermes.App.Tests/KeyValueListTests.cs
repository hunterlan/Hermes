using Hermes.App.ViewModels;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

public class KeyValueListTests
{
    [Fact]
    public void Bulk_edit_keeps_what_the_text_does_not_show()
    {
        var list = new KeyValueListViewModel(allowFiles: true);
        list.Load(
        [
            new KeyValueItem("name", "ada") { Description = "Who" },
            new KeyValueItem("avatar ", "/tmp/a.png") { Kind = KeyValueKind.File },
            new KeyValueItem("tag", "one") { Description = "First tag" },
            new KeyValueItem("tag", "two") { Description = "Second tag" },
        ]);

        list.IsBulkEdit = true;
        list.BulkText = list.BulkText.Replace("name:ada", "name:grace") + "extra:new\n";
        list.IsBulkEdit = false;

        Assert.Collection(list.ToModels(),
            i => Assert.Equal(("name", "grace", "Who"), (i.Key, i.Value, i.Description)),
            i => Assert.Equal(("avatar", KeyValueKind.File), (i.Key, i.Kind)),
            i => Assert.Equal(("tag", "one", "First tag"), (i.Key, i.Value, i.Description)),
            i => Assert.Equal(("tag", "two", "Second tag"), (i.Key, i.Value, i.Description)),
            i => Assert.Equal(("extra", "new", null, KeyValueKind.Text), (i.Key, i.Value, i.Description, i.Kind)));
    }
}
