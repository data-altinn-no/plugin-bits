using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Dan.Plugin.Bits.Services;

/// <summary>
/// The text files of a GitHub repository zipball, addressed by their path inside the repo. GitHub wraps the
/// archive in one root folder named "{owner}-{repo}-{short sha}", which is stripped from every path.
/// </summary>
public sealed class RepositoryArchive
{
    private readonly Dictionary<string, string> files;

    private RepositoryArchive(string rootFolder, Dictionary<string, string> files)
    {
        RootFolder = rootFolder;
        this.files = files;
    }

    /// <summary>
    /// Name of the root folder in the zip, e.g. "data-altinn-no-bits-1a2b3c4". Identifies the commit the data came from.
    /// </summary>
    public string RootFolder { get; }

    public int FileCount => files.Count;

    public static RepositoryArchive FromZip(byte[] zipBytes)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string rootFolder = null;

        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // directory entry
            }

            var separator = entry.FullName.IndexOf('/');
            if (separator < 0)
            {
                continue; // not inside the root folder; GitHub never produces this
            }

            rootFolder ??= entry.FullName[..separator];
            var path = entry.FullName[(separator + 1)..];

            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            files[path] = reader.ReadToEnd();
        }

        return new RepositoryArchive(rootFolder ?? string.Empty, files);
    }

    public bool TryGetFile(string path, out string content)
    {
        return files.TryGetValue(Normalize(path), out content);
    }

    /// <summary>
    /// Direct children of a directory, as (file name, content).
    /// </summary>
    public IReadOnlyList<(string Name, string Content)> FilesInDirectory(string directory)
    {
        var prefix = Normalize(directory) + "/";

        return files
            .Where(file => file.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                           && file.Key.IndexOf('/', prefix.Length) < 0)
            .Select(file => (Name: file.Key[prefix.Length..], file.Value))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static string Normalize(string path) => path.Trim().Replace('\\', '/').Trim('/');
}
