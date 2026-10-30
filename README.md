# Hermes

A native, Postman-style desktop client for testing REST APIs, built with .NET 10 and Avalonia 12.

Hermes focuses on the core Postman workflow — compose a request, send it, inspect the response, organise requests in
collections and switch environments — in a fast native app that stores everything as plain JSON files.

## Features

### Requests

- Multiple tabs with unsaved-changes indicator; open tabs and drafts are restored on restart
- Preview tabs (single click previews in an italic tab, double click or editing keeps it), pinned tabs, collection
  prefix on same-named tabs; tabs can be turned off in Settings
- Composer shows only the sections a request uses; the rest wait as "Not used yet" chips. Headers added
  automatically are listed inline, and a "Sends to" line shows the resolved URL
- Confirmation before sending with unresolved variables or to environments marked "Ask before sending"
- Command palette (Ctrl+K) to find requests by name, method or URL and run any command
- Methods GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS
- URL bar with `{{variable}}` highlighting (resolved / unresolved) and hover tooltips showing the value
- Typing `{{` in the URL, params, headers or auth fields lists up to five matching variables (↑/↓, Enter or Tab to insert, Esc to dismiss)
- Query params table kept in two-way sync with the URL; `:pathVariables` table
- Headers with an auto-generated ("hidden") headers view
- Body: none, form-data (text and files), x-www-form-urlencoded, raw (Text, JSON, XML, HTML, JavaScript) with
  syntax highlighting, folding and Beautify, binary file
- Per-request settings: follow redirects, SSL certificate verification, timeout
- Paste a cURL command into the URL bar to import it; generate a cURL snippet for any request (`</>`)

### Authorization

- Basic
- Bearer token
- API key (header or query)
- OAuth 2.0 (authorization code with PKCE, client credentials, password; token obtained through the system browser and a loopback callback)
- "Inherit auth from parent" through folders up to the collection

### Responses

- Status
- Time (with time-to-first-byte breakdown) and size
- Pretty / Raw / Preview (images) views with highlighting, folding and search (Ctrl+F)
- Headers and cookies
- Copy and save to file

> [!NOTE]
> Responses are saved with each saved request (latest plus five earlier), and the "Sent request" tab shows the
> exact request after variables and auth.

> [!NOTE]
> Very large responses fall back to raw text so the UI stays responsive.

### Collections

- Collections and nested folders in a sidebar tree with search
- Inline rename, duplicate, delete
- Drag-and-drop reordering
- Per-collection/folder description and authorization
- Collection variables

### Environments & variables

- Custom environments and global variables
- Each variable has an initial (shared/exported) and a current (local) value, and an optional secret type
- Environment selector with a quick-look popup
- Precedence: environment > collection > globals
- [Dynamic variables](docs/manual/dynamic-variables.md) such as `{{$guid}}` and
  `{{$timestamp}}`

> [!NOTE]
> An environments matrix edits every variable in every environment side by side and flags variables used by
> requests that are empty in the active environment. The auth editor shows where auth is inherited from and which
> items override it.

### Secrets

Passwords, tokens and other secrets are stored in your operating system's keychain, not in Hermes' JSON files.
Everything else stays in the JSON files, so you can safely commit the data folder. Fields that use a `{{variable}}`
are stored in the files as written. Saved responses hide credentials from the sent request, history doesn't keep
them, and exports ask whether to include them. If your system has no usable keychain, secrets are saved as plain
text and the status bar tells you. More details are on the [Secrets](docs/manual/secrets.md)
manual page.

### Importing and exporting

- Postman Collection v2.0/v2.1 and Postman environment/globals files (import via dialog or by
dropping files on the window)
- cURL import
- Export collections as Postman v2.1 and environments as Postman environment files. 

> [!NOTE]
> Pre-request/test scripts and unsupported body types (e.g. GraphQL) are preserved on round trip.

### Other

- History: every sent request, grouped by day and searchable; click to reopen
- Shared cookie jar with a cookie manager
- Dark and light themes
- Response below or beside the request

### Not implemented (yet)

- Tests/scripts execution
- Collection runner
- Flows, mocks, monitors
- AI features
- GraphQL/WebSocket/gRPC (not planned for the near future)

## Building and running

Requires the .NET 10 SDK.

```bash
dotnet run --project src/Hermes.App          # run
dotnet test                                  # Core unit tests + headless UI tests
```

The headless UI tests render screenshots to `tests/Hermes.App.Tests/bin/Debug/net10.0/screenshots/`
(override with `HERMES_SCREENSHOT_DIR`).

### Publishing

Specifying a runtime produces a self-contained single executable:

```bash
dotnet publish src/Hermes.App -c Release -r linux-x64   -o dist/linux-x64
dotnet publish src/Hermes.App -c Release -r win-x64     -o dist/win-x64
dotnet publish src/Hermes.App -c Release -r osx-arm64   -o dist/osx-arm64
```

## Project layout

```
src/Hermes.Core     UI-independent library: models, JSON storage, variable resolution, auth (incl. OAuth 2.0),
                    request preparation and execution, Postman/cURL import & export, formatters
src/Hermes.App      Avalonia 12 desktop app (MVVM with CommunityToolkit.Mvvm, AvaloniaEdit + TextMate editors)
tests/Hermes.Core.Tests   xUnit tests for the core library (HTTP tests run against a local HttpListener)
tests/Hermes.App.Tests    Avalonia headless UI tests
```

## Documentation

The [user manual](docs/manual/index.md) ships with the app and opens offline with `F1`:

- [Keyboard shortcuts](docs/manual/07-keyboard-shortcuts.md)
- [Data](docs/manual/08-data-folder.md) - where Hermes stores its JSON files
- [Secrets](docs/manual/secrets.md) - what goes into the OS keychain
- [Dynamic variables](docs/manual/dynamic-variables.md) - built-in `{{$...}}` variables

Maintainer notes live in the wiki: [Releases](https://github.com/hunterlan/Hermes/wiki/Releases) - installers,
release workflow, app icon.
