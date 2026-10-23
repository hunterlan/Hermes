# Hermes

A native, Postman-style desktop client for testing REST APIs, built with .NET 10 and Avalonia 12.

Hermes focuses on the core Postman workflow — compose a request, send it, inspect the response, organise requests in
collections and switch environments — in a fast native app that stores everything as plain JSON files.

## Features

**Requests**
- Multiple tabs with unsaved-changes indicator; open tabs and drafts are restored on restart
- Preview tabs (single click previews in an italic tab, double click or editing keeps it), pinned tabs, collection
  prefix on same-named tabs; tabs can be turned off in Settings
- Composer shows only the sections a request uses; the rest wait as "Not used yet" chips. Headers added
  automatically are listed inline, and a "Sends to" line shows the resolved URL
- Confirmation before sending with unresolved variables or to environments marked "Ask before sending"
- Command palette (Ctrl+K) to find requests by name, method or URL and run any command
- Methods GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS
- URL bar with `{{variable}}` highlighting (resolved / unresolved) and hover tooltips showing the value
- Query params table kept in two-way sync with the URL; `:pathVariables` table
- Headers with an auto-generated ("hidden") headers view
- Body: none, form-data (text and files), x-www-form-urlencoded, raw (Text, JSON, XML, HTML, JavaScript) with
  syntax highlighting, folding and Beautify, binary file
- Per-request settings: follow redirects, SSL certificate verification, timeout
- Paste a cURL command into the URL bar to import it; generate a cURL snippet for any request (`</>`)

**Authorization** — Basic, Bearer token, API key (header or query), OAuth 2.0 (authorization code with PKCE,
client credentials, password; token obtained through the system browser and a loopback callback), and
"Inherit auth from parent" through folders up to the collection.

**Responses** — status, time (with time-to-first-byte breakdown) and size; Pretty / Raw / Preview (images) views
with highlighting, folding and search (Ctrl+F); response headers and cookies; copy and save to file. Very large
responses fall back to raw text so the UI stays responsive.

**Collections** — collections and nested folders in a sidebar tree with search, inline rename, duplicate, delete,
drag-and-drop reordering, and per-collection/folder description and authorization; collection variables.

**Environments & variables** — environments plus globals, each variable with an initial (shared/exported) and
current (local) value and an optional secret type; environment selector with a quick-look popup. Precedence:
environment > collection > globals. Dynamic variables: `{{$guid}}`, `{{$randomUUID}}`, `{{$timestamp}}`,
`{{$isoTimestamp}}`, `{{$randomInt}}`, `{{$randomBoolean}}`, `{{$randomEmail}}`, `{{$randomFirstName}}`,
`{{$randomLastName}}`, `{{$randomAlphaNumeric}}`.

An environments matrix edits every variable in every environment side by side and flags variables used by
requests that are empty in the active environment. The auth editor shows where auth is inherited from and which
items override it.

**Responses** are also saved with each saved request (latest plus five earlier), and the "Sent request" tab shows the
exact request after variables and auth.

**History** — every sent request, grouped by day, searchable; click to reopen.

**Import / export** — Postman Collection v2.0/v2.1 and Postman environment/globals files (import via dialog or by
dropping files on the window), cURL import; export collections as Postman v2.1 and environments as Postman
environment files. Pre-request/test scripts and unsupported body types (e.g. GraphQL) are preserved on round trip.

**Other** — shared cookie jar with a cookie manager, dark and light themes, response below or beside the request.

Not included (by design, for now): tests/scripts execution, collection runner, flows, mocks, monitors, AI features,
GraphQL/WebSocket/gRPC.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+Enter | Send request |
| Enter (in URL bar) | Send request |
| Ctrl+S | Save |
| Ctrl+T / Ctrl+N | New request tab |
| Ctrl+W | Close tab (middle-click a tab also closes it) |
| Ctrl+Tab / Ctrl+Shift+Tab, Ctrl+PgDn / Ctrl+PgUp | Next / previous tab |
| Ctrl+K | Command palette |
| Ctrl+E | Switch environment |
| Ctrl+L | Focus URL bar |
| Ctrl+O | Import |
| Ctrl+, | Settings |
| Ctrl+\\ | Toggle sidebar |
| Ctrl+F | Search in the body / response editor |
| F2 / Delete / Enter | Rename / delete / open the selected sidebar item |

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

## Data

Everything is stored as JSON in the data folder — by default `~/.config/Hermes` (Linux),
`%APPDATA%\Hermes` (Windows) or `~/Library/Application Support/Hermes` (macOS). The data folder can be changed in
Settings; `HERMES_CONFIG_DIR` overrides the config folder (useful for portable setups and tests).

```
settings.json              app settings
collections/{id}.json      one file per collection
environments/{id}.json     one file per environment
globals.json               global variables
history/yyyy-MM-dd.json    request history, one file per day
responses/{requestId}.json last responses of a saved request
session.json               open tabs, drafts, active environment
```

## Project layout

```
src/Hermes.Core     UI-independent library: models, JSON storage, variable resolution, auth (incl. OAuth 2.0),
                    request preparation and execution, Postman/cURL import & export, formatters
src/Hermes.App      Avalonia 12 desktop app (MVVM with CommunityToolkit.Mvvm, AvaloniaEdit + TextMate editors)
tests/Hermes.Core.Tests   xUnit tests for the core library (HTTP tests run against a local HttpListener)
tests/Hermes.App.Tests    Avalonia headless UI tests
```
