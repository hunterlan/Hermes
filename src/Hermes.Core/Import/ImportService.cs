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
    // A hand-edited file with comments or trailing commas still imports. A key that appears twice doesn't: which value
    // is meant is anyone's guess, and it is a mistake in the file.
    private static readonly JsonDocumentOptions LenientJson = new()
    {
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
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

        Guarded(result, sourceName, () =>
        {
            if (CurlParser.LooksLikeCurl(text))
                result.Requests.Add(CurlParser.Parse(text));
            else
                ImportJson(original, result, sourceName);
        });
    }

    /// <summary>Runs an import and turns what it throws into errors in <paramref name="result"/>.</summary>
    internal static void Guarded(ImportResult result, string sourceName, Action import)
    {
        try
        {
            import();
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
            // Duplicate keys are found after parsing, so that exception has no position. Parsing again where they are
            // allowed tells them apart from a syntax error without reading the exception's text.
            var position = StorageErrorDescriber.PositionOf(syntaxError);
            var summary = position is null && ParsesWithDuplicateKeys(text)
                ? $"{sourceName} has a key that appears twice in the same object."
                : $"{sourceName} isn't valid JSON{position}.";
            result.Errors.Add(new StorageError(StorageErrorKind.InvalidJson, summary, null, DetailsOf(syntaxError, sourceName)));
            return;
        }

        if (PostmanCollectionImporter.IsMeantAsCollection(root))
        {
            var skipped = new List<ImportException>();
            var warnings = new List<string>();
            result.Collections.Add(PostmanCollectionImporter.Import(root!, skipped, warnings));
            result.Errors.AddRange(warnings.Select(warning => StorageError.Plain($"{sourceName}: {warning}.")));
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

    private static bool ParsesWithDuplicateKeys(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = LenientJson.AllowTrailingCommas,
                CommentHandling = LenientJson.CommentHandling,
                AllowDuplicateProperties = true,
            });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string DetailsOf(Exception exception, string sourceName) =>
        $"Source: {sourceName}\nError: {exception.GetType().FullName}: {exception.Message}" +
        (exception.StackTrace is { } stack ? $"\n{stack}" : "");
}
