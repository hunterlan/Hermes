using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.Core.Tests;

public class ImportErrorTests
{
    // What the messages must never contain: the wording of .NET exceptions and their positions.
    private static readonly string[] DotNetWording =
    [
        "BytePositionInLine", "LineNumber", "Object reference", "was not present", "Unable to cast", "is an invalid start",
        "Exception", "System.", "JsonNode", "Parameter",
    ];

    private static ImportResult Import(string text, string source = "file.json")
    {
        var result = new ImportResult();
        ImportService.ImportText(text, result, source);
        return result;
    }

    private static void AssertPlain(ImportResult result)
    {
        Assert.NotEmpty(result.Errors);
        foreach (var error in result.Errors)
        {
            foreach (var wording in DotNetWording)
                Assert.DoesNotContain(wording, error.Message);
        }
    }

    [Fact]
    public void Truncated_json_names_the_file_and_a_one_based_position()
    {
        var result = Import("{\n  \"info\": { \"name\": \"A\" },\n  \"item\": [ {", "pets.json");

        var error = Assert.Single(result.Errors);
        Assert.Equal("pets.json isn't valid JSON (line 3, column 14).", error.Summary);
        Assert.Equal(StorageErrorKind.InvalidJson, error.Kind);
        AssertPlain(result);
        Assert.Contains("System.Text.Json", error.Details);
    }

    [Fact]
    public void The_position_ignores_whitespace_before_the_text()
    {
        var result = Import("\n\n  {\"a\": ", "x.json");

        Assert.Contains("(line 3,", Assert.Single(result.Errors).Summary);
    }

    [Fact]
    public void Text_that_is_neither_json_nor_curl_is_reported_as_invalid_json()
    {
        var error = Assert.Single(Import("hello", "Pasted text").Errors);

        Assert.Equal("Pasted text isn't valid JSON (line 1, column 1).", error.Summary);
    }

    [Fact]
    public void A_collection_without_required_fields_says_which_one()
    {
        var noInfo = Import("""{ "item": [] }""");
        var noItems = Import("""{ "info": { "name": "A", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" } }""");
        var itemIsObject = Import("""{ "info": { "name": "A" }, "item": { "name": "x" } }""");

        Assert.Equal("file.json: The collection has no `info` section. Nothing was imported from it.", Assert.Single(noInfo.Errors).Summary);
        Assert.Equal("file.json: The collection has no `item` list. Nothing was imported from it.", Assert.Single(noItems.Errors).Summary);
        Assert.Equal("file.json: `item` must be a list. Nothing was imported from it.", Assert.Single(itemIsObject.Errors).Summary);
        foreach (var result in new[] { noInfo, noItems, itemIsObject })
        {
            Assert.True(result.IsEmpty);
            AssertPlain(result);
        }
    }

    [Fact]
    public void An_old_schema_version_is_named()
    {
        var result = Import("""{ "info": { "name": "A", "schema": "https://schema.getpostman.com/json/collection/v1.0.0/collection.json" }, "item": [] }""");

        Assert.Contains("Unsupported Postman schema version 1.0.0", Assert.Single(result.Errors).Summary);
    }

    [Fact]
    public void An_environment_without_values_or_name_says_so()
    {
        var noName = Import("""{ "values": [] }""");
        var badValues = Import("""{ "name": "E", "values": "x" }""");

        Assert.Contains("no `name`", Assert.Single(noName.Errors).Summary);
        Assert.Contains("`values` must be a list", Assert.Single(badValues.Errors).Summary);
        AssertPlain(noName);
        AssertPlain(badValues);
    }

    [Fact]
    public void An_item_of_the_wrong_type_is_skipped_with_its_path_and_the_rest_is_imported()
    {
        const string json = """
            {
              "info": { "name": "Pets" },
              "item": [
                { "name": "Good", "request": { "method": "GET", "url": "https://x.test/a" } },
                "just a text",
                { "name": "Create user", "request": { "method": "POST" } },
                { "name": "Array request", "request": [ 1 ] },
                { "name": "List url", "request": { "url": [ 1 ] } },
                { "name": "No request" },
                { "name": "Broken folder", "item": "nope" },
                { "name": "Folder", "item": [
                  { "name": "Inside good", "request": "https://x.test/b" },
                  { "name": "Inside bad", "request": { "url": { "raw": "" } } }
                ] }
              ]
            }
            """;

        var result = Import(json, "pets.json");

        var collection = Assert.Single(result.Collections);
        Assert.Equal("Good", Assert.IsType<HttpRequestModel>(collection.Items[0]).Name);
        var folder = Assert.IsType<FolderModel>(collection.Items[1]);
        Assert.Equal("Inside good", Assert.IsType<HttpRequestModel>(Assert.Single(folder.Items)).Name);
        Assert.Equal(2, collection.Items.Count);

        var messages = result.Errors.Select(error => error.Summary).ToList();
        Assert.Equal(7, messages.Count);
        Assert.Contains(messages, m => m.StartsWith("pets.json: An item must be an object") && m.Contains("(at item[1])"));
        Assert.Contains(messages, m => m.Contains("The request \"Create user\" has no URL (at item[2].request.url)"));
        Assert.Contains(messages, m => m.Contains("(at item[3].request)"));
        Assert.Contains(messages, m => m.Contains("(at item[4].request.url)") && m.Contains("isn't text or an object"));
        Assert.Contains(messages, m => m.Contains("\"No request\"") && m.Contains("(at item[5])"));
        Assert.Contains(messages, m => m.Contains("\"Broken folder\"") && m.Contains("(at item[6].item)"));
        Assert.Contains(messages, m => m.Contains("\"Inside bad\"") && m.Contains("(at item[7].item[1].request.url)"));
        Assert.All(messages, m => Assert.EndsWith("That item was skipped, the rest was imported.", m));
        AssertPlain(result);
    }

    [Fact]
    public void Wrong_types_deep_inside_a_request_do_not_become_bugs()
    {
        const string json = """
            {
              "info": { "name": "Pets" },
              "variable": [ "text", 3 ],
              "item": [ { "name": "R", "request": {
                "url": "https://x.test",
                "auth": { "type": "bearer", "bearer": [ "text", 3 ] },
                "body": { "mode": "raw", "raw": "x", "options": "oops" }
              } } ]
            }
            """;

        var result = Import(json);

        Assert.Empty(result.Errors);
        Assert.Single(Assert.Single(result.Collections).Items);
    }

    [Theory]
    [InlineData("curl --frobnicate value", "The cURL command uses the option --frobnicate, which Hermes doesn't know, so it can't tell which word is the URL")]
    [InlineData("curl --frobnicate", "The cURL command uses the option --frobnicate, which Hermes doesn't know, so it can't tell which word is the URL")]
    [InlineData("curl --frobnicate https://x.test", null)]
    [InlineData("curl -Z https://x.test -q", null)]
    [InlineData("curl -H", "The cURL command ends after -H, which needs a value")]
    [InlineData("curl -sX", "The cURL command ends after -X, which needs a value")]
    [InlineData("curl --header", "The cURL command ends after --header, which needs a value")]
    [InlineData("curl -H 'Accept: x https://x.test", "The cURL command has a ' quote that is never closed")]
    [InlineData("curl -d \"{\\\"a\\\": 1} https://x.test", "The cURL command has a \" quote that is never closed")]
    [InlineData("curl -s --compressed", "The cURL command has no URL")]
    public void Malformed_curl_names_the_problem(string command, string? problem)
    {
        var result = Import(command, "Pasted text");

        if (problem is null)
        {
            Assert.Empty(result.Errors);
            return;
        }
        Assert.Equal($"Pasted text: {problem}. Nothing was imported from it.", Assert.Single(result.Errors).Summary);
        Assert.True(result.IsEmpty);
        AssertPlain(result);
    }

    [Fact]
    public void Common_curl_options_are_not_unknown()
    {
        var request = Assert.Single(Import("curl 'https://x.test/a' --compressed -s -S -v --no-keepalive -i --http2 --fail-with-body").Requests);

        Assert.Equal("https://x.test/a", request.Url);
    }

    [Fact]
    public void An_unexpected_exception_is_reported_as_a_bug_with_the_raw_text_in_details()
    {
        // Repeated names make System.Text.Json throw ArgumentException when the object is read.
        var result = Import("""{ "info": { "name": "a", "name": "b" }, "item": [] }""", "dup.json");

        var error = Assert.Single(result.Errors);
        Assert.Equal("Hermes couldn't import dup.json. This is a bug; please report it.", error.Summary);
        Assert.Contains("ArgumentException", error.Details);
        Assert.Contains("dup.json", error.Details);
        AssertPlain(result);
    }

    [Fact]
    public void An_unreadable_file_is_described_by_its_exception_type()
    {
        var path = Path.Combine(Path.GetTempPath(), "hermes-missing-" + Guid.NewGuid() + ".json");

        var denied = StorageErrorDescriber.Describe(new UnauthorizedAccessException("Access to the path is denied."), path, "a.json", StorageOperation.Read);
        var missing = StorageErrorDescriber.Describe(new FileNotFoundException("Could not find file"), path, "a.json", StorageOperation.Read);

        Assert.Equal("a.json couldn't be loaded: Hermes isn't allowed to read it.", denied.Summary);
        Assert.Equal("a.json couldn't be loaded: it or its folder wasn't found.", missing.Summary);
        Assert.DoesNotContain("Access to the path", denied.Message);
        Assert.DoesNotContain("Could not find file", missing.Message);
    }
}
