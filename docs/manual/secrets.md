# Secrets

Hermes keeps secrets in your operating system's keychain instead of the JSON files in the data folder.

## What is stored in the keychain

- Variables with the secret type (both the initial and the current value)
- Credentials typed directly into an auth field: Basic password, Bearer token, API key value, OAuth client secret,
  OAuth password, and the OAuth access and refresh tokens
- The auth credentials of unsaved request drafts, so they survive a restart

An auth field counts as a credential unless it consists only of `{{variable}}` references. Text such as
`Bearer-{{token}}` is stored in the keychain.

## Where it is stored

| System | Storage |
| --- | --- |
| macOS | Keychain |
| Linux | The keyring, through libsecret (default collection) |
| Windows | A file encrypted with DPAPI, `secrets-<id>.bin`, in the Hermes config folder |

All secrets of one data folder are saved together as a single entry, so macOS asks for access once.

The entry is named after the id in the data folder's `workspace.json`, not after its path. Moving or renaming the
folder keeps its secrets. A copied folder gets its own id and a copy of the secrets. See [Data](08-data-folder.md).

## What stays in the JSON files

Everything else, including auth fields that consist only of `{{variable}}` references. The reference is saved as
written, and the value comes from the variable. That is why the data folder is safe to commit.

## Where secrets are hidden

- Saved responses show credentials in the sent request as masked values. The `Authorization` and
  `Proxy-Authorization` headers are always masked. Hermes also masks a secret wherever the same text shows up in the
  URL, headers or body (also URL-encoded), but only for secrets of 4 or more characters, because a shorter one would
  hide unrelated text.
- History does not keep literal auth credentials.
- When an export contains secrets, Hermes asks whether to include them. Closing the dialog leaves them out, and they
  are exported blank.

## No keychain available

If the system has no usable keychain (for example Linux without a keyring), or the keychain fails while Hermes is
running, secrets are saved as plain text in the data folder. The status bar says so and gives the reason.

## Tests

Setting the environment variable `HERMES_SECRET_STORE=memory` keeps secrets in memory only. Nothing is saved.
