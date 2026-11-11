using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core.Storage;

namespace Hermes.Core.Import;

/// <summary>
/// Detects what a piece of text is (Postman collection, environment, or cURL) and imports it. What can't be imported
/// is added to <see cref="ImportResult.Errors"/> in plain words, never as the text of a .NET exception; that text is
/// kept in each error's Details.
/// </summary>
public static class ImportService
{
    // A hand-edited file with comments or trailing commas still imports.
    private static readonly JsonDocumentOptions LenientJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <param name="sourceName">What the text came from, to name it in errors: a file name, or "Pasted text".</param>
    public static void ImportText(string text, ImportResult result, string sourceName = "Pasted text")
    {
        // Parsed as given (apart from a byte order mark) so that the line and column of a syntax error are the ones
        // the user sees in their editor.
        var original = text.TrimStart('﻿');
        text = original.Trim();
        if (text.Length == 0)
            return;

        try
        {
            if (CurlParser.LooksLikeCurl(text))
                result.Requests.Add(CurlParser.Parse(text));
            else
                ImportJson(original, result, sourceName);
        }
        catch (ImportException problem)
        {
            result.Errors.Add(StorageError.Plain($"{sourceName}: {problem.Message}. Nothing was imported from it."));
        }
        catch (Exception unexpected)
        {
            // Whatever else an importer throws is a bug in Hermes, not a problem with the user's file.
            result.Errors.Add(new StorageError(StorageErrorKind.Other,
                $"Hermes couldn't import {sourceName}. This is a bug; please report it.",
                null, DetailsOf(unexpected, sourceName)));
        }
    }

    private static void ImportJson(string text, ImportResult result, string sourceName)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: LenientJson);
        }
        catch (JsonException syntaxError)
        {
            result.Errors.Add(new StorageError(StorageErrorKind.InvalidJson,
                $"{sourceName} isn't valid JSON{StorageErrorDescriber.PositionOf(syntaxError)}.",
                null, DetailsOf(syntaxError, sourceName)));
            return;
        }

        if (PostmanCollectionImporter.IsMeantAsCollection(root))
        {
            var skipped = new List<ImportException>();
            result.Collections.Add(PostmanCollectionImporter.Import(root!, skipped));
            result.Errors.AddRange(skipped.Select(problem =>
                StorageError.Plain($"{sourceName}: {problem.Message}. That item was skipped, the rest was imported.")));
        }
        else if (PostmanEnvironmentConverter.IsMeantAsEnvironment(root))
        {
            result.Environments.Add(PostmanEnvironmentConverter.Import(root!));
        }
        else
        {
            result.Errors.Add(StorageError.Plain(
                $"{sourceName}: unrecognized format. Supported: Postman Collection v2.0/v2.1, Postman environment, cURL."));
        }
    }

    private static string DetailsOf(Exception exception, string sourceName) =>
        $"Source: {sourceName}\nError: {exception.GetType().FullName}: {exception.Message}" +
        (exception.StackTrace is { } stack ? $"\n{stack}" : "");
}
