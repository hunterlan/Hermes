# Authorization

Set the authorization on a request, folder or collection. Auth fields accept [variables](03-variables.md).

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

## OAuth 2.0 grants

- **Authorization code**
- **Client credentials**
- **Password**

Access and refresh tokens are kept in the keychain. See [Secrets](secrets.md).
