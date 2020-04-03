[![license](https://img.shields.io/github/license/mashape/apistatus.svg)](./LICENSE)
[![LICENSE](https://img.shields.io/badge/license-Anti%20996-blue.svg)](https://github.com/996icu/996.ICU/blob/master/LICENSE)
![](https://github.com/okawali/shark/workflows/Build/badge.svg)

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
