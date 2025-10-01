using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BrowserBookmark;

internal sealed partial class BrowserBookmarkPage : DynamicListPage
{
    private readonly IReadOnlyList<BookmarkEntry> _allBookmarks;
    private IListItem[] _currentItems = Array.Empty<IListItem>();
    private readonly ListItem _noBookmarksItem;
    private readonly ListItem _noMatchesItem;

    public BrowserBookmarkPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Browser Bookmark Search";
        Name = "Open";
        PlaceholderText = "Search bookmark titles, urls, or folders";

        _allBookmarks = BookmarkLoader.LoadBookmarks();
        _noBookmarksItem = new ListItem(new NoOpCommand())
        {
            Title = "No bookmarks detected",
            Subtitle = "Only Chromium-based browsers are supported right now."
        };
        _noMatchesItem = new ListItem(new NoOpCommand())
        {
            Title = "No matching bookmark",
            Subtitle = "Try a different keyword or folder name."
        };

        if (_allBookmarks.Count == 0)
        {
            EmptyContent = _noBookmarksItem;
        }
        else
        {
            EmptyContent = _noMatchesItem;
            ApplyFilter(string.Empty);
        }
    }

    public override IListItem[] GetItems() => _currentItems;

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        ApplyFilter(newSearch);
    }

    private void ApplyFilter(string? searchText)
    {
        if (_allBookmarks.Count == 0)
        {
            _currentItems = Array.Empty<IListItem>();
            EmptyContent = _noBookmarksItem;
            HasMoreItems = false;
            RaiseItemsChanged(0);
            return;
        }

        List<BookmarkEntry> filtered = FilterBookmarks(searchText).ToList();

        _currentItems = filtered.Select(CreateItem).ToArray();
        HasMoreItems = false;
        EmptyContent = _currentItems.Length == 0 ? _noMatchesItem : null;
        RaiseItemsChanged(_currentItems.Length);
    }

    private IEnumerable<BookmarkEntry> FilterBookmarks(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return _allBookmarks
                .OrderByDescending(b => b.AddedOn ?? DateTimeOffset.MinValue)
                .ThenBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Url, StringComparer.OrdinalIgnoreCase);
        }

        string[] tokens = searchText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return _allBookmarks
            .Where(b => MatchesTokens(b, tokens))
            .OrderByDescending(b => b.AddedOn ?? DateTimeOffset.MinValue)
            .ThenBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Url, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesTokens(BookmarkEntry entry, string[] tokens)
    {
        foreach (string token in tokens)
        {
            if (!MatchesToken(entry, token))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesToken(BookmarkEntry entry, string token)
    {
        return Contains(entry.Title, token)
            || Contains(entry.Url, token)
            || Contains(entry.Browser, token)
            || Contains(entry.Profile, token)
            || Contains(entry.FolderLabel, token);
    }

    private static bool Contains(string? source, string token)
    {
        return !string.IsNullOrEmpty(source)
            && source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static ListItem CreateItem(BookmarkEntry entry)
    {
        var openCommand = new OpenUrlCommand(entry.Url)
        {
            Result = CommandResult.Hide(),
        };

        var item = new ListItem(openCommand)
        {
            Title = entry.Title,
            Subtitle = entry.Url,
            Section = entry.SectionLabel,
            TextToSuggest = string.Concat(entry.Title, ' ', entry.Url),
        };

        if (entry.FolderLabel is not null)
        {
            item.Tags = new ITag[] { new Tag(entry.FolderLabel) };
        }

        return item;
    }
}
