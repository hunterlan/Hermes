# Collections, folders and tabs

## Collections and folders

A collection groups requests. It can contain folders, and folders can contain more folders. A collection or folder
can hold its own [variables](03-variables.md) and [authorization](05-authorization.md), which its requests can
inherit. Use the sidebar to create, rename (`F2`) and delete items. `Ctrl+\` shows or hides the sidebar.

A collection or folder can also have header rows. They are sent with every request inside it, along with the request's
own headers:

- **Nearest wins:** if a request, folder or collection sets a header that an outer level also sets, the nearest one is
  sent. Header names ignore case.
- **Unticked rows** are off. They are not sent, and they don't replace a header of the same name from an outer level.
- **Skipping:** a request can skip an inherited header from its Headers section. This applies to that request only.
- **Authorization:** if a header has the name the request's authorization would add (for example `Authorization`),
  it is sent instead of the one from the authorization, and Hermes shows a warning on the row. See
  [Authorization](05-authorization.md).
- **Postman export:** Postman has no folder headers, so the ticked collection and folder headers are copied into each
  request. Hermes warns you before exporting a collection that has any.

## Tabs

- **Preview tab:** opening an item from the sidebar with a single click shows it in a preview tab, which the next
  item you open replaces.
- **Pinned tab:** pin a tab to keep it. A pinned tab is not replaced while tabs are on.
- **Unsaved changes:** a tab with unsaved changes is kept until you save (`Ctrl+S`) or close it. If saving fails, the
  tab stays unsaved and Hermes says why; see [Troubleshooting](10-troubleshooting.md).

Close a tab with `Ctrl+W` or a middle click. Switch tabs with `Ctrl+Tab` and `Ctrl+Shift+Tab`.

## Turning tabs off

In [Settings](09-settings.md), turn off **Tabs** to work on one item at a time. Only one tab is ever open, and the
tab strip is hidden:

- **Turning it off** closes every tab except the current one, pinned tabs included. Hermes first says how many tabs
  will close, then asks you to save each one with unsaved changes. Cancel at any point to keep tabs on.
- **Opening an item** (request, folder, collection, environment, History entry or a new request) replaces the
  current tab. If the current tab has unsaved changes, Hermes asks first: **Save changes**, **Don't save**, or
  **Cancel** to stay where you are. An entry opened from History is replaced without asking until you edit it.
- **Pinning** has no effect: the pinned tab is replaced like any other.
- **Open in tab** and **Open in new tab** are hidden from the sidebar and History menus.
- **Restarting** brings back only the current tab.

Turning tabs back on doesn't reopen the tabs that were closed.
