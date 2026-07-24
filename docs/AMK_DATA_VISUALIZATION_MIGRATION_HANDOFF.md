# AMK Data Visualization — handoff для новой сессии

Последняя актуализация: **23 июля 2026 года**.

## 1. Контекст

Целевой проект:

```text
C:\Users\Admin\Desktop\AMK-Data-Visualization
```

Legacy-источник поведения:

```text
C:\Users\Admin\Desktop\Amk-Horizon-New-Chart
```

Пользовательское имя приложения — **«Визуализация данных»**. Текущая версия —
`0.1.3`, executable — `DataVisualization.exe`.

Важно: setup `0.1.3` и descriptor собраны 23 июля 2026 года из текущего дерева
исходников; SHA-256 совпадает. Они ещё не опубликованы: перед выкладкой в
HTTP-каталог требуется ручная проверка установки владельцем и проверка
автообновления с `0.1.2`.

Цель миграции — сохранить бинарные форматы, расчётные контракты и сценарии
старого C++-приложения в модульной реализации WinUI 3 / .NET 10. Legacy остаётся
источником истины до ручного сравнения на одинаковых данных.

## 2. Первые действия в новой сессии

1. Выполнить `git status --short` и не удалять существующие изменения.
2. Прочитать `docs/SESSION_TASKS.md`.
3. Прочитать релевантные разделы этого файла.
4. Открыть текущие исходники затронутой вертикали.
5. Перед переносом поведения проверить соответствующий legacy-модуль.
6. Раздельно фиксировать implemented, build-, runtime-, parity- и
   release-verified состояния.

## 3. Технологический профиль

- WinUI 3;
- `net10.0-windows10.0.26100.0`;
- minimum Windows `10.0.17763.0`;
- только x64;
- unpackaged;
- Windows App SDK self-contained;
- `Microsoft.WindowsAppSDK 2.2.0`;
- `CommunityToolkit.Mvvm 8.4.2`;
- автоматизированных тестовых проектов нет.

Авторитетный источник версии:

```text
AMK-Data-Visualization/AMK-Data-Visualization.csproj -> Version
```

## 4. Архитектура

| Слой | Основные файлы | Ответственность |
| --- | --- | --- |
| App | `MainWindow.*`, `Views/*`, `Controls/LineChartControl.*` | UI и orchestration |
| App services | `ChartWorkspace.cs`, `ChartViewportState.cs`, `AutoUpdateService.cs`, `SecondaryWindowIntegration.cs` | состояние графиков, update flow и общая Win32-интеграция окон |
| Core | `Binary/BinaryStructs.cs`, `DomainModels.cs`, `Adapters/*`, `Access/*` | binary/domain contracts |
| IO | `DataFiles/ToolDataFileReader.cs`, `ToolDataRepository.cs`, `ToolDataRangeExporter.cs` | файлы и repository |
| Charting | `Configuration/*`, `Series/*` | конфигурации и series |
| Hardware | `Serial/SerialDeviceService.cs`, transports, settings и log | COM, polling, recording |
| Metrology | `MetrologySession.cs`, `MetrologyCalculator.cs` | метрология и `FromComputer` |
| Reporting | только `.csproj` | будущая реализация |

UI не должен самостоятельно разбирать canonical layout или дублировать
формулы Core/Charting/Metrology.

## 5. Стабильные контракты данных

### Binary layout

- `GpToolData` — 240 байт;
- `MetrologyData` — 240 байт;
- `.bin/.biv` — physical frame 240 байт;
- `.DEV` — physical frame 251 байт, canonical payload начинается со смещения
  11;
- проверка размеров выполняется `BinaryLayoutVerifier`;
- repository хранит canonical records и отдельную metadata каждого кадра.

Ключевые файлы:

- `src/AMK.DataVisualization.Core/Binary/BinaryStructs.cs`;
- `src/AMK.DataVisualization.Core/DomainModels.cs`;
- `src/AMK.DataVisualization.Core/Adapters/ToolDataAdapterRegistry.cs`;
- `src/AMK.DataVisualization.Core/Access/ToolDataAccessView.cs`.

### Картограф LWD 4tx

Ветка определяется по двум признакам signature:

- тип прибора — `Картограф`;
- число передатчиков — 4.

Модификация не участвует в выборе ветки. T5 недоступен. Обычный
`Картограф 5tx` имеет другой контракт и этой canonical-веткой не принимается.

Амплитуды R1/R2 и молчащих каналов передаются в Q12. Semantic access-view
применяет `AmplitudeScale = 1 / 4096`, приводя их к общему LWD-контракту `mV`.
Фазы, УЭС, температура и исходные 240 байт не масштабируются.

### Repository

`ToolDataRepository` владеет:

- records и metadata;
- именем/путём источника;
- `DataVersion`;
- добавлением live-кадров;
- диапазонным анализом и export.

Graph 1 использует primary repository. Graph 2 в стандартном режиме читает тот
же repository, а в режиме отдельного файла — собственный. Смена режима Graph 2
не должна очищать Graph 1 или удалять независимый источник Graph 2.

## 6. Файловая вертикаль

Путь:

`FileOpenPicker` → `ToolDataFileReader` → `ToolDataRepository` →
`ChartConfiguration` → `ChartDataCalculator` → `LineChartControl`.

Реализовано:

- detect/validate/load `.bin`, `.biv`, `.DEV`;
- canonical materialization и metadata;
- отображение имени файла и числа кадров;
- сохранение выбранного диапазона Graph 1 в canonical `.bin`;
- вырезание диапазона из текущего файла;
- сохранение копии без диапазона в физическом формате источника;
- append выбранного диапазона или файла без вырезаемого диапазона в совместимый canonical `.bin`;
- конструктор файлов с порядком сегментов, открытием/пересохранением сборки и проверкой сигнатур;
- segment-layout в `%LOCALAPPDATA%\AMK-Data-Visualization\segments`, его обновление при cut и
  пунктирные границы сегментов на кадровой/температурной оси.

Не реализовано:

- ручная parity-проверка конструктора и append-сценариев владельцем проекта.

## 7. Графики и viewport

Ключевые файлы:

- `AMK-Data-Visualization/MainWindow.xaml.cs`;
- `AMK-Data-Visualization/Services/ChartWorkspace.cs`;
- `AMK-Data-Visualization/Services/ChartViewportState.cs`;
- `AMK-Data-Visualization/Controls/LineChartControl.xaml.cs`;
- `AMK-Data-Visualization/Views/NotesJournalWindow.xaml.cs`;
- `src/AMK.DataVisualization.Charting/Series/ChartDataCalculator.cs`;
- `src/AMK.DataVisualization.Charting/Notes/NotesModule.cs`.

Реализовано:

- независимые конфигурации и viewport Graph 1/Graph 2;
- X по номеру кадра или температуре;
- одна/две Y-оси;
- aligned multi-series и `NaN`-разрывы;
- legacy nice-step, сетка, формат подписей и легенды;
- X Range из полей; пустое `To` означает последнюю запись;
- drag-zoom по X/Y;
- отдельная история zoom и пошаговый undo;
- reset range и reset zoom;
- X-scroll увеличенного или заданного диапазона по всему файлу;
- Y-scroll ручного Y-окна;
- при X-scroll ручной Y-range сбрасывается и обе оси пересчитываются по новому
  видимому участку;
- автоматический Y-padding 5%;
- `Поток` показывает последние N записей, следует за live-данными, блокирует
  ручное изменение X, но сохраняет Y-only zoom/scroll;
- WinUI ScrollBar принудительно удерживается в desktop-состоянии
  `MouseIndicator`;
- клик по графику устанавливает crosshair и заполняет cursor-поля, кнопка `⌖`
  сбрасывает оба состояния;
- контекстное меню crosshair переносит его X в начальную или конечную границу
  диапазона и открывает создание заметки;
- заметки совместимы с legacy append-only `.notes.jsonl`, фильтруются по
  конфигурации кривых, поддерживают маркеры, hit-test, редактирование, удаление
  и журнал с восстановлением конфигурации;
- кнопка «Заметки» открывает журнал напрямую, без промежуточного flyout;
- системная кнопка папки в статусной строке открывает каталог файла Graph 1 или
  COM-записи; при двух независимых файлах показывает выбор Graph 1/Graph 2.

Zoom, прокрутка и автомасштабирование подтверждены владельцем.

Не реализовано:

- LOD/downsampling и cache;
- отдельно выделенная общая geometry-модель для draw/hit-test.

## 8. Окно «Схема прибора»

Ключевые файлы:

- `AMK-Data-Visualization/Views/SchematicWindow.xaml`;
- `AMK-Data-Visualization/Views/SchematicWindow.xaml.cs`;
- `src/AMK.DataVisualization.Charting/Configuration/SchematicConfigurationEditor.cs`;
- `CurveSelectionService.cs` и `SchematicSelection.cs`.

Контракт:

- немодальное окно для Graph 1 и Graph 2;
- повторное открытие использует существующий экземпляр;
- отдельное editor-состояние каждого графика;
- capability-aware T1–T5, R1/R2 и молчащие каналы;
- частоты 400/2000 кГц;
- X по кадрам или температуре;
- комбинированный выбор и блокировка несовместимой конфигурации;
- быстрые пресеты амплитуд R1/R2, отношения амплитуд, разности фаз,
  температуры, УЭС, симметричных фаз и молчащих каналов;
- `FromComputer` для УЭС/фаз доступен только после чтения метрологии;
- окно имеет отдельную taskbar-кнопку и иконку приложения.

### Общая логика вспомогательных окон

`SecondaryWindowIntegration` используется окнами подключения, конструктора
файлов, журнала заметок, логов и метрологии. Он:

- сохраняет owner-связь с главным окном;
- добавляет `WS_EX_APPWINDOW` и убирает `WS_EX_TOOLWINDOW`, поэтому окно видно в
  панели задач;
- наследует большую и малую иконки главного окна;
- восстанавливает свёрнутое окно;
- центрирует окно по фактическим Win32-размерам владельца и дочернего окна,
  включая повторный проход после активации/масштабирования.

## 9. COM, live и запись

Ключевые файлы:

- `AMK-Data-Visualization/Views/ConnectionSettingsWindow.*`;
- `src/AMK.DataVisualization.Hardware/Serial/SerialDeviceService.cs`;
- `LegacyToolSerialTransport.cs`;
- `SerialConnectionSettingsStore.cs`;
- `SerialRecordingFolder.cs`;
- `DeviceProtocolLog.cs`;
- `DeviceExchangeLog.cs`, `DeviceLogPayloadFormatter.cs`;
- `AMK-Data-Visualization/Views/DeviceLogWindow.*`.

Общий transport:

- 125000 baud, 8N1, no handshake;
- DTR/RTS включены;
- packet `[CMD:1][LEN:2][PAYLOAD][CRC16:2]`;
- CRC16 initial `0xFFFF`, polynomial `0xA001`.

`Картограф LWD 4tx`:

- DTR/RTS выключены;
- packet `[CMD:1][PAYLOAD:N][CRC16:2]`;
- чтение данных использует запрос `{05 00}` и payload 242 байта;
- canonical `GpToolData` начинается после двух служебных байтов;
- нулевой CRC допускается только для data-frame этой ветки и компенсируется
  обязательной проверкой signature.

Команды:

- `0x11` — canonical frame;
- `0x13` — метрология и запись метрологии;
- `0x16` — signature;
- `0x17` — selected key для подтверждённого length-framed контракта;
- `0x1A` — AIR calibration для подтверждённого length-framed контракта.

Для legacy transport команды без source-backed контракта не отправляются
вслепую и возвращают `InvalidResponse`.

Recording:

- новый canonical `.bin` или дозапись существующего;
- default `Desktop\AMK_Data` создаётся автоматически;
- явно выбранный путь сохраняется в
  `%LOCALAPPDATA%\AMK-Data-Visualization\connection-settings.json`;
- старое сохранённое значение корня рабочего стола мигрирует в `AMK_Data` один
  раз и не переопределяет последующий выбор пользователя.

Кнопка с системной иконкой папки в статусной строке открывает текущий каталог
записи. Если фактический `RecordFilePath` ещё не создан, используется папка или
файл дозаписи из сохранённых настроек подключения.

Порт и интервал опроса:

- `LastPort` — единственный авторитетный источник порта, сохраняется в
  `connection-settings.json`. `CaptureSettingsFromControls` перезаписывает его
  только при наличии реально выбранного порта, поэтому гонка при закрытии окна
  до завершения `RefreshPortsAsync` больше не затирает его пустым значением;
- `RefreshPortsAsync` при недоступном сохранённом порте добавляет его в список и
  оставляет выбранным, а не переключается на первый доступный; смена порта —
  только явное действие пользователя;
- при нажатии «Включить» на главном окне, если `LastPort` отсутствует среди
  доступных портов, выполняется не тихое переключение, а поясняющее сообщение и
  открытие окна подключения;
- `PollIntervalMilliseconds` помечен `[JsonIgnore]` и не персистится: каждая
  сессия стартует с 2000 мс, минимум 700 мс (`MinimumPollInterval`), значение
  сессии хранит `SerialDeviceService`, а не файл настроек.

Базовый live-приём и запись фактической сигнатуры `0x00055349` подтверждены.
Полный reconnect и метрологический сценарий на приборе остаются ручной
проверкой.

### Диагностический лог

`DeviceProtocolLog` пишет TX/RX, CRC, размеры, таймауты, signature, transport и
причины отклонения кадров только в диагностический файл. Пользовательский
raw-режим повторяет legacy-состав: `TX сырые`, краткую строку обмена и
расшифровку `GP_TOOL_DATA`; внутренняя транспортная трассировка в окно не
попадает. Окно также поддерживает очистку памяти, паузу записи, копирование,
открытие папки журналов и полужирные блоки идентификации.

В памяти остаются последние 300 элементов, которые отображаются единым потоком
без разделителей сессий. В `%LOCALAPPDATA%\AMK-Data-Visualization\Logs` хранятся
не более трёх UTF-8 файлов сессий по 32 МБ каждый: новое подключение прибора и
переход к работе с файлами данных начинают отдельную сессию, а при создании
четвёртой удаляется самая старая. Запись выполняется фоновой ограниченной
очередью. UI обновляется только при открытом окне с интервалом 300/500 мс,
следует за последней записью только при нижнем положении полосы прокрутки и
сохраняет видимый диапазон, когда пользователь просматривает историю.

## 10. Метрология и FromComputer

Ключевые файлы:

- `AMK-Data-Visualization/Views/MetrologyWindow.*`;
- `src/AMK.DataVisualization.Metrology/MetrologySession.cs`;
- `src/AMK.DataVisualization.Metrology/MetrologyCalculator.cs`;
- `src/AMK.DataVisualization.Charting/Series/ChartDataCalculator.cs`.

Реализовано:

- ровно 240 байт на метрологический файл;
- чтение из зонда/файла и сохранение файла;
- запись в зонд с повторным чтением для верификации;
- signature editor: тип, 3/4/5 Tx, модификация 1–9 и положение R1;
- L1/L2/F1/F2 read-only по умолчанию; флажок разрешает редактирование;
- AIR-поправка, condition-byte, матрицы 3/4/5Tx;
- `SIGNAL`, обратный расчёт УЭС и симметризованные фазы;
- блокировка `FromComputer` до успешного чтения метрологии;
- fallback к canonical `FromSonde` при несовпадении signature.

Компоновка окна и переключение read-only подтверждены владельцем. Численный
parity `FromComputer` на одинаковых реальных данных ещё не выполнен.

## 11. Поставка, брендинг и обновление

Ключевые файлы:

- `AMK-Data-Visualization/AMK-Data-Visualization.csproj`;
- `AMK-Data-Visualization/Services/AutoUpdateService.cs`;
- `scripts/Generate-AppIcon.ps1`;
- `scripts/Export-SystemFolderIcon.ps1`;
- `scripts/Build-Installer.ps1`;
- `installer/setup.iss`;
- `installer/README.md`.

Контракт:

- продукт, окно, launch profile, EXE и installer используют имя
  «Визуализация данных»;
- иконка построена как светлая стальная плашка (`#E9F0F7`) со стальной рамкой,
  координатной сеткой, осями без стрелок и двумя ломаными — тёмно-синей
  `#1B4A73` и акцентной `#0087C4`; эмблема AMK сохранена без изменений в правом
  верхнем углу;
- сетка не отрисовывается на кадрах меньше 32 px, где она сливается в заливку;
- `SystemFolder.png` формируется из системного `SIID_FOLDER` и используется
  кнопками открытия файла Graph 2 и каталога в статусной строке;
- self-contained `Release x64` устанавливается для всех пользователей;
- общие ярлыки меню «Пуск» и рабочего стола выбираются независимо;
- стабильный Inno `AppId` поддерживает обновление поверх установки;
- build-скрипт переносит WinUI PRI/XBF и проверяет полный payload;
- отдельный descriptor нового приложения расположен в рабочем legacy
  HTTP-каталоге:
  `FilesForProgramVisualisation/data-visualization-version.txt`;
- descriptor содержит version, имя setup и рекомендуемый SHA-256;
- фоновая проверка выполняется через три секунды; ручная — по кнопке;
- скачанный setup проверяется по имени, размеру, MZ-сигнатуре и SHA-256;
- `/UPDATE=1` перезапускает приложение после обновления.
- сообщения ручной проверки обновлений используют центрированную кнопку;
  сообщение об актуальной версии компактное и центрированное.

Setup `0.1.3` и descriptor сформированы 23 июля 2026 года из текущего дерева;
имя файла и SHA-256 совпали, все обязательные PRI/XBF проверены скриптом. Во
время упаковки приложение и установщик не запускались. Setup ещё не
опубликован в HTTP-каталоге. Code signing и полный цикл update/uninstall
между двумя версиями не подтверждены.

## 12. Реализовано частично или не начато

- Graph 1 range save/cut, append и segment-layout реализованы; ручная parity-проверка остаётся;
- меню документации — заглушка;
- Reporting, печать и экспорт не начаты.

## 13. Матрица проверки

Подтверждено:

- компиляция Debug x64 и self-contained Release x64;
- создание полного installer payload с PRI/XBF;
- запуск основного окна и окон схемы, подключения и метрологии;
- layout ключевых окон;
- zoom/scroll/автомасштабирование;
- заметки, прямое открытие журнала, кнопки папок и поведение вспомогательных
  окон подтверждены владельцем;
- базовый live-приём/запись `Картограф LWD 4tx`;
- создание setup и descriptor версии `0.1.2` с совпадающим SHA-256.

Не считать подтверждённым:

- полный parity всех series на контрольных `.bin/.biv/.DEV`;
- linked/independent Graph 2 на двух контрольных файлах как полный regression;
- range export/cut на каждом физическом формате;
- полный reconnect и все метрологические команды на физическом приборе;
- `FromComputer` parity со старым приложением;
- LOD/performance на больших файлах;
- подписанный end-to-end update/uninstall.

## 14. Legacy-якоря

Пути относительно `C:\Users\Admin\Desktop\Amk-Horizon-New-Chart`:

- `Amk-Horizon-New-Chart/ToolDataTypes.*` — binary structures;
- `ToolDataAdapters.*`, `ToolDataAccess.h` — adapter/access contracts;
- `DataModule.*` — files, canonical state, calculations, range operations;
- `ChartManager.*`, `ChartModule.*` — graph behavior and rendering;
- `CurveSelectionModule.*`, `SchematicModule.*` — selection and presets;
- `SerialModule.*` — protocol and polling;
- `MetrologyModule.*` — metrology and formulas;
- `DeviceExchangeLog.*`, `DeviceLogPanel.*` — diagnostics UI;
- `NotesModule.*` — notes/sidecar;
- `FileConstructorModule.*` — file constructor;
- `AutoUpdateModule.*`, `installer/setup.iss` — update/install behavior.

Перед переносом необходимо заново открыть соответствующие функции; старые line
anchors не следует считать неизменными.

## 15. Следующая рекомендуемая вертикаль

Если пользователь не назначил другую задачу:

1. parity `FromSonde` / `FromComputer` на одном реальном файле и метрологии;
2. полный COM regression `Картограф LWD 4tx`;
3. единая geometry-модель и LOD/downsampling;
4. reporting;
5. новый номер версии, code signing и release regression.

## 16. Проверка и обновление документов

Команда сборки:

```powershell
dotnet build .\AMK-Data-Visualization.slnx -c Debug -p:Platform=x64 --no-restore
```

После изменений выполнять `git diff --check`. Реальный запуск не заменяется
build-успехом. Если владелец просит не запускать приложение, это явно отмечается
в результате.

Документы обновляются так:

- этот handoff — при изменении контрактов, архитектуры и точки продолжения;
- `MIGRATION_PLAN.md` — при изменении статуса этапов;
- `SESSION_TASKS.md` — после каждой завершённой пользовательской задачи;
- `README.md` — краткое текущее состояние без истории сессий;
- `installer/README.md` — при изменении packaging/update flow.
