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

## OAuth 2.0 token errors

When **Get New Access Token** fails, the Auth panel shows the same four parts as a failed request: a headline, the
facts, one button for the next step, and a collapsed **Details** with **Copy details**.

| Headline | Meaning | Next step |
|---|---|---|
| Couldn't find the server, Server isn't accepting connections, Server didn't answer | The token URL can't be reached. They read as for requests above. | Edit token URL, or Try again |
| Certificate ... | The token server's certificate failed its check. See below. | Open settings |
| Port 8080 is already in use | Another program uses the callback port. Choose another port in **Callback URL**; the field is selected for you. | Edit callback URL |
| The token server doesn't accept this client, ... rejected the authorization, ... rejected the scope | The server answered with an OAuth error such as `invalid_client`, `invalid_grant` or `invalid_scope`. The facts say what to check and quote the server's own description. | Try again, when it helps |
| The token server refused the request | An error code Hermes has no wording for. The facts show the code and the server's description. | |
| Couldn't get a token | Something unexpected happened. Open **Details** and use **Copy details** when you report it. | |

Token requests follow the SSL setting in effect where you edit the auth. On a request that has its own **Enable SSL
certificate verification** switch in its Settings section, that switch applies and the error offers **Open request
settings**. Everywhere else, including folders, collections and requests with no setting of their own, the app-wide
**SSL certificate verification** in [Settings](09-settings.md) applies, and the error offers **Open settings**.

## Timeouts

A request that takes longer than **Request timeout** fails. Raise the value in [Settings](09-settings.md), or set it
to 0 to wait forever.

## Variables are sent as written

`{{name}}` in the sent request means the variable is not defined in the active environment or above it. Check the
[resolution order](03-variables.md) and the [environment matrix](04-environments.md).

## No keychain

Without a usable keychain, secrets are saved as plain text in the data folder. Click "Secrets aren't protected" in the
status bar to see why and what to do. On Linux you need libsecret and a keyring service (GNOME Keyring or KWallet);
restart Hermes after installing or starting one. See [Secrets](secrets.md).

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

### When saving fails

If Hermes can't write a collection, environment, folder or request (the data folder is read-only, the disk is full or
another program has the file open), a **Couldn't save** dialog says which one and why:

- Nothing is lost. The tab keeps its changes and stays marked as unsaved, and what you see in the sidebar is what is
  on disk.
- Fix the problem, then choose **Retry** to save again. **Cancel** leaves the tab unsaved. **Reveal file** or
  **Reveal folder** opens the file manager where the file belongs.
- When a rename, move, delete or new item can't be saved, the dialog says why and the change is undone: the name
  stays as it was, the item stays where it was, and a collection or environment that couldn't be written is not added.
- If the open tabs and drafts (the session) can't be saved, Hermes tells you once and keeps trying quietly, so you
  aren't interrupted every half minute. It tells you again if saving works and then fails again.
- If Settings can't be saved, the error appears in the Settings dialog and the dialog stays open, so you can try again.
- Exporting a collection or environment, or saving a response to a file, names the file that couldn't be written and
  why, for example when the folder you picked is read-only. Pick another location and try again.

## Something went wrong

If Hermes hits a problem it didn't expect, it shows a **Something went wrong** dialog and then closes when you choose
**Quit**. It doesn't ask to save and doesn't write the session again, because it can't tell what state it is in. Tabs
and drafts as of the last automatic save (every half minute) come back the next time you start Hermes. Choose **Copy
details** and include them when you report the problem.
