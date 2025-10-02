using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using BrowserBookmark.Bookmarks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BrowserBookmark;

internal sealed partial class BrowserBookmarkPage : DynamicListPage
{
    private const string DefaultIconGlyph = "\uE774";

    private readonly IReadOnlyList<BookmarkEntry> _allBookmarks;
    private IListItem[] _currentItems = Array.Empty<IListItem>();
    private readonly ListItem _noBookmarksItem;
    private readonly ListItem _noMatchesItem;

    private static readonly Dictionary<string, string> BrowserIconPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft Edge"] = "Assets\\Edge.png",
        ["Google Chrome"] = "Assets\\Chrome.png",
        ["Brave"] = "Assets\\Brave.png",
    };

    public BrowserBookmarkPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\logo.png");

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

        string query = searchText!.Trim();

        return _allBookmarks
            .Select(entry =>
            {
                MatchRank? rank = null;
                int fuzzyScore = 0;

                if (ContainsIgnoreCase(entry.Browser, query))
                {
                    rank = MatchRank.Browser;
                }
                else if (ContainsIgnoreCase(entry.Url, query))
                {
                    rank = MatchRank.Url;
                }
                else
                {
                    var titleMatch = StringMatcher.FuzzySearch(query, entry.Title);
                    if (titleMatch.Success)
                    {
                        rank = MatchRank.Title;
                        fuzzyScore = titleMatch.Score;
                    }
                }

                return new
                {
                    Entry = entry,
                    Rank = rank,
                    TitleScore = fuzzyScore
                };
            })
            .Where(result => result.Rank.HasValue)
            .OrderBy(result => result.Rank.GetValueOrDefault())
            .ThenByDescending(result => result.Rank == MatchRank.Title ? result.TitleScore : 0)
            .ThenByDescending(result => result.Entry.AddedOn ?? DateTimeOffset.MinValue)
            .ThenBy(result => result.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Entry.Url, StringComparer.OrdinalIgnoreCase)
            .Select(result => result.Entry);
    }

    private static bool ContainsIgnoreCase(string? source, string value)
        => !string.IsNullOrEmpty(source) && source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private enum MatchRank
    {
        Browser = 0,
        Url = 1,
        Title = 2,
    }

    private static IconInfo ResolveIcon(string browser)
    {
        if (BrowserIconPaths.TryGetValue(browser, out string? iconPath) && !string.IsNullOrEmpty(iconPath))
        {
            return IconHelpers.FromRelativePath(iconPath);
        }

        return new IconInfo(DefaultIconGlyph);
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
        };

        item.Icon = ResolveIcon(entry.Browser);

        string? tagText = entry.FolderLabel;
        if (!string.IsNullOrWhiteSpace(entry.Profile))
        {
            tagText = string.IsNullOrWhiteSpace(tagText) ? entry.Profile : string.Concat(entry.Profile, " > ", tagText);
        }

        if (!string.IsNullOrWhiteSpace(tagText))
        {
            item.Tags = [new Tag(tagText)];
        }

        return item;
    }
}
