# Troubleshooting

## Certificate errors

If a server uses a self-signed or private certificate, the request fails with a certificate error. Turn off
**SSL certificate verification** in [Settings](09-settings.md), or only on the request that needs it. Only do this
for servers you trust.

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
