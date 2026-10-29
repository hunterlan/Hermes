# Environments

An environment is a named set of [variables](03-variables.md), for example *Local*, *Staging* or *Production*.
Globals are a set of variables that is always available.

## The picker

`Ctrl+E` opens the environment picker. Type to filter, move with the arrow keys and press `Enter` to choose. The
active environment is saved with your session.

## The matrix

Run **Open environments matrix** from the command palette to see every variable across globals and all environments
side by side, and edit them in place. The matrix also warns about variables used by requests that would be sent
empty or literally in the active environment.

## Color

Give an environment a color to tell it apart at a glance. In the environment's tab, the **Color** row under the
name offers *No color* and eight swatches: red, orange, amber, green, teal, blue, purple and pink. Click one, or
press `Tab` to reach the row and move between swatches with the arrow keys. Save the tab to keep the change.

The color shows as a small dot next to the environment's name:

- on the active environment button in the header,
- in the environment picker,
- in the Environments pane of the sidebar,
- in the column headers of the environments matrix,
- in the response status bar, for the environment the response was sent with.

An environment with no color shows a hollow ring. Each swatch has a light and a dark shade, so the dot follows the
theme. Globals have no color.

The color is only a label. The checkmark in the picker marks the active environment, and the lock marks an
environment that asks before sending. Choosing red does not make an environment ask first.

Postman has no environment colors, so importing a Postman environment gives it no color, and exporting one leaves
the color out.

## Ask before sending

An environment can be set to ask before sending. While it is active, Hermes asks for confirmation before each
request. Use it for environments such as Production. This setting is separate from the color.
