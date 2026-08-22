# Проверка релиза

## Автоматически

- восстановление NuGet-зависимостей;
- запуск `ServiceLib.Tests`;
- Release-публикация WPF-проекта;
- проверка наличия `SG-Client.exe`;
- проверка runtime-комплекта Xray, sing-box, Mihomo и AmneziaWG;
- проверка структуры Portable ZIP;
- проверка отсутствия пользовательских профилей, конфигураций, журналов, latency-cache и backup-дублей GeoFiles;
- SHA-256 release-файлов.

## Вручную

- чистый запуск Portable на Windows 10/11 x64;
- импорт обычного профиля и подписки;
- импорт/обновление SG-native подписки с AWG2/AWG3;
- включение и отключение TUN;
- переключение профиля при работающем TUN;
- System Proxy и Local Proxy;
- Xray, Hysteria 2, Mieru, AnyTLS, TUIC v5, AWG2 и AWG3;
- проверка latency, включая Mieru UDP, TUIC v5 и AWG2/AWG3;
- RU White List для Xray/sing-box/Mihomo и AmneziaWG;
- Kill Switch и аварийный сброс;
- перенос окна между мониторами и low-resolution UI;
- окна «Настройки», «Маршрутизация», «DPI», «Раздельный TUN», «Справка»;
- live Connections и route diagnostics;
- отсутствие пользовательских данных в финальном Portable.

Публикация разрешается только после обязательного smoke-теста финального Portable.
