# Getting started

## Send your first request

1. Press `Ctrl+T` to open a new request tab.
2. Type a URL in the URL bar, for example `https://example.com`.
3. Press `Ctrl+Enter` (or `Enter` in the URL bar) to send it.

The response appears next to or below the request, depending on the layout in [Settings](09-settings.md).
Press `Ctrl+S` to save the request into a collection.

## Import

Press `Ctrl+O`, or use **Import** on the welcome page. Hermes imports:

- Postman collections and environments
- cURL commands

You can also paste a cURL command straight into the URL bar of a request, or drop a file onto the window.
If a file has a problem, Hermes says what and where it is, for example `item[3].request.url`. When only some items
of a collection are wrong, Hermes imports the rest and lists the skipped items after the import. Requests with
no URL are imported blank and listed too. Click **Details**,
or **Copy details**, for the technical text when you report a problem.

## Command palette

`Ctrl+K` opens the command palette. It lists the commands of the app, with their shortcuts. See
[Keyboard shortcuts](07-keyboard-shortcuts.md).
