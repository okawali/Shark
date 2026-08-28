[![LICENSE](https://img.shields.io/github/license/mashape/apistatus.svg)](./LICENSE)
[![Build Status](https://github.com/okawali/shark/workflows/Build/badge.svg)](https://github.com/okawali/Shark/actions)

# Shark
A proxy ng

## Installation(choose one)
### pre compiled package
1. download pre packaged executable from release, zip files
1. just unzip

### dotnet tool package(local)
1. download packed dotnet tool from release, and install use `dotnet tool` command, nupkg files 
1. `dotnet tool install shark/shark-server -g --add-source ${folder of nupkg files}`

### dotnet tool package(github package registry)
1. add github package registry `https://nuget.pkg.github.com/okawali/index.json`
1. `dotnet tool install shark/shark-server -g --ignore-failed-sources`

## How to start
### cmd

```sh
shark-server | shark -h
-c, --config=VALUE         config file path, default ${appRoot}/config.yml
-h, --help                 show this message and exit
-v, --version              show version information and exit
```

Print only the version number, without build metadata or starting the client or server:

```sh
shark --version
shark-server --version
```

### Config
```yaml
pluginRoot: ~
backlog: ~
logLevel: 2

shark:
  host: 127.0.0.1
  port: 12306
  max: 0
  auth: none
  keygen: scrypt
  crypto: aes-256-cbc

client:
  host: 127.0.0.1
  port: 1080
  protocol: Socks5
....
```

### Plugins

Plugins are loaded once, before the client or server builds its service provider.
Set `pluginRoot` to the plugin directory; the default is `plugins` beside the executable.
Each immediate subdirectory must contain a DLL with the same name:

```text
plugins/
  Shark.SimpleAuth/
    Shark.SimpleAuth.dll
    Shark.SimpleAuth.deps.json
    ...plugin dependencies...
```

Plugin entry types implement `Shark.Plugins.IPlugin`, have a public parameterless
constructor, and register services in `Configure`. The type itself may be `internal`.
Publish plugins as complete output directories, including their dependency files.

Shark owns its loader, built on .NET's `AssemblyLoadContext` and
`AssemblyDependencyResolver`. Each plugin has its own context, with these rules:

- Managed dependencies first use the host's assembly when its version can satisfy
  the request. Otherwise, the plugin's `.deps.json` is used, followed by the plugin
  directory (or the culture subdirectory for satellite assemblies).
- Native dependencies use the plugin's `.deps.json`, then adjacent libraries with
  the platform's normal filename variations, then the runtime's system-library search.
- Plugins keep their private dependencies separate. They are trusted, run in the
  host process, and stay loaded until the process exits; hot reload is not supported.
- Missing plugin directories are allowed. Unregistered service names retain the
  configured defaults (`aes-256-cbc`, `scrypt`, and `none`) and name matching remains
  case-insensitive. Invalid assemblies and missing required dependencies still fail.

The loader does not add custom probing paths from plugin `runtimeconfig.json` or
`runtimeconfig.dev.json` files, or download missing dependencies. Deploy plugin
dependencies with the plugin instead.
