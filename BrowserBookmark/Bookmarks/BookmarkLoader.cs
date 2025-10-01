using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BrowserBookmark.Bookmarks;

internal static class BookmarkLoader
{
    internal static IReadOnlyList<BookmarkEntry> LoadBookmarks()
    {
        var results = new List<BookmarkEntry>();

        foreach (var source in EnumerateChromiumSources())
        {
            try
            {
                results.AddRange(ReadChromiumBookmarks(source));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (JsonException)
            {
            }
            catch (FormatException)
            {
            }
        }

        return results;
    }

    private static IEnumerable<BookmarkSource> EnumerateChromiumSources()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var roots = new[]
        {
            new BrowserRoot("Microsoft Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data")),
            new BrowserRoot("Google Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data")),
            new BrowserRoot("Brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data")),
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root.BasePath))
            {
                continue;
            }

            var rootCandidate = Path.Combine(root.BasePath, "Bookmarks");
            if (File.Exists(rootCandidate))
            {
                yield return new BookmarkSource(root.Browser, string.Empty, rootCandidate);
            }
            foreach (var directory in EnumerateDirectoriesSafe(root.BasePath))
            {
                var path = Path.Combine(directory, "Bookmarks");
                if (File.Exists(path))
                {
                    yield return new BookmarkSource(root.Browser, Path.GetFileName(directory), path);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<BookmarkEntry> ReadChromiumBookmarks(BookmarkSource source)
    {
        var entries = new List<BookmarkEntry>();

        using FileStream stream = File.Open(source.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using JsonDocument document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("roots", out JsonElement roots))
        {
            return entries;
        }

        var normalizedProfile = NormalizeProfileName(source.Profile);

        foreach (JsonProperty root in roots.EnumerateObject())
        {
            if (!root.Value.TryGetProperty("children", out JsonElement children) || children.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement child in children.EnumerateArray())
            {
                TraverseChromiumNode(child, source.Browser, normalizedProfile, string.Empty, entries.Add);
            }
        }

        return entries;
    }

    private static void TraverseChromiumNode(
        JsonElement node,
        string browser,
        string profile,
        string currentPath,
        Action<BookmarkEntry> onEntry)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var nodeType = TryGetString(node, "type");

        if (string.Equals(nodeType, "url", StringComparison.OrdinalIgnoreCase))
        {
            var url = TryGetString(node, "url");
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var title = TryGetString(node, "name") ?? url;
            var added = TryParseChromiumTimestamp(node, "date_added") ?? TryParseChromiumTimestamp(node, "date_last_used");

            onEntry(new BookmarkEntry(browser, profile, title, url, currentPath, added));
            return;
        }

        if (!string.Equals(nodeType, "folder", StringComparison.OrdinalIgnoreCase) && nodeType is not null)
        {
            return;
        }

        var folderName = TryGetString(node, "name");
        var nextPath = CombinePath(currentPath, folderName);

        if (node.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                TraverseChromiumNode(child, browser, profile, nextPath, onEntry);
            }
        }
    }

    private static string CombinePath(string parent, string? child)
    {
        if (string.IsNullOrWhiteSpace(child))
        {
            return parent;
        }

        return string.IsNullOrWhiteSpace(parent) ? child : string.Concat(parent, " > ", child);
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out JsonElement value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null,
            };
        }

        return null;
    }

    private static string NormalizeProfileName(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return string.Empty;
        }

        return profile.Equals("Default", StringComparison.OrdinalIgnoreCase)
            ? "Default"
            : profile;
    }

    private static DateTimeOffset? TryParseChromiumTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        string? raw = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long microseconds))
        {
            return null;
        }

        const long ticksPerMicrosecond = 10;
        var epoch = new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);

        try
        {
            return epoch.AddTicks(microseconds * ticksPerMicrosecond);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private sealed record BrowserRoot(string Browser, string BasePath, bool TreatBaseAsProfile = false);

    private sealed record BookmarkSource(string Browser, string Profile, string Path);
}

internal sealed class BookmarkEntry
{
    internal BookmarkEntry(string browser, string profile, string title, string url, string folderPath, DateTimeOffset? addedOn)
    {
        Browser = browser;
        Profile = profile;
        Title = string.IsNullOrWhiteSpace(title) ? url : title;
        Url = url;
        FolderPath = folderPath;
        AddedOn = addedOn;
    }

    internal string Browser { get; }

    internal string Profile { get; }

    internal string Title { get; }

    internal string Url { get; }

    internal string FolderPath { get; }

    internal DateTimeOffset? AddedOn { get; }

    internal string SectionLabel => string.IsNullOrEmpty(Profile) ? Browser : string.Concat(Browser, " - ", Profile);

    internal string? FolderLabel => string.IsNullOrWhiteSpace(FolderPath) ? null : FolderPath;
}

