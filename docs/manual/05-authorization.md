# Authorization

Set the authorization on a request, folder or collection. Auth fields accept [variables](03-variables.md).

Passwords and client secrets show as dots. Tokens and API key values show as dots too, until you click into the field
to edit them; the eye button keeps one shown until you leave the tab. A token that holds only variables, such as
`{{token}}`, isn't masked, because it shows no secret.

## Types

| Type | What it does |
| --- | --- |
| Inherit | Use the authorization of the parent folder or collection |
| None | Send no authorization |
| Basic | Username and password |
| Bearer | A token in the `Authorization` header |
| API key | A key sent in a header or in the query string |
| OAuth 2.0 | Gets a token, see below |

## Inheritance and overrides

A request set to **Inherit** uses the authorization of its folder, then its collection. Choose another type on the
request, or on a folder, to override what is above it.

A header in a request, folder or collection with the name the effective authorization would add (`Authorization`, or
the key of an API key sent as a header) is sent instead of the header from the authorization, and the row is tagged
**Replaces auth header**. Authorization types that add no header, such as an API key sent in the query, are not
affected. See
[Collections and folders](02-collections-and-tabs.md#collections-and-folders) for how headers are inherited.

## OAuth 2.0 grants

- **Authorization code**
- **Client credentials**
- **Password**

Access and refresh tokens are kept in the keychain. See [Secrets](secrets.md).
