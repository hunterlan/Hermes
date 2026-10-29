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

## Ask before sending

An environment can be set to ask before sending. While it is active, Hermes asks for confirmation before each
request. Use it for environments such as Production.
