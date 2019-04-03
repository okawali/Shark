[![license](https://img.shields.io/github/license/mashape/apistatus.svg)](./LICENSE)
[![LICENSE](https://img.shields.io/badge/license-Anti%20996-blue.svg)](https://github.com/996icu/996.ICU/blob/master/LICENSE)

# Shark
A proxy ng

## installation
1. download pre packaged executable from release, zip files
1. download packed dotnet tool from release, and install use `dotnet tool` command, nupkg files 

## how to start
```sh
shark-server -h
-a, --addr=VALUE           bind address default='127.0.0.1'
-p, --port=VALUE           bind port default=12306
-b, --backlog=VALUE        accept backlog default use SocketOptionName.MaxConnections
    --log-level=VALUE      log level, one of 
			   Trace, Debug, Information, Warning, Error, Critical, None,
			   default Information
-h, --help                 show this message and exit
```

```sh
shark -h
    --local-address=VALUE  bind address default='127.0.0.1'
    --local-port=VALUE     bind port default=1080
    --remote-address=VALUE remote address default='127.0.0.1'
    --remote-port=VALUE    remote port default=12306
    --protocol=VALUE       proxy protocol socks5 or http, defualt=socks5
    --backlog=VALUE        accept backlog default use SocketOptionName.MaxConnections
    --max=VALUE            max client connection count, 0 for unlimited, default 0
    --log-level=VALUE      log level, one of 
    			   Trace, Debug, Information, Warning, Error, Critical, None,
			   default Information
-h, --help                 show this message and exit
```
