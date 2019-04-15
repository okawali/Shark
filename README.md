[![license](https://img.shields.io/github/license/mashape/apistatus.svg)](./LICENSE)
[![LICENSE](https://img.shields.io/badge/license-Anti%20996-blue.svg)](https://github.com/996icu/996.ICU/blob/master/LICENSE)

# Shark
A proxy ng

## installation
1. download pre packaged executable from release, zip files
1. download packed dotnet tool from release, and install use `dotnet tool` command, nupkg files 

## how to start
### cmd

```sh
shark-server | shark -h
-c, --config=VALUE         config file path, default ${appRoot}/config.yml
-h, --help                 show this message and exit
```

### config
```yaml
pluginRoot: ${appRoot}\plugins
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
  protocol: socks5
....
```
