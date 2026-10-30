# Variables

Write a variable as `{{name}}` in a URL, header, body or auth field. Hermes replaces it when the request is sent.
Names are suggested when you type `{{`.

## Resolution order

When the same name is defined in several places, the closest one wins:

1. the request
2. the folder
3. the collection
4. the environment
5. globals

## Types

- **Default:** a normal variable, saved in the JSON files.
- **Secret:** the value is kept in the OS keychain. See [Secrets](secrets.md).

## Dynamic variables

Variables that start with `$`, such as `{{$guid}}`, give a new value each time. See
[Dynamic variables](dynamic-variables.md).

## Missing variables

A variable that is not defined is sent as written. The [environment matrix](04-environments.md) warns about
variables that would be sent empty or literally.
