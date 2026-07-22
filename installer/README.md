# Установщик «Визуализация данных»

Актуальное состояние: **22 июля 2026 года**.

Приложение распространяется как unpackaged WinUI 3, self-contained
`Release x64`. Установщик создаётся Inno Setup 6 и предназначен для всех
пользователей компьютера.

## Источник версии

Единственный источник версии:

```text
AMK-Data-Visualization\AMK-Data-Visualization.csproj -> Version
```

`scripts\Build-Installer.ps1` передаёт это значение Inno Setup и создаёт:

```text
installer\Output\DataVisualization-Setup-<version>.exe
installer\Output\data-visualization-version.txt
```

Не следует вручную дублировать номер версии в release-инструкциях или feed.

## Сборка

Из корня репозитория:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1
```

Скрипт:

1. очищает изолированные `artifacts\build\win-x64` и
   `artifacts\publish\win-x64`;
2. выполняет self-contained `dotnet publish` для `Release|x64`;
3. переносит созданные WinUI `DataVisualization.pri` и `*.xbf` из build-output
   в publish-payload;
4. проверяет EXE, runtimeconfig, PRI, XBF окон (включая `FileConstructorWindow.xbf`),
   `Microsoft.UI.Xaml` и Windows App Runtime bootstrapper;
5. компилирует setup;
6. вычисляет SHA-256 и записывает descriptor обновления.

При отсутствии любого обязательного файла сборка прерывается до создания
установщика. Параметр `-SkipPublish` допустим только для повторной компиляции уже
проверенного publish-каталога.

## Поведение установки

- требуется подтверждение UAC;
- каталог по умолчанию — `Program Files\Визуализация данных`;
- приложение регистрируется в «Установленных приложениях»;
- пользователь отдельно выбирает общие ярлыки меню «Пуск» и рабочего стола;
- стабильный `AppId` позволяет устанавливать новую версию поверх существующей;
- после обычной установки мастер предлагает запустить приложение;
- удаление выполняется стандартным Inno Setup uninstaller.

Запуск версии `0.1.1` после установки и через созданные ярлыки подтверждён
владельцем. Полный цикл удаления и обновления между двумя опубликованными
версиями остаётся отдельной release-проверкой.

## Feed обновлений

Каталог обновлений:

```text
http://ivanbohg.beget.tech/FilesForProgramVisualisation/
```

В него публикуются setup и одноимённый descriptor:

```text
<version>
DataVisualization-Setup-<version>.exe
<SHA-256>
```

Первые две строки обязательны. Третья строка рекомендуется и проверяется до
запуска setup. Имя `data-visualization-version.txt` намеренно отличается от
legacy `version.txt`.

При старте приложение через три секунды выполняет тихую проверку. Ручная
проверка доступна из главного окна. Если версия новее, пользователь подтверждает
загрузку; файл скачивается во временный каталог, проверяется и запускается с
UAC. Режим `/UPDATE=1` перезапускает приложение после установки.

## Release checklist

- синхронизировать версию через `.csproj`;
- собрать установщик штатным скриптом;
- убедиться, что descriptor указывает на фактическое имя setup и содержит его
  SHA-256;
- подписать `DataVisualization.exe` и setup code-signing сертификатом;
- проверить установку поверх предыдущей версии, оба ярлыка, запуск, удаление и
  автообновление;
- публиковать setup и descriptor только после ручной проверки.

Без цифровой подписи Windows SmartScreen может показывать предупреждение даже
при корректном HTTPS и SHA-256.
