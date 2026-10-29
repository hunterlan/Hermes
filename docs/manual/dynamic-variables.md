# Dynamic variables

Dynamic variables give a new value every time they are used. Write them like normal variables, for example
`{{$guid}}` in a URL, header, body or auth field. The value is made when the request is prepared for sending.

| Variable | What it gives you | Example |
| --- | --- | --- |
| `{{$guid}}` | A random GUID | `3f2b8c1e-5a47-4d0b-9c36-7e1d2a9b4f10` |
| `{{$randomUUID}}` | A random GUID, same as `$guid` | `9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d` |
| `{{$timestamp}}` | Current time as Unix seconds (UTC) | `1792833600` |
| `{{$isoTimestamp}}` | Current time in ISO 8601 (UTC) | `2026-10-24T09:20:00.000Z` |
| `{{$randomInt}}` | A whole number from 0 to 1000 | `482` |
| `{{$randomBoolean}}` | `true` or `false` | `true` |
| `{{$randomEmail}}` | A fake email address | `maria.417@example.org` |
| `{{$randomFirstName}}` | A first name from a short built-in list | `Olena` |
| `{{$randomLastName}}` | A last name from a short built-in list | `Kovalenko` |
| `{{$randomAlphaNumeric}}` | One random character, a-z or 0-9 | `k` |

## Good to know

- Names are case-sensitive and always start with `$`.
- Using the same variable twice in one request gives two different values.
- If you define your own variable with the same name, yours is used.
- An unknown name such as `{{$foo}}` is left as it is.
- Names are suggested when you type `{{`.
- Names and emails come from short built-in lists (8 first names, 7 last names, 3 email domains).
