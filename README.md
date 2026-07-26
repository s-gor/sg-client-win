# SG Client v0.0.96

SG Client is a Windows client for VPN profiles and subscriptions from SG-Panel, SG-AWG-Panel and compatible third-party sources.

## Highlights of 096

- Mihomo runtime integration;
- import and export of simple `mierus://` links;
- Mieru TCP and UDP, single ports and port ranges;
- Mieru support in TUN, System Proxy and Local Proxy;
- SG routing conversion for Mihomo;
- profile display, filtering and latency handling for Mieru;
- final **Luxury Jade Depth** light theme with soft gradients, layered cards and restrained shadows;
- original **Graphite** and **Northern** themes preserved;
- theme selector remains in the main header;
- GeoFiles management is located in Routing;
- Settings, Routing, Maintenance and GeoFiles use classic raised surfaces.

## Connection modes

- TUN;
- System Proxy;
- Local Proxy.

## Runtime engines

| Profiles | Engine |
|---|---|
| VLESS REALITY / TLS, raw/TCP and XHTTP | Xray-core |
| Hysteria 2, AnyTLS and other compatible profiles | sing-box |
| Mieru TCP / UDP | Mihomo |
| AmneziaWG `.conf` | AmneziaWG |

## Full build kit

The full release build kit contains the source tree, runtime files and guarded Windows build scripts.

1. Extract the archive completely.
2. Run `START-096.cmd` as administrator.
3. The builder restores NuGet packages, runs tests and publishes the x64 application.
4. The result is written to `build\096\SG-Client.exe`.

Requirements for building:

- Windows 10/11 x64;
- .NET SDK 10.x;
- internet access for NuGet and the official Mihomo verification step when required.

## Source build

```text
dotnet restore v2rayN/v2rayN.sln
dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj -c Release
dotnet publish v2rayN/v2rayN/v2rayN.csproj -c Release -r win-x64 -p:SelfContained=true -p:EnableWindowsTargeting=true
```

## Safety and release verification

The release builder:

- validates the package structure;
- restores previous local profile data only through the guarded migration path;
- verifies the official Mihomo archive by SHA-256;
- runs `ServiceLib.Tests`;
- publishes the WPF application;
- checks required runtime files;
- preserves Graphite and Northern while applying Luxury Jade Depth only to the light theme.

The approved release source passed 61 unit tests on Windows. The final package manifest and XAML/XML files were also rechecked before publication.

## Documentation

- [Quick start](docs/01-QUICK-START.md)
- [Profiles and engines](docs/02-PROFILES-AND-ENGINES.md)
- [TUN and routing](docs/03-TUN-AND-ROUTING.md)
- [DPI](docs/04-DPI.md)
- [Troubleshooting](docs/05-TROUBLESHOOTING.md)
- [Build](docs/06-BUILD.md)
- [Release checklist](docs/07-RELEASE-CHECKLIST.md)

## License and upstream components

SG Client is based on open-source components, including v2rayN. See [LICENSE](LICENSE), [SG-UPSTREAM.md](SG-UPSTREAM.md) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
