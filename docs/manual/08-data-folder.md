# Data

Everything is stored as JSON in the data folder — by default `~/.config/Hermes` (Linux),
`%APPDATA%\Hermes` (Windows) or `~/Library/Application Support/Hermes` (macOS). The data folder can be changed in
Settings; `HERMES_CONFIG_DIR` overrides the config folder (useful for portable setups and tests).

```
settings.json              app settings
collections/{id}.json      one file per collection
environments/{id}.json     one file per environment
globals.json               global variables
history/yyyy-MM-dd.json    request history, one file per day
responses/{requestId}.json last responses of a saved request
session.json               open tabs, drafts, active environment
workspace.json             the data folder's id and last known path
```

`workspace.json` holds a random id that Hermes uses to find this folder's secrets in the OS keychain (see
[Secrets](secrets.md)). Because the id is inside the folder, you can move or rename the folder and keep its secrets.
If you delete the file, Hermes makes a new id and can no longer find the old secrets. If you copy the folder, the copy
gets its own id and a copy of the secrets.

## Backup and Git

The data folder is plain JSON, so a backup is a copy of the folder. To keep it in Git, make the folder a repository.
Secrets are not in the folder (see [Secrets](secrets.md)), so it is safe to commit. `session.json` and `history/`
change often and are usually left out with a `.gitignore`.
