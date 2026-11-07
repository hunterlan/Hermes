# Responses

## Views

The response body can be shown prettified (highlighted) or as raw text. Responses larger than the limit in
[Settings](09-settings.md) are shown as raw text to stay responsive. `Ctrl+F` searches the body.

## The exact sent request

Each response shows the request as it was sent, after variables were resolved: the request line, one row per header
and the body. Credentials (headers set by auth, `Authorization`, `Proxy-Authorization`, `Cookie` and `X-Api-Key`) show
as dots. The eye reveals a value and the copy button copies the real one, also while it is masked. Values stay on one
line; the expand button shows a long one in full. The same buttons are on the header lists of a request. See
[Secrets](secrets.md).

## Saved responses

The last responses of a saved request are kept in the data folder, in `responses/`. See
[Data folder](08-data-folder.md).

## Cookies

Hermes keeps cookies between requests. Open the cookies from the status bar. **Clear all cookies** in
[Settings](09-settings.md) removes them.

## Timing

The response shows the time the request took.
