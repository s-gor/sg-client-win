# Сборка из исходников

## Требования

- Windows 10/11 x64;
- Git;
- .NET SDK 10.x.

## Команды

```cmd
git clone https://github.com/s-gor/sg-client-win.git
cd sg-client-win
dotnet restore v2rayN\v2rayN.sln
dotnet test v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj -c Release
dotnet publish v2rayN\v2rayN\v2rayN.csproj -c Release -r win-x64 -p:SelfContained=true -p:EnableWindowsTargeting=true -o artifacts\sg-client
```

Готовый `SG-Client.exe` появится в `artifacts\sg-client`.

Runtime-файлы Xray, sing-box, Mihomo, AmneziaWG, Wintun и маршрутизационные базы в исходный build автоматически не загружаются. Для публичного Portable-релиза используется отдельно проверенный runtime-комплект.
