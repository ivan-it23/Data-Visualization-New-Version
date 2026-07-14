# AMK Data Visualization — handoff для новой сессии

Снимок состояния: **2026-07-14**.

## 1. Контекст и рабочие папки

- целевой проект, в котором нужно продолжать работу:
  `C:\Users\Admin\Desktop\AMK-Data-Visualization`;
- старый проект — источник истины:
  `C:\Users\Admin\Desktop\Amk-Horizon-New-Chart`;
- C++-исходники старого приложения:
  `C:\Users\Admin\Desktop\Amk-Horizon-New-Chart\Amk-Horizon-New-Chart`;
- документация старого приложения:
  `C:\Users\Admin\Desktop\Amk-Horizon-New-Chart\doc`.

Новый проект доступен для записи и является единственной целью изменений.
Старый проект нужно читать и сравнивать, но не менять без отдельного запроса.

## 2. Первые действия в новой сессии

1. Открыть workspace `C:\Users\Admin\Desktop\AMK-Data-Visualization`.
2. Прочитать этот файл полностью.
3. Прочитать `SESSION_TASKS.md`, затем нужный этап `MIGRATION_PLAN.md`.
4. Выполнить `git status --short`, чтобы не затереть незакоммиченные изменения.
5. Сопоставить новый код с конкретным C++-участком старого проекта.
6. После изменения собрать всё решение.
7. Не называть build функциональной или parity-проверкой.

## 3. Технологический профиль

- WinUI 3;
- `.NET 10`;
- `x64`;
- unpackaged приложение;
- Windows App SDK self-contained;
- главный проект: `AMK-Data-Visualization/AMK-Data-Visualization.csproj`;
- решение: `AMK-Data-Visualization.slnx`;
- автоматизированных тестовых проектов нет по просьбе владельца.

Версия приложения в `.csproj` пока `0.1.0`; installer ещё не создан.

## 4. Архитектура нового проекта

| Слой | Ключевые файлы | Фактическое состояние |
| --- | --- | --- |
| App | `MainWindow.*`, `Views/*.xaml`, `Controls/LineChartControl.*` | shell, два графика, схема и подключение |
| App services | `Services/ChartWorkspace.cs` | источники и конфигурации Graph 1/Graph 2 |
| Core | `Binary/BinaryStructs.cs`, `Adapters/ToolDataAdapterRegistry.cs`, `Access/ToolDataAccessView.cs` | binary contracts и semantic access |
| IO | `DataFiles/ToolDataFileReader.cs`, `ToolDataRepository.cs`, `ToolDataRangeExporter.cs` | файлы, repository, range export |
| Charting | `Series/ChartDataCalculator.cs`, `Configuration/*.cs` | series, конфигурации, presets |
| Hardware | `Serial/SerialDeviceService.cs`, `SerialContracts.cs`, `SerialConnectionSettingsStore.cs` | COM/polling/recording |
| Metrology | `MetrologySession.cs`, `MetrologyCalculator.cs` | файлы, состояние, 3/4/5Tx phase/rho FromComputer |
| Reporting | только `.csproj` | прикладной код отсутствует |

## 5. Стабильные контракты данных

### Binary layout

- `GpToolData`: 240 байт;
- `MetrologyData`: 240 байт;
- `.bin/.biv`: physical frame 240 байт, signature offset 0;
- `.DEV`: physical frame 251 байт, payload offset 11;
- materialize выполняется через `ToolDataAdapterRegistry`;
- UI и IO не должны вручную вычислять индексы полей canonical структуры.

### Repository

`ToolDataRepository` владеет canonical records и metadata, поддерживает:

- `LoadDataFromFile`;
- `ApplyLoadedData`;
- `ClearData`;
- `AddData`, `AddDataBatch`, `AddParsedData`;
- `DataVersion`;
- `LoadedFileName`;
- range metadata summary.

Пустой `AddDataBatch` не должен повышать `DataVersion`.

### Range export

- `SaveDataRange` пишет canonical 240-byte records;
- `SaveFileExcludingRange` сохраняет исходную физическую форму кадров, когда
  формат однороден, включая `.DEV` header/frame shape.

## 6. Реализованные рабочие вертикали

### Открытие файла и график

```text
FileOpenPicker
  → ToolDataFileReader
  → ToolDataRepository
  → ChartConfiguration
  → ChartDataCalculator.CalculateChartDataFromConfiguration
  → LineChartControl
```

Поддерживаются `.bin`, `.biv`, `.DEV`, несколько curves, две Y-оси и X по
номеру кадра или температуре.

### Graph 1 / Graph 2

- Graph 1 всегда использует primary repository.
- Graph 2 в `Стандартном режиме` читает primary repository.
- Graph 2 в режиме `Отдельный файл` читает
  `Graph2IndependentRepository`.
- Конфигурации графиков независимы.
- Переход между режимами не очищает Graph 1 и не уничтожает ранее загруженный
  независимый источник Graph 2.
- Все обращения к источнику Graph 2 идут через `ChartWorkspace`.

### COM live

```text
SerialDeviceService
  → packet length / CRC16 validation
  → ParsedToolFrame
  → primary ToolDataRepository
  → Graph 1 и linked Graph 2
```

Параллельно сервис может создавать canonical `.bin` или дописывать выбранный
существующий файл.

## 7. Текущее окно «Схема прибора»

Ключевые файлы:

- `AMK-Data-Visualization/Views/SchematicWindow.xaml`;
- `AMK-Data-Visualization/Views/SchematicWindow.xaml.cs`;
- `src/AMK.DataVisualization.Charting/Configuration/SchematicConfigurationEditor.cs`;
- `src/AMK.DataVisualization.Charting/Configuration/CurveSelectionService.cs`;
- `src/AMK.DataVisualization.Charting/Configuration/SchematicSelection.cs`.

Поведение:

- окно немодальное;
- одна window instance хранит отдельные editor-сессии Graph 1/Graph 2;
- закрытие и `Отмена` скрывают окно;
- `Применить` применяет конфигурацию к выбранному графику и не закрывает окно;
- повторное открытие восстанавливает окно, включая состояние после сворачивания;
- окно имеет `WS_EX_APPWINDOW`, собственную кнопку панели задач и иконки.

Схема:

- порядок элементов: T5, T3, T1, R1, R2, T2, T4, Молч. R1, Молч. R2;
- T1..T5 доступны с учётом capabilities текущего repository;
- выбранный элемент получает только жёлтую рамку;
- подписи элементов полужирные;
- частоты: 400 и 2000 кГц;
- X-axis: номер кадра или температура.

Быстрые пресеты:

- амплитуды R1;
- амплитуды R2;
- отношения амплитуд;
- разности фаз;
- УЭС;
- симметричные фазы;
- амплитуды молчащего режима;
- разность фаз молчащего режима;
- температура.

Последняя подтверждённая владельцем компоновка:

- заголовок «Схема прибора» центрирован;
- левый и правый рабочие блоки имеют равную ширину;
- частота выровнена слева;
- правая граница X-axis совпадает с правой границей `Амплитуды R2`;
- `Применить` и `Отмена` находятся в двух равных колонках и не пересекаются при
  текущем DPI;
- `Температура` растянута от левой границы `Ампл. молч.` до правой границы
  `Разн. фаз молч.`;
- нижнее пустое пространство удалено;
- текст ошибки конфигурации не выводится в окне, но `Применить` остаётся
  недоступной для неверного выбора.

## 8. Renderer и легенда

Ключевой файл: `AMK-Data-Visualization/Controls/LineChartControl.xaml.cs`.

Реализовано:

- Canvas-rendering нескольких series;
- single/dual Y-axis;
- полная горизонтальная и вертикальная сетка;
- подписи активных X/Y осей;
- legacy nice-step `1/2/5/10`;
- frame X-axis использует только целые значения;
- ноль выводится как `0`;
- числовые подписи не используют экспоненциальный формат;
- X по температуре;
- разрывы series по `NaN`;
- перенос легенды по строкам без слипания;
- частота находится в заголовке графика;
- быстрый preset выводит тип один раз и короткие series labels;
- комбинированная легенда группирует общий тип данных по оси;
- левую и правую группы разделяет `|`.

Не реализовано:

- zoom/scroll;
- crosshair и заполнение cursor-полей;
- LOD/downsampling;
- hit-test и notes geometry;
- общая geometry-модель для draw и screen-to-data.

## 9. Текущее окно подключения

Ключевые файлы:

- `AMK-Data-Visualization/Views/ConnectionSettingsWindow.xaml`;
- `AMK-Data-Visualization/Views/ConnectionSettingsWindow.xaml.cs`;
- `src/AMK.DataVisualization.Hardware/Serial/SerialDeviceService.cs`.

UI:

- заголовок `Подключение к прибору`;
- COM-порт + `Обновить`;
- блок `Статус`: состояние, кадр, пакеты;
- блок `Запись в файл`: запись, дозапись, весь файл, имя файла, число кадров,
  `Папка...`/`Файл...`, `Открыть`;
- блок `Таймер`: интервал polling;
- нижние кнопки `Подключить`, `Отключить`, `Закрыть`;
- размер 600×680;
- пункт верхнего меню открывает окно одним нажатием без flyout.

Settings path:

`%LOCALAPPDATA%\AMK-Data-Visualization\connection-settings.json`

Протокол:

- 125000 baud, 8N1, no handshake, DTR/RTS enabled;
- packet `[CMD:1][LEN:2][PAYLOAD][CRC16:2]`;
- CRC16 с initial `0xFFFF` и polynomial `0xA001`;
- `0x11`: canonical frame;
- `0x13`: metrology;
- `0x16`: signature;
- polling interval 500–30000 мс;
- автоматическое переподключение после ошибок.

## 10. Важные ограничения

### Metrology / FromComputer

`SmtComputationMode.FromComputer` подключён к `ChartDataCalculator` через
`MetrologyCalculator`. Используются AIR-поправка, condition-byte и точные
матрицы 3/4/5Tx. Режим доступен в окне схемы только после чтения метрологии;
signature кадра должна совпасть с метрологией, иначе действует legacy fallback
к canonical `FromSonde`.

### Визуальные заглушки shell

- range apply/reset/load/cut и zoom-кнопки отключены;
- cursor-поля read-only, но crosshair не реализован;
- `Поток` не выполняет полный Surf-сценарий;
- notes controls не имеют storage/rendering логики;
- меню конструктора, логов, обновления и документации не перенесены.

### Пустые проекты

- `AMK.DataVisualization.Reporting` — только `.csproj`.

## 11. Что подтверждено, а что нет

Подтверждено:

- решение собирается в `Debug x64` с `0 warnings / 0 errors`;
- основное окно и немодальные окна ранее запускались;
- владелец вручную подтвердил актуальную компоновку окна схемы и окна
  подключения;
- UI Automation ранее подтверждала независимость editor-состояний Graph 1/Graph 2.

Не считать подтверждённым без новой ручной фиксации:

- полный паритет всех series на реальных `.bin/.biv/.DEV`;
- linked/independent Graph 2 с двумя контрольными файлами;
- range export/cut на всех форматах;
- COM-протокол, reconnect и запись на физическом приборе;
- parity результатов `FromComputer` со старым приложением на реальных данных;
- производительность на больших файлах.

## 12. Рекомендуемый следующий блок

Сначала выполнить ручную parity-проверку `from_sonde`/`from_computer` на
одинаковых реальных данных и проверить запись/air calibration на физическом
приборе. Затем рекомендуемый порядок: `DeviceExchangeLog` → интерактивная
geometry графика → notes/sidecar → file constructor → reporting и installer.

## 13. Основные C++-якоря

Пути ниже считаются относительно корня старого проекта.

### Binary и файлы

- `Amk-Horizon-New-Chart/ToolDataTypes.h` — `GP_TOOL_DATA`;
- `Amk-Horizon-New-Chart/ToolDataAdapters.*` — adapters и registry;
- `Amk-Horizon-New-Chart/ToolDataAccess.h` — semantic access;
- `Amk-Horizon-New-Chart/DataModule.cpp` — file load, repository-like state,
  series calculations и range export.

### Схема и конфигурации

- `Amk-Horizon-New-Chart/SchematicModule.h` — `CurveDefinition`,
  `ChartConfiguration`;
- `Amk-Horizon-New-Chart/SchematicModule.cpp` — окно и axis configuration;
- `Amk-Horizon-New-Chart/CurveSelectionModule.cpp:871` — preset entrypoint;
- `Amk-Horizon-New-Chart/CurveSelectionModule.cpp:919` — full curve set;
- `Amk-Horizon-New-Chart/CurveSelectionModule.cpp:1021` — validation;
- `Amk-Horizon-New-Chart/CurveSelectionModule.cpp:1618` — silent amplitude;
- `Amk-Horizon-New-Chart/CurveSelectionModule.cpp:1631` — silent phase;
- `Amk-Horizon-New-Chart/CurveSelectionModule.cpp:1786` и далее — быстрые presets.

### Графики

- `Amk-Horizon-New-Chart/ChartManager.cpp:4077` — применение конфигурации;
- `Amk-Horizon-New-Chart/ChartModule.cpp:1420` — dual-axis draw;
- `Amk-Horizon-New-Chart/ChartModule.cpp:2188` — dual-axis legend;
- `Amk-Horizon-New-Chart/ChartModule.cpp:3002` — temperature-X dual-axis;
- `Amk-Horizon-New-Chart/ChartModule.cpp:3133` — temperature grid;
- `Amk-Horizon-New-Chart/ChartModule.cpp:3386` — temperature drawing.

### COM и метрология

- `Amk-Horizon-New-Chart/SerialModule.*` — команды, packet framing, polling;
- `Amk-Horizon-New-Chart/MetrologyModule.*` — данные и вычисления;
- `doc/COM_5TX_GP_POLLING_AND_METROLOGY.md` — сценарий COM;
- `doc/ues_phase_from_computer_5tx.md` — phase/rho from computer.

Перед использованием line anchors нужно перепроверить их в текущем checkout
старого проекта: файлы могли измениться после первоначального анализа.

## 14. Проверка

Сборка из корня нового проекта:

```powershell
dotnet build .\AMK-Data-Visualization.slnx -c Debug -p:Platform=x64 --no-restore
```

`git diff --check` используется для проверки whitespace/conflict markers.

Если владелец просит только внедрить код и оставляет runtime-проверку себе, не
запускать приложение автоматически. В итоговом сообщении явно указать, что
подтверждена только сборка.

## 15. Правило обновления документов

- этот handoff обновлять при изменении архитектурных контрактов, реализованных
  вертикалей и точки продолжения;
- `MIGRATION_PLAN.md` обновлять при смене статуса этапов;
- `SESSION_TASKS.md` обновлять после каждой завершённой пользовательской задачи;
- `README.md` держать кратким входом и не превращать в журнал истории.
