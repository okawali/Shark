# Shark
A proxy ng

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

# LICENSE
MIT
