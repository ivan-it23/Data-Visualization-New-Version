# AMK Data Visualization - план миграции логики

Дата создания: 2026-07-06
Исходный проект: `C:\Users\Admin\Desktop\Amk-Horizon-New-Chart`
Целевой проект: `C:\Users\Admin\Desktop\AMK-Data-Visualization`

## 1. Цель миграции

Новый проект должен воспроизвести всю пользовательскую и расчетную логику `Amk-Horizon-New-Chart`, но перенести ее из Win32 C++ в современную модульную архитектуру WinUI 3 / .NET 10. Первая цель - поведенческий паритет. Визуальное обновление и улучшение UX выполняются только после фиксации контрактов данных, расчетов и сценариев.

## 2. Непереговорные правила

- Старое приложение остается источником истины по поведению до тех пор, пока новая реализация не пройдет parity-проверку.
- Бинарные форматы, индексация каналов, единицы измерения и расчетные формулы переносятся без смысловых изменений.
- Любое изменение поведения должно быть явно описано как новая бизнес-логика, а не как часть технического переноса.
- Сначала переносятся модели, форматы, расчеты и тестовые фикстуры; затем строится UI.
- Сборка не считается полной проверкой: для UI-изменений нужно отдельно подтверждать запуск окна и основные сценарии.

## 3. Текущие источники истины старого проекта

Основные документы:

- `doc/README.md` - обзор возможностей и текущая архитектура.
- `doc/TECHNICAL_REFERENCE.md` - инварианты графиков, UI, координат, расчетов и модулей.
- `doc/ANALYSIS_GP_TOOL_DATA.md` - главная спецификация `GP_TOOL_DATA`, `METROLOGY_DATA`, capabilities и adapter-driven parsing.
- `doc/FILE_FORMATS_ANALYSIS.md` - фактическое чтение `.bin`, `.biv`, `.DEV`.
- `doc/FILE_VALIDATION_FOR_5TX_MODEL.md` - правила валидации входных файлов.
- `doc/COM_5TX_GP_POLLING_AND_METROLOGY.md` - COM polling и метрология.
- `doc/ues_phase_from_computer_5tx.md` - расчет phase/rho from computer.
- `doc/PROJECT_STATUS.md` - актуальные границы стабильности и оставшиеся риски.

Основные C++-модули:

- `ToolDataTypes.h`, `ToolDataAdapters.h/.cpp`, `ToolDataAccess.h` - бинарные структуры, capabilities, adapter registry.
- `DataModule.h/.cpp` - загрузка файлов, canonical storage, derived cache, range/export.
- `SerialModule.h/.cpp` - COM-протокол, команды, запись потока, polling, чтение метрологии.
- `MetrologyModule.h/.cpp` - метрология, подписи, параметры прибора.
- `ChartManager.h/.cpp` - координация UI, режимы источника данных, graph 2, COM live/file view.
- `ChartModule.h/.cpp` - отрисовка, масштабирование, координаты, LOD, crosshair.
- `CurveSelectionModule.h/.cpp` - кривые, пресеты, цвета, phase/rho from sonde/computer.
- `SchematicModule.h/.cpp` - схема прибора и выбор каналов.
- `FileConstructorModule.h/.cpp` - сборка canonical `.bin` из сегментов.
- `NotesModule.h/.cpp` - заметки, sidecar, hit-test маркеров.
- `DeviceExchangeLog.h/.cpp`, `DeviceLogPanel.h/.cpp` - лог обмена с прибором.
- `AutoUpdateModule.h/.cpp`, `installer/setup.iss` - обновление и поставка.

## 4. Целевая архитектура

Текущая заготовка решения:

- `AMK.DataVisualization.App` - WinUI 3 shell, страницы, команды, привязки, навигация.
- `AMK.DataVisualization.Core` - бинарные структуры, доменные модели, capabilities, enums, общие контракты.
- `AMK.DataVisualization.IO` - чтение/валидация `.bin`, `.biv`, `.DEV`, sidecar-сегменты, экспорт диапазонов.
- `AMK.DataVisualization.Hardware` - COM-порты, протокол `[CMD][LEN][PAYLOAD][CRC16]`, polling, запись потока.
- `AMK.DataVisualization.Metrology` - расчетные формулы, phase/rho, метрология, подписи, проверки совместимости.
- `AMK.DataVisualization.Charting` - подготовка рядов, координаты, LOD, hit-test, модели графиков.
- `AMK.DataVisualization.Reporting` - отчеты, экспорт, печать, будущие документы.

Тестовые smoke-проекты добавляются рядом с переносимыми слоями: `AMK.DataVisualization.Core.Tests` проверяет бинарный контракт, `AMK.DataVisualization.IO.Tests` проверяет чтение контейнеров `.bin/.biv/.DEV`.

## 5. Этапы переноса

### Этап 0. Базовая конфигурация проекта

Статус: выполнен 2026-07-06.

Задачи:

- Перевести WinUI-проект на `net10.0-windows10.0.26100.0`.
- Использовать `x64` как основную платформу.
- Включить unpackaged/self-contained Windows App SDK, чтобы повторить удобную desktop-модель `LOOP_NEXT`.
- Зафиксировать `Microsoft.WindowsAppSDK`, `Microsoft.Windows.SDK.BuildTools`, `CommunityToolkit.Mvvm` и `System.IO.Ports` явно.
- Создать модульные проекты под будущие слои.
- Проверить сборку.

### Этап 1. Фиксация поведения старого приложения

Задачи:

- Собрать набор контрольных файлов `.bin`, `.biv`, `.DEV`.
- Зафиксировать ожидаемые свойства: signature, frame count, temperature range, tool capabilities, graph-ready series.
- Зафиксировать контрольные сценарии UI: один график, два графика, graph 2 linked/independent, COM live + просмотр файла, возврат `К прибору`.
- Создать таблицу parity-кейсов: вход, ожидаемый результат, источник истины, статус.

### Этап 2. Core: бинарные структуры и capabilities

Статус: начат 2026-07-06. Первая версия `GpToolData`, `MetrologyData`, capabilities, `UniversalCanonicalAdapter`, `ToolDataAdapterRegistry`, `ToolDataAccessView` и smoke tests перенесена.

Задачи:

- [x] Перенести `GP_TOOL_DATA` размером 240 байт.
- [x] Перенести `METROLOGY_DATA` размером 240 байт.
- [x] Реализовать accessor-методы для `[freqIdx][txIdx]`, чтобы не размазывать индексную арифметику по UI.
- [x] Перенести decode toolCode из signature: тип прибора, число передатчиков, модификация.
- [x] Перенести `ToolCapabilities`, `ToolRecordMetadata`, `ParsedToolFrame`.
- [x] Перенести `UniversalCanonicalAdapter` и registry.
- [x] Добавить проверки `sizeof`/`Marshal.SizeOf` и smoke tests.
- [ ] Добавить golden fixtures на реальных файлах после переноса `AMK.DataVisualization.IO`.

### Этап 3. IO: файлы и canonical storage

Статус: начат 2026-07-07. Первая версия file-validation/file-loading и stateful canonical storage и range export перенесены в `AMK.DataVisualization.IO`: `.bin/.biv/.DEV`, signature offset, physical frame size, adapter parsing, materialize в `GpToolData + ToolRecordMetadata`, `ToolDataRepository`, `dataVersion`, record/metadata access, range metadata summary, canonical save range и cut range.

Задачи:

- [x] Перенести валидацию расширений `.bin`, `.biv`, `.DEV`.
- [x] Реализовать чтение signature с правильного смещения: начало кадра для `.bin/.biv`, после 11 байт для `.DEV`.
- [x] Реализовать physical frame size: 240 для `.bin/.biv`, 251 для `.DEV`.
- [x] Реализовать materialize через `ToolDataAdapterRegistry` в canonical `GP_TOOL_DATA + ToolRecordMetadata`.
- [x] Добавить smoke tests для generated `.bin`, `.biv`, `.DEV`, unsupported extension и corrupted size.
- [x] Перенести stateful-часть `DataModule`: `ApplyLoadedData`, `ClearData`, `AddData`, `AddDataBatch`, `AddParsedData`, `dataVersion`, `LoadedFileName`, доступ к records/metadata.
- [x] Перенести `TryDescribeRangeMetadata`, `TryAnalyzeExportRange`, `TryGetRangeMetadataSummary`.
- [x] Добавить repository smoke tests для версионирования, mixed metadata range и access view.
- [x] Подключить real-data smoke проверку из `Desktop\AMK_Data`: загрузка поддержанных файлов через `ToolDataFileReader` и `ToolDataRepository`.
- [x] Добавить smoke tests для canonical save range, `.bin` cut range, `.DEV` cut range и отказов при invalid/same/mismatched range.
- [ ] Добавить golden fixtures с зафиксированными ожидаемыми значениями на выбранных реальных файлах старого приложения.
- [x] Перенести range export, save range, cut range: `SaveDataRange` пишет canonical 240-byte records, `SaveFileExcludingRange` сохраняет исходные physical frames при совпадающем формате.
- [ ] Перенести sidecar-сегменты `%LOCALAPPDATA%\AMK-Horizon-New-Chart\segments\...` с новым пространством хранения и миграцией старых sidecar.
### Этап 4. Metrology: расчеты и from computer/from sonde

Задачи:

- Перенести нормализацию фаз и перевод единиц: амплитуда в mV, фаза в mdeg.
- Перенести `get_condition`, `formula_simmetry`, `simmetry`, `SIGNAL`, `RO_ARG` без изменения математики.
- Отдельно оформить текущее ограничение: часть `from_computer` исторически работает по T1..T4.
- Решить и документировать стратегию для T5: сохранить старое поведение или добавить согласованную 5Tx-логику отдельной задачей.
- Перенести проверки наличия метрологии и совпадения signature.
- Добавить сравнение `from_sonde`, `from_computer`, `difference`.

### Этап 5. Hardware: COM, polling и запись

Задачи:

- Перенести COM-протокол `[CMD:1][LEN:2][PAYLOAD][CRC16:2]`.
- Перенести команды `0x11`, `0x12`, `0x13`, `0x16`, `0x17` и связанные сценарии.
- Перенести чтение подписи, чтение метрологии, polling кадров, air calibration.
- Реализовать `System.IO.Ports` transport с таймаутами и отменой.
- Перенести запись потока кадров в файл по правилам старого приложения.
- Перенести настройки `lastComPort`, `lastRecordFolder` в новый settings store.
- Перенести `DeviceExchangeLog` как потокобезопасный сервис логирования.

### Этап 6. Charting: данные графиков и геометрия

Статус: начат 2026-07-07, продолжен 2026-07-08. Первая версия aligned data-series слоя перенесена в `AMK.DataVisualization.Charting`: `DataType`, `FrequencyBand`, `Transmitter`, `GetDataIndexRange`, `NormalizePhase`, conversion to millidegrees и multi-series `CalculateChartData` для базовых типов. Также перенесен слой конфигурации кривых: `CurveDefinition`, `ChartConfiguration`, axis grouping, `CalculateSingleCurveData`, `CalculateCustomCurveData` и `CalculateChartDataFromConfiguration`.

Задачи:

- [x] Перенести `DataType`, `FrequencyBand`, `Transmitter` для базовых графических рядов.
- [x] Перенести aligned multi-series расчет для `PHASE_DIFFERENCE`, `AMPLITUDE_RATIO`, `AMPLITUDE_RX1`, `AMPLITUDE_RX2`, `PHASE_SMT`, `RHO_SMT`, silent amplitude/phase и `TEMPERATURE`.
- [x] Сохранить старую семантику пропусков: отсутствующие/невидимые значения добавляются как `NaN`, чтобы не ломать X-alignment.
- [x] Сохранить phase normalization и conversion `57295.7795 mdeg/rad`.
- [x] Добавить Charting smoke tests на synthetic records и реальный файл из `Desktop\AMK_Data`.
- [x] Перенести модели `ChartConfiguration`, curve definitions и axis grouping.
- [x] Перенести `CalculateSingleCurveData`, `CalculateCustomCurveData` и `CalculateChartDataFromConfiguration`.
- [x] Сохранить совместимость `CalculateSingleCurveData`: `valueScale` применяется после расчета; `RHO_SMT` и `TEMPERATURE` удаляют точки вне `minY/maxY`, остальные типы сохраняют aligned-ряд.
- [ ] Перенести graph data source для одного/двух графиков.
- [ ] Поддержать один и два графика.
- [ ] Поддержать graph 2 в linked mode и independent file mode.
- [ ] Поддержать X-axis по `FRAME_NUMBER`, `TEMPERATURE`, `Surf` и crossplot-сценарии.
- [ ] Поддержать single-axis и dual-axis, включая Y zoom target.
- [ ] Перенести zoom, scroll, undo/reset, crosshair, cursor coordinate fields.
- [ ] Перенести LOD/downsampling и cache-инварианты.
- [ ] Обеспечить единый geometry path для draw, screen-to-data, crosshair, notes и hit-test.
### Этап 7. UI: WinUI shell и рабочие сценарии

Задачи:

- Построить основной shell: меню/command bar, рабочая область графиков, панель источников, статусная строка.
- Перенести схему прибора и capability-aware доступность T1..T5.
- Перенести пресеты кривых: амплитуды R1/R2, отношения, разности фаз, silent, температура, UES, симм. фазы.
- Перенести немодальный лог обмена с прибором.
- Перенести notes UI и визуализацию маркеров.
- Перенести окно конструктора файлов.
- Проверить поведение на обычном и широком экране, без наложения текста и элементов.

### Этап 8. Reporting, installer, update

Задачи:

- Перенести отчетные и экспортные сценарии.
- Подготовить Inno Setup installer по модели `LOOP_NEXT`.
- Решить стратегию автообновления: сохранить текущую HTTP/FTP-модель или заменить на отдельный корпоративный updater.
- Синхронизировать versioning: `.csproj`, installer, release notes, документация.

## 6. Минимальная первая вертикаль реализации

Первая рабочая вертикаль должна быть маленькой, но настоящей:

1. Открыть `.bin`.
2. Распознать signature и capabilities.
3. Прочитать кадры в canonical `GP_TOOL_DATA`.
4. Показать один график с одной выбранной кривой.
5. Поддержать zoom и координаты курсора.
6. Сравнить значения с текущим приложением на одном контрольном файле.

## 7. Риски

- Ошибки в бинарной укладке приведут к тихо неправильным графикам.
- Нельзя переносить UI-логику раньше data model: появится красивый интерфейс без гарантии паритета.
- `from_computer` для 5Tx требует отдельного решения по T5, чтобы не выдать улучшение за перенос.
- Производительность графиков нужно проверять на реальных больших файлах, а не на маленьких демо-данных.
- Graph 2 independent mode и COM live/file view легко сломать, если источник данных будет глобальным состоянием.

## 8. Правило обновления этого плана

Этот файл описывает общий маршрут. Текущий ход работ, задачи сессии и фактические статусы фиксируются в `docs/SESSION_TASKS.md`.








