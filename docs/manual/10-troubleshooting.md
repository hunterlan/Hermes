# Troubleshooting

## Requests that fail before a response

When a request can't be sent or no response comes back, Hermes names the problem in plain words, the same on every
operating system. The headline says what went wrong, the facts name the host, port, file or header, and one button
offers the next step.

| Headline | Meaning | Next step |
|---|---|---|
| Couldn't find the server | The host name doesn't resolve. Check the URL, your network or VPN. | Edit URL |
| Server isn't accepting connections | Nothing is listening on that host and port. Is the server running? | Retry |
| Connection was closed by the server | The server closed the connection before the response finished. | Retry |
| URL isn't valid | The facts say what is wrong: empty, no `http://`, no host, an unresolved `{{variable}}`. | Edit URL |
| Body file can't be read | The file doesn't exist, is a folder, or you have no permission to read it. | Choose file |
| Header name isn't valid / Header value isn't valid | The header can't be sent. The value of a credential header is not repeated. | Show headers |

**Details** is collapsed and holds the original message and the error codes. **Copy details** puts them on the
clipboard for a bug report.

## Certificate errors

When a server's certificate fails its check, Hermes stops before sending anything and says why:

| Headline | Meaning |
|---|---|
| Certificate expired | The certificate's dates have passed. The facts give the host and the date. |
| Certificate isn't valid yet | The certificate starts in the future. Check the computer's clock. |
| Certificate is self-signed | The server signed its own certificate. Common for local and test servers. |
| Certificate isn't from a trusted authority | The issuer is named. If it is your company's authority, add it to the system's trusted certificates. |
| Certificate is for a different address | The names it was issued for are listed. Check the URL for a typo or a missing subdomain. |
| Secure connection failed | Any other problem with the secure connection. The message is shown as it is. |

Two buttons sit under the facts:

- **Turn off verification for this request** switches SSL certificate verification off for this request only and
  sends it again. The app-wide setting stays as it is. Only do this for servers you trust.
- **Open request settings** shows the request's Settings section, expanded, so you can change it yourself.

**Details** is collapsed. It shows who the certificate was issued to and by, the dates it is valid, which check failed,
and the original message. **Copy details** puts all of it on the clipboard for a bug report.

To turn verification off for every request instead, use **SSL certificate verification** in
[Settings](09-settings.md).

## Timeouts

A request that takes longer than **Request timeout** fails. Raise the value in [Settings](09-settings.md), or set it
to 0 to wait forever.

## Variables are sent as written

`{{name}}` in the sent request means the variable is not defined in the active environment or above it. Check the
[resolution order](03-variables.md) and the [environment matrix](04-environments.md).

## No keychain

Without a usable keychain, secrets are saved as plain text in the data folder. See [Secrets](secrets.md).

## macOS blocks the app

The macOS app is not notarized. On first launch, right-click Hermes.app and choose Open, or run
`xattr -dr com.apple.quarantine /Applications/Hermes.app`.

## Windows SmartScreen

The Windows installer is unsigned, so SmartScreen warns before running it.

## Files that couldn't be loaded or saved

When Hermes can't read a file in the [data folder](08-data-folder.md), it tells you which collection, environment or
file it is, and why, in a dialog. Each problem comes with a next step:

| Message | What to do |
|---|---|
| It isn't valid JSON (line 41, column 8) | The file is damaged, often cut short by a crash or a sync tool. Choose **Reveal file** to repair it by hand, or **Move aside** to rename it to `.corrupt` so Hermes starts without it. Nothing is deleted. |
| Hermes isn't allowed to read or write it | Check the permissions of the folder, then try again. |
| The disk is full | Free up disk space, then try again. |
| Another program is using it | Close the program that has the file open (a sync or backup tool, or another Hermes window), then try again. |
| It or its folder wasn't found | Check that the folder still exists and that its drive is connected, then try again. |

The technical message is kept under **Details**; choose **Copy details** to put it on the clipboard, for example to
include in a bug report.

A damaged line in a history file is skipped and reported once; the other entries load normally.

If Hermes can't use the data folder when it starts, it says which folder and why. Choose **Use default folder** to
go back to the default location, or **Choose another folder** to pick one. Your files in the old folder are not
touched.
