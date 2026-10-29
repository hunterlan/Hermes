# Collections, folders and tabs

## Collections and folders

A collection groups requests. It can contain folders, and folders can contain more folders. A collection or folder
can hold its own [variables](03-variables.md) and [authorization](05-authorization.md), which its requests can
inherit. Use the sidebar to create, rename (`F2`) and delete items. `Ctrl+\` shows or hides the sidebar.

## Tabs

- **Preview tab:** opening an item from the sidebar with a single click shows it in a preview tab, which the next
  item you open replaces.
- **Pinned tab:** pin a tab to keep it. A pinned tab is not replaced while tabs are on.
- **Unsaved changes:** a tab with unsaved changes is kept until you save (`Ctrl+S`) or close it.

Close a tab with `Ctrl+W` or a middle click. Switch tabs with `Ctrl+Tab` and `Ctrl+Shift+Tab`.

## Turning tabs off

In [Settings](09-settings.md), turn off **Tabs** to work on one item at a time. Only the current tab and tabs with
unsaved changes stay open:

- **Turning it off** closes every other tab without unsaved changes, pinned tabs included. Hermes first says how
  many tabs will close; cancel to keep tabs on.
- **Opening an item** (request, folder, collection or environment) replaces the current tab. If the current tab has
  unsaved changes, the item opens next to it instead.
- **The tab strip** only shows while another tab has unsaved changes. Once those are saved or closed, it hides again.
- **Switching** to an unsaved tab, by clicking it or with `Ctrl+Tab`, closes the tab you were on if it has no unsaved
  changes.
- **Pinning** has no effect: pinned tabs are replaced and closed like any other.
- **Open in tab** and **Open in new tab** are hidden from the sidebar and History menus.
- **Restarting** brings back only the current tab and tabs with unsaved changes.

Turning tabs back on doesn't reopen the tabs that were closed.
