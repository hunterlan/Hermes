using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core.Models;

namespace Hermes.Core.Import;

public sealed class ImportResult
{
    public List<CollectionModel> Collections { get; } = [];
    public List<EnvironmentModel> Environments { get; } = [];
    public List<HttpRequestModel> Requests { get; } = [];
    public List<string> Errors { get; } = [];

    public bool IsEmpty => Collections.Count == 0 && Environments.Count == 0 && Requests.Count == 0;
}

/// <summary>Detects what a piece of text is (Postman collection, environment, or cURL) and imports it.</summary>
public static class ImportService
{
    public static void ImportText(string text, ImportResult result, string sourceName = "Pasted text")
    {
        text = text.Trim().TrimStart('﻿');
        if (text.Length == 0)
            return;

        if (CurlParser.LooksLikeCurl(text))
        {
            try
            {
                result.Requests.Add(CurlParser.Parse(text));
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{sourceName}: {ex.Message}");
            }
            return;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"{sourceName}: not valid JSON or cURL ({ex.Message})");
            return;
        }

        try
        {
            if (PostmanCollectionImporter.LooksLikeCollection(root))
                result.Collections.Add(PostmanCollectionImporter.Import(root!));
            else if (PostmanEnvironmentConverter.LooksLikeEnvironment(root))
                result.Environments.Add(PostmanEnvironmentConverter.Import(root!));
            else
                result.Errors.Add($"{sourceName}: unrecognized format. Supported: Postman Collection v2.0/v2.1, Postman environment, cURL.");
        }
        catch (Exception ex)
        {
            // Whatever a malformed file makes an importer throw, it is that file's import error, not the app's crash.
            result.Errors.Add($"{sourceName}: {ex.Message}");
        }
    }
}
