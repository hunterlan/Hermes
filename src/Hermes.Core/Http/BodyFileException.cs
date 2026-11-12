namespace Hermes.Core.Http;

/// <summary>A file that the request body refers to couldn't be opened. The inner exception says why.</summary>
public sealed class BodyFileException(string path, Exception innerException)
    : Exception($"Couldn't read the body file '{path}'.", innerException)
{
    public string Path { get; } = path;
}
