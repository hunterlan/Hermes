# Hermes design guide

How Hermes looks and why. Read this before you add or change UI. The tokens below are defined in
[`Palette.axaml`](../src/Hermes.App/Themes/Palette.axaml) (colours) and
[`Sizes.axaml`](../src/Hermes.App/Themes/Sizes.axaml) (type, radii, spacing). When you change a value there, change
it here in the same commit.

## Principles

- **Local.** No account, plain JSON files in one data folder, nothing leaves the machine unless you send a request.
- **Quiet.** Only what a request uses is on screen; the rest waits as a chip or in Settings.
- **Honest.** Variables show whether they resolve, auth and headers show where they come from, and the exact
  request sent can always be inspected.

## Tokens

Use resources, never literal values. Colours are `{DynamicResource …}` (they change with the theme); sizes are
`{StaticResource …}`. In C#, bind with `control.Bind(Property, this.GetResourceObservable("Key"))`.

### Colour

| Brush | Dark | Light | Where it's used |
|---|---|---|---|
| `BgPrimaryBrush` | `#181B22` | `#FFFFFF` | Window, sidebar, header bar, status bar, active tab, welcome actions, pickers and flyouts, inheritance chain nodes, matrix header cells, checked segment |
| `BgSecondaryBrush` | `#12141A` | `#F4F5F8` | Tab strip, side panels, table headers, import drop zone, cURL snippet box |
| `BgTertiaryBrush` | `#212631` | `#ECEEF3` | Tags, segmented control track, selected auth pill, “No response” chip, search box, welcome icon tiles and hover, active matrix column, layout toggle when on |
| `BgInputBrush` | `#12141A` | `#F4F5F8` | URL bar, bordered inputs and variable fields, focused table cell |
| `BgHoverBrush` | `#212631` | `#ECEEF3` | Pointer-over on outline, ghost, icon and chip buttons, tree and list rows, tabs, history rows, table cells, picker items |
| `BgSelectedBrush` | `#2A303C` | `#E2E5EC` | Selected tree and list items, checked ghost toggles |
| `BgCodeBrush` | `#0E1015` | `#EEF0F5` | Raw body editor, response body text view, sent request view, header value boxes |
| `BorderBrush` | `#2D3340` | `#D3D8E1` | Dividers, outline buttons, cards, chain nodes, matrix cells, tables, splitters |
| `BorderStrongBrush` | `#3D4554` | `#B4BCC9` | Dashed “Not used yet” chips, chain links, import drop zone, variable suggestion popup |
| `TextPrimaryBrush` | `#E9EBF0` | `#161A22` | Main text, hovered and checked buttons, section headers, manual code blocks |
| `TextSecondaryBrush` | `#A3ACBA` | `#465062` | Supporting text (.secondary), ghost and icon buttons, chips, tab labels, sidebar icons, chain details |
| `TextTertiaryBrush` | `#8590A1` | `#586274` | Hints and meta (.tertiary), watermarks, picker captions, response timings, struck-out chain details, “no colour” dot |
| `AccentBrush` | `#F0B84A` | `#8F5600` | Logo, selected tab, unsaved dot, accent buttons, source node in the inheritance chain, swatch focus ring, welcome icons, timing bar “wait” segment |
| `AccentHoverBrush` | `#F5C86E` | `#7A4900` | Accent buttons on pointer-over |
| `AccentInkBrush` | `#17120A` | `#FFFFFF` | Text and icons on accent, Send and danger buttons; the shortcut hint inside Send |
| `TabItemHeaderSelectedPipeFill` | `#F0B84A` | `#8F5600` | Selection pipe under the selected TabItem (Body, Headers…); read by the Fluent theme, not by Hermes code |
| `SendBrush` | `#F0B84A` | `#8F5600` | Send button, link buttons (Bulk Edit, Reset all) |
| `SendHoverBrush` | `#F5C86E` | `#7A4900` | Send and link buttons on pointer-over |
| `SuccessBrush` | `#62D6A0` | `#0D6E45` | 2xx status, ok tags, secret key icons, timing bar “download” segment |
| `SuccessBgBrush` | `#14342A` | `#DFF3E9` | Behind ok tags and the 2xx status chip |
| `RedirectBrush` | `#80B9FF` | `#1B5BB8` | 3xx status text |
| `InfoBrush` | `#80B9FF` | `#1B5BB8` | Info tags, variable names in the matrix and environment quick look |
| `InfoBgBrush` | `#172A44` | `#E1ECFB` | Behind info tags |
| `WarningBrush` | `#F0B84A` | `#7D5200` | 4xx status (with alert icon), warn tags, matrix warning icon, “empty” in quick look |
| `WarningBgBrush` | `#3A2F14` | `#FBEFD4` | Behind warn tags, matrix “empty in this environment” notice |
| `ErrorBrush` | `#FF8179` | `#B0241C` | 5xx and failed requests, error tags, danger buttons, import and settings errors, matrix and environment error icons |
| `ErrorBgBrush` | `#3A1C1E` | `#FBE4E2` | Behind error tags and the failed-request chip |
| `VarResolvedBrush` | `#80B9FF` | `#1B5BB8` | Resolved variables in the URL, params, headers and auth fields |
| `VarResolvedBgBrush` | `#172A44` | `#E1ECFB` | Behind resolved variables; sidebar drop-target highlight while dragging |
| `VarUnresolvedBrush` | `#F0B84A` | `#7D5200` | Unresolved variables; Hermes asks before sending |
| `VarUnresolvedBgBrush` | `#3A2F14` | `#FBEFD4` | Behind unresolved variables |

Every text colour passes 4.5:1 on the surfaces it's used on, in both themes.

#### HTTP methods

Keys are `Method<VERB>Brush`, for example `MethodPOSTBrush`. Used for the method badge in tabs, the sidebar tree,
history, the command palette and auth overrides. `OTHER` covers custom methods such as PURGE.

| Method | Dark | Light |
|---|---|---|
| GET | `#80B9FF` | `#1B5BB8` |
| POST | `#62D6A0` | `#0D6E45` |
| PUT | `#F0B84A` | `#7D5200` |
| PATCH | `#F0B84A` | `#7D5200` |
| DELETE | `#FF8179` | `#B0241C` |
| HEAD | `#80B9FF` | `#1B5BB8` |
| OPTIONS | `#A3ACBA` | `#465062` |
| OTHER | `#A3ACBA` | `#465062` |

#### Environment colours

Keys are `EnvironmentColor<Name>Brush`. Each has a dark and a light shade so it stays readable in both themes. The
user picks one per environment; it's shown as a dot in the header button, environment picker, sidebar, matrix column
headers and status bar. The colour carries no status: the lock (asks before sending) and the checkmark (active) do.

| Environment colour | Dark | Light |
|---|---|---|
| Red | `#FF8179` | `#C62F26` |
| Orange | `#FF9F5A` | `#B8540F` |
| Amber | `#F0B84A` | `#8F5600` |
| Green | `#62D6A0` | `#0D6E45` |
| Teal | `#4FD1D9` | `#0B6E78` |
| Blue | `#80B9FF` | `#1B5BB8` |
| Purple | `#B69CFF` | `#6C45C9` |
| Pink | `#F58CC8` | `#B02C7A` |

### Type

| Token | Size | Use for |
|---|---|---|
| `FontSizeMicro` | 10 | Method badges in tabs and the sidebar |
| `FontSizeCaption` | 11 | Tags, keyboard hints, mono meta (timings, sizes) |
| `FontSizeSmall` | 12 | Labels, secondary text, mono data rows (headers, tables) |
| `FontSizeBody` | 13 | Default text and editors |
| `FontSizeSubtitle` | 14 | Section titles and lead text |
| `FontSizeTitle` | 16 | Dialog and tab titles |
| `FontSizeHeading` | 18 | Page headings (`TextBlock.h1`) |
| `FontSizeDisplay` | 32 | The welcome page headline |

Sizes are in device-independent pixels. There are no half steps.

Fonts are Inter (UI) and `MonoFont` (JetBrains Mono first) for URLs, variables, headers and bodies. Use the
`mono` class instead of setting a font family.

### Corner radius

| Token | Radius | Use for |
|---|---|---|
| `RadiusXSmall` | 4 | Tables, bordered inputs, list rows, drop targets |
| `RadiusSmall` | 5 | Tags, keyboard hints, segmented buttons |
| `RadiusMedium` | 6 | Status chips, picker and suggestion lists, value boxes, drop zones |
| `RadiusControl` | 7 | Buttons, pills, segmented controls |
| `RadiusLarge` | 8 | Icon tiles |
| `RadiusCard` | 10 | Cards, the URL bar, notices, flyouts |
| `RadiusPopup` | 12 | Command palette and large popups |
| `RadiusTabTop` | 8,8,0,0 | Tab headers |
| `RadiusCardInnerStart` | 9,0,0,9 | A part inside a card's left edge (card radius minus its 1px border) |
| `RadiusBarStart` | 4,0,0,4 | First segment of a bar |
| `RadiusBarEnd` | 0,4,4,0 | Last segment of a bar |

Pills and circles (chips, colour swatches) use half their height and stay literal. `0` stays literal.

### Spacing

Gaps between items: `StackPanel.Spacing`, `Grid.ColumnSpacing`, `WrapPanel.ItemSpacing` and similar.

| Token | Gap | Typical use |
|---|---|---|
| `Spacing2` | 2 | Lines inside one item (name over detail), groups of icon buttons |
| `Spacing4` | 4 | Small inline groups: auth pills, keyboard hints |
| `Spacing6` | 6 | Header groups, rows of tags and small controls |
| `Spacing8` | 8 | Default between controls; dialog buttons |
| `Spacing10` | 10 | Fields in a form, items in a dialog or tab page |
| `Spacing12` | 12 | Groups inside a section |
| `Spacing14` | 14 | Blocks in a card or editor, wide inline groups |
| `Spacing16` | 16 | Settings blocks, legend items |

Margins and paddings use the same steps, plus 20 and 24 for dialog and page edges.

### Failure view

What the response pane shows when a request fails (headline, facts, next step, collapsed details).

| Token | Size | Use for |
|---|---|---|
| `SizeFailureIconTile` | 32 | The square tile behind the failure's icon (`ErrorBgBrush`, `RadiusLarge`) |
| `WidthFailureMax` | 560 | Widest the failure's text runs, so it stays a readable column |

## The token rule

- Don't write a literal `FontSize`, `CornerRadius` or gap. Use a token.
- A value with no token gets one: add it to `Sizes.axaml` and to the tables above in the same change. Prefer
  snapping to an existing step.
- Margins and paddings are still mostly literal. When you edit a line that has one, snap it to the scale, and
  introduce a token if the value repeats.

## Rules

- **Never colour alone.** Status is icon + text + colour. Environment colours carry no status of their own.
- **Contrast.** Every text colour passes 4.5:1 on its background, in dark and light. Check new colours in both.
- **Amber is the brand colour, used sparingly.** `Accent` marks the logo, the primary action (Send), selection
  (tab pipe, active checkmark) and unsaved changes. Link buttons (Bulk Edit, Reset all) use `SendBrush`. Don't use
  it for decoration. Warnings use the amber `Warning` brushes, always with an icon and text.
- **Method colours are fixed.** GET/HEAD blue, POST green, PUT/PATCH amber, DELETE red, others grey.

## Icons

Filled [Material Design Icons](https://pictogrammers.com/library/mdi/) (Apache 2.0). Each is a `StreamGeometry`
named `Icon<Name>` in [`Icons.axaml`](../src/Hermes.App/Themes/Icons.axaml), drawn with `PathIcon` at 16 px
(the default `PathIcon` style). Add new icons from the same set; don't mix in outline icons.

## Design canvas

New screens are sketched on the [Hermes UI Design](https://claude.ai/artifact/3G6PCKemdGpp3W9fH4JJCA) canvas. The
code is the source of truth: when they differ, update the canvas. The canvas uses the same type scale. Its outline
icons are sketches; the app uses the filled icons above. Its CSS variables map to brushes like this:

| Canvas | Brush |
|---|---|
| `--bg` | `BgSecondary` |
| `--panel` | `BgPrimary` |
| `--raise` | `BgTertiary` |
| `--sel` | `BgSelected` |
| `--code` | `BgCode` |
| `--line`, `--strong` | `Border`, `BorderStrong` |
| `--text`, `--mute`, `--faint` | `TextPrimary`, `TextSecondary`, `TextTertiary` |
| `--accent`, `--ink` | `Accent`, `AccentInk` |
| `--ok`, `--info`, `--warn`, `--err` (+ `…bg`) | `Success`, `Info`, `Warning`, `Error` (+ `…Bg`) |
| `--c-<name>` | `EnvironmentColor<Name>` |
