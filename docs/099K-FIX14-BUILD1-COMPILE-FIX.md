# Исправление компиляции SG Client 099K FIX14 BUILD1

## Ошибка исходного FIX14

Windows-сборка останавливалась с сообщением:

```text
ServiceLib/Handler/ConfigHandler.cs(...): error CS0103:
The name 'ProfilesViewModel' does not exist in the current context
```

## Причина

Общий batch-импортер находился в `ServiceLib.Handler.ConfigHandler`, но напрямую вызывал `ProfilesViewModel.Instance`. Это создавало недопустимую связь обработчика импорта с моделью представления и не компилировалось в данном пространстве имён.

## Исправление

```text
ConfigHandler
→ AppEvents.ProfileRevealRequested(profileId)
→ ProfilesViewModel на UI-планировщике
→ выделение и прокрутка импортированного профиля
```

Дополнительно добавлены проверки:

- `ConfigHandler` не содержит прямого упоминания `ProfilesViewModel`;
- событие `ProfileRevealRequested` объявлено как `EventChannel<string>`;
- `ProfilesViewModel` подписан на событие;
- batch-импорт публикует ID до восстановления текущего активного профиля.

## Кандидат

```text
SG-CLIENT-099K-STEP-WIZARD-CMD-FIX14-BUILD1.zip
SHA-256: e1f0f6ca733913e03b45ae4013134e55622d3d78eeadaadb6bfcc189fee3f93f
Размер: 104 410 008 байт
```

Архив содержит 893 файла и 892 записи внутреннего SHA-256 манифеста. CRC, пути и манифест проверены. Полная WPF-компиляция всё равно должна быть повторно подтверждена на Windows.
