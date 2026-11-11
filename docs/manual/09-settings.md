# Settings

Open Settings with `Ctrl+,`.

## Appearance

| Setting | Meaning |
| --- | --- |
| Theme | Dark, Light or Sync with OS |
| Layout | Response below or beside the request |
| Tabs | When off, only one tab is open and opening another item replaces it. See [Turning tabs off](02-collections-and-tabs.md#turning-tabs-off) |
| Tabs kept ready | How many recently used tabs switch back instantly, the open one included. 1 to 10, 3 by default. See [Tabs kept ready](#tabs-kept-ready) |

### Tabs kept ready

Hermes keeps the views of your most recently used tabs in memory, so switching back to one of them is instant, even with a large response. The other tabs share those views: opening one takes a moment longer while its request and response are loaded into a view.

Each kept tab uses memory: a few megabytes for small requests, about 60 MB with a 1 MB response. With many large responses, a lower number saves memory; with plenty of memory, a higher one makes more tabs instant. The number doesn't limit how many tabs you can open.

## Requests

| Setting | Meaning |
| --- | --- |
| Request timeout (ms) | How long to wait for a response. 0 waits forever |
| SSL certificate verification | Verify server certificates. A request can override it |
| Automatically follow redirects | A request can override it |
| Max response size for pretty view (MB) | Larger responses are shown as raw text |
| Cookies | Clear all cookies |

## Data

| Setting | Meaning |
| --- | --- |
| History retention (days) | Number of days to keep, counting today. 0 keeps history forever. At most 36500 |
| Data folder | Where the JSON files are stored. Restart Hermes after changing it |

### History retention

With a retention of 7, Hermes keeps today and the 6 days before it. With 1 it keeps only today. A value of 0 keeps history forever. The most you can enter is 36500 days.

Older history is removed as soon as you save the setting, and again each time the date changes while Hermes is open. You don't need to restart. Removed entries leave the History sidebar and their files are deleted, so they can't be brought back.

The value must be a whole number, 0 or more. If it isn't, the dialog explains why under the field and Save stays disabled until you fix it.

If the settings can't be written, the reason appears at the bottom of the dialog and it stays open, so you can fix the
problem and press Save again, or Cancel to keep the old settings. See [Troubleshooting](10-troubleshooting.md).

See [Data folder](08-data-folder.md).
