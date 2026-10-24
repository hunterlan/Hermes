# Hermes

## Tech stack
- .NET 10
- Avalonia UI

If avalonia-docs MCP is available, use it when you need additional information related to Avalonia UI.

## Coding instructions

- Use descriptive names for variables and methods.
- Follow the Avalonia UI coding conventions.
- Each class, record, enum must have own file, unless they are private and used by other class.
- Fields of the class/record must be above methods.

## Testing instructions

Before commiting, use `dotnet test` to make sure all tests pass.

## Commit instructions

- Line shouldn't exceed 72 characters.
- Commit message must follow conventional commit specification
- Do not include Claude-Session info into commit body
- You may include yourself as co-author to be transparence in usage of AI
- For fix and hotfix commits, body message must have section "Problem:" and "Solution:" each having no more than 3 lines.