# Troubleshooting

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
