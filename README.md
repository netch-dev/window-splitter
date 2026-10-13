# Window Splitter

A small Windows tray app for snapping windows and bringing a whole set of them up with one hotkey. Built for a single monitor.

## Running it

You need the .NET 10 runtime. Build with:

    dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist

Then run `dist\WindowSplitter.exe`. Nothing opens. Look for the icon in the system tray, behind the ^ arrow by the clock. Right click it for the menu. "Start with Windows" is in there too.

Only one copy can run at a time, so close the old one before launching a rebuilt exe.

## Snapping

All of these are Ctrl+Alt plus a key.

| Key | Result |
|---|---|
| Left / Right | left / right half |
| Up / Down | top / bottom half |
| U / I | top left / top right quarter |
| J / K | bottom left / bottom right quarter |
| 1 / 2 / 3 | left / middle / right third |
| Enter | fill the screen |
| C | centered |

If a combo does nothing, another program probably owns it. The app shows a notice on startup listing any it couldn't grab.

## Workspaces

A workspace is a set of windows and where each one goes.

1. Arrange your windows how you want them.
2. Press Ctrl+Alt+S, name it, and tick the windows to include.
3. Press Ctrl+Alt+W to open the workspace. Anything that isn't running gets launched, minimized windows are restored, everything is moved into place and brought to the front.

Saving with the same name overwrites the old one. Ctrl+Alt+F1 to F9 open the first nine workspaces in the order they were saved. Double clicking the tray icon does the same as Ctrl+Alt+W.

## Config

Workspaces live in `%AppData%\WindowSplitter\config.json`. The tray menu has an "Edit config file" entry. Each window looks like this:

    {
      "Process": "chrome",
      "TitleContains": "YouTube",
      "Launch": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      "Args": "",
      "X": 0.5, "Y": 0, "W": 0.5, "H": 1
    }

- `Process` is the exe name without `.exe`.
- `TitleContains` is optional. Use it to tell apart two windows of the same app.
- `Launch` and `Args` are used when the window isn't open. Leave `Launch` empty to never start it.
- `X`, `Y`, `W`, `H` are fractions of the screen's work area, so 0.5 is half. The order of the list is the stacking order, last on top.

## Known limits

- Windows running as administrator ignore the app unless it is also run as administrator.
- Windows are matched by process name, so two windows of one app can swap places unless you use `TitleContains`.
- Some apps ignore size requests or enforce a minimum size.
