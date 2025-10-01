// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BrowserBookmark;

public partial class BrowserBookmarkCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public BrowserBookmarkCommandsProvider()
    {
        DisplayName = "Browser Bookmark Search";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _commands = [
            new CommandItem(new BrowserBookmarkPage()) { Title = DisplayName },
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

}
