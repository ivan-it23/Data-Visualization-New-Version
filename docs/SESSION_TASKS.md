# AMK Data Visualization - задачи текущей сессии

Последнее обновление: 2026-07-13

## Назначение файла

Этот файл ведется как живой журнал текущей работы. В него нужно вносить:

- цель текущей сессии;
- выполненные действия;
- незавершенные задачи;
- решения, которые нельзя потерять между сообщениями;
- проверки, которые уже выполнены или еще не выполнены.

## Текущая цель

Продолжить перенос логики из `Amk-Horizon-New-Chart` в новый WinUI 3 / .NET 10 проект `AMK-Data-Visualization`, двигаясь от проверяемых data contracts к файловому вводу, stateful canonical storage и затем к первой вертикали отображения графика.

## Выполнено

- [x] Проверена структура созданного Visual Studio проекта.
- [x] Выбрана целевая модель: WinUI 3 / .NET 10, x64, unpackaged, self-contained Windows App SDK.
- [x] Создана модульная структура решения:
  - `AMK.DataVisualization.App`
  - `AMK.DataVisualization.Core`
  - `AMK.DataVisualization.IO`
  - `AMK.DataVisualization.Hardware`
  - `AMK.DataVisualization.Metrology`
  - `AMK.DataVisualization.Charting`
  - `AMK.DataVisualization.Reporting`
- [x] Подготовлен общий план миграции: `docs/MIGRATION_PLAN.md`.
- [x] Создан этот файл живого журнала: `docs/SESSION_TASKS.md`.
- [x] Перенесены бинарные контракты `GpToolData` и `MetrologyData` с `StructLayout(Pack=1)` и fixed-buffer layout.
- [x] Добавлены semantic accessors для каналов `[frequencyIndex][transmitterIndex]` и silent-каналов.
- [x] Перенесены `FileFormat`, `DeviceType`, `ToolDataKind`, `ToolCapabilities`, `ToolRecordMetadata`, `ToolIdentity`.
- [x] Перенесены `UniversalCanonicalAdapter`, `ToolDataAdapterRegistry`, `ParsedToolFrame`.
- [x] Добавлены `ToolDataAccessView` и `MeasurementView` для capability-aware доступа к canonical record.
- [x] Добавлен `ToolDataFileReader` для `.bin`, `.biv`, `.DEV`.
- [x] Перенесены правила старого `DataModule`: extension detection, signature offset, physical frame size, adapter support check, size multiplicity.
- [x] Добавлен `DataLoadResult` / `DataFileValidationResult` для pointer-free результата загрузки.
- [x] Добавлен `ToolDataRepository` как stateful-аналог части старого `DataModule`.
- [x] Перенесены `ApplyLoadedData`, `ClearData`, `AddData`, `AddDataBatch`, `AddParsedData`, `dataVersion`, `LoadedFileName`, records/metadata access.
- [x] Перенесены `DataRangeMetadataSummary`, `DataRangeExportAnalysis`, `TryDescribeRangeMetadata`, `TryAnalyzeExportRange`, `TryGetRangeMetadataSummary`.
- [x] Добавлен `ToolDataRangeExporter` для `SaveDataRange` и `SaveFileExcludingRange`.
- [x] Перенесено старое различие контрактов: `SaveDataRange` пишет canonical 240-byte records из repository, а `SaveFileExcludingRange` копирует исходные physical frames и сохраняет `.DEV` header.
- [x] Добавлен `AMK.DataVisualization.Charting.Series.ChartDataCalculator`.
- [x] Перенесены `DataType`, `FrequencyBand`, `Transmitter`, `GetDataIndexRange`, `NormalizePhase`, conversion to millidegrees.
- [x] Перенесен aligned multi-series расчет для базовых типов: phase difference, amplitude ratio, RX1/RX2 amplitude, phase/rho SMT, silent values, temperature.
- [x] Перенесены `CurveDefinition`, `ChartConfiguration`, `ElementType`, `XAxisType`, `SmtComputationMode`, `PresetType`.
- [x] Перенесены `UpdateAxisConfiguration`, `GroupCurvesByAxis`, `CalculateSingleCurveData`, `CalculateCustomCurveData`, `CalculateChartDataFromConfiguration`.
- [x] Сохранена совместимость конфигурационного расчета: disabled curves пропускаются, неизвестные `YAxisGroup` игнорируются, порядок кривых по осям сохраняется, `valueScale` применяется после расчета.
- [x] Сохранена особенность старого `CalculateSingleCurveData`: для `RHO_SMT` и `TEMPERATURE` значения вне `minY/maxY` удаляются из результата, а остальные типы рассчитываются как aligned-ряды без Y-фильтра.
- [x] Реализована первая UI-вертикаль WinUI: `Open` -> `FileOpenPicker` -> `ToolDataRepository` -> `ChartConfiguration` -> `ChartDataCalculator` -> один график.
- [x] Добавлен первый `LineChartControl` для отображения одного ряда с сеткой, осями, пустым состоянием и theme-aware контейнером.
- [x] Исправлена первая WinUI-оболочка под схему старого главного окна: верхнее меню, компактная панель управления графиком, график на всю рабочую область, нижняя статусная строка.
- [x] Англоязычные подписи в первой UI-вертикали заменены на русские/legacy-совместимые.
- [x] Перенесен источник данных Graph 2: `Стандартный режим` использует repository Graph 1, `Отдельный файл` — собственный `ToolDataRepository`.
- [x] В shell добавлены второй график, вторая панель управления, поля курсора и controls заметок строго между областями графиков; видимость определяется `Двойной режим`.
- [x] Перенесен контракт Graph 2: кнопка выбора файла видна только в `Отдельном файле`; переключение режима не очищает данные Graph 1 и не удаляет загруженный независимый источник Graph 2.
- [x] Статусная строка воспроизводит legacy-формат: один источник — `Кадров: N | файл`, независимый Graph 2 — `Г1: файл (N) | Г2: файл (N)`.
- [x] Исправлена legacy-совместимая компоновка панелей: полная подпись `Двойной режим`, одинаковый межэлементный интервал и отсутствие наложений в controls Graph 1/Graph 2; поля координат сохранены справа.
- [x] Удалена скрытая стандартная минимальная ширина WinUI у чекбоксов `Поток`, чтобы видимый интервал до поля количества кадров совпадал с интервалом между остальными элементами.
- [x] Перенесено немодальное окно «Схема прибора»: повторное открытие активирует существующее окно, закрытие и `Отмена` скрывают его, `Применить` оставляет окно открытым.
- [x] Перенесены ручной комбинированный выбор элементов, capability-aware доступность T1..T5 и legacy-проверки несовместимых комбинаций перед применением.
- [x] Перенесены быстрые пресеты: амплитуды R1/R2, отношения амплитуд, разности фаз, УЭС, симметричные фазы, амплитуды/разность фаз молчащего режима и температура.
- [x] Конфигурации и незавершенные состояния редактора изолированы для Graph 1/Graph 2; после смены файла или режима источника capabilities обновляются по `ToolDataRepository.DataVersion`.
- [x] Конфигурация применяется к соответствующему графику через `ChartWorkspace`; выбранная температурная ось X и `PresetDisplayName` передаются в `LineChartControl`.

## В работе

- [x] Перенести stateful-часть старого `DataModule` в `AMK.DataVisualization.IO`.
- [x] Проверить чистую сборку всего решения после range export-шага.
- [x] Проверить фактический запуск WinUI-окна после range export-шага.
- [x] Проверить фактический запуск WinUI-окна после Charting data-series шага.
- [x] Перенести `ChartConfiguration`, curve definitions, axis grouping и конфигурационный расчет кривых.
- [x] Проверить сборку после переноса `ChartConfiguration`.
- [x] Сделать первую вертикаль UI: открыть файл -> прочитать canonical frames -> показать один график.
- [x] Проверить сборку и фактический запуск WinUI-окна после первой UI-вертикали.
- [x] Вернуть первой UI-вертикали расположение элементов старого главного окна вместо новой боковой панели.
- [x] Проверить сборку и фактический запуск WinUI-окна после исправления legacy shell.
- [x] Перенести source-state Graph 2 и переключение linked/independent в отдельный WinUI service.
- [x] Проверить сборку и фактический запуск WinUI-окна после Graph 2 vertical.
- [x] Проверить через UI Automation подписи, выравнивание и отсутствие пересечений controls обеих панелей.
- [x] Перенести редактор «Схема прибора», комбинированные конфигурации и быстрые пресеты для Graph 1/Graph 2.
- [x] Проверить через UI Automation открытие окна, доступность основных controls, генерацию быстрого пресета и независимость незавершенных состояний Graph 1/Graph 2.

## Не начато

- [x] Перенести range export / save range / cut range.
- [ ] Перенести sidecar-сегменты старого приложения и новое пространство хранения.
- [x] Перенести первую версию Charting data-series слоя: `DataType`, `FrequencyBand`, transmitter-aware aligned series.
- [x] Перенести Charting configuration слой: `ChartConfiguration`, curve definitions, axis grouping.
- [x] Сделать первую вертикаль UI: открыть файл -> прочитать canonical frames -> показать один график.
- [ ] Автоматизировать или вручную зафиксировать parity-сценарий выбора реального файла через WinUI picker.
- [ ] Проверить вручную Graph 2 на реальных файлах: linked-режим, переход в `Отдельный файл`, выбор второго файла и возврат в `Стандартный режим`.

## Решения текущей сессии

- `Core` остается единственным владельцем бинарной структуры и adapter-driven parsing.
- `IO` не дублирует логику расшифровки кадра: он читает контейнер и передает 240-байтный payload в `ToolDataAdapterRegistry`.
- `ToolDataRepository` хранит только canonical records и metadata; расчетные chart-derived caches переносятся в `Charting`, но `dataVersion` уже повышается в тех же местах, что и в старом `DataModule`.
- `AddDataBatch` повторяет старое поведение: пустой batch не повышает `dataVersion`.
- Mixed metadata range считается валидным для canonical export, но требует adapter-aware policy, как в старом `TryAnalyzeExportRange`.
- Автоматизированные тестовые проекты удалены из решения 2026-07-13. Функциональная проверка выполняется владельцем проекта вручную на реальных `.bin/.biv/.DEV` после завершения очередного пользовательского сценария; сборка подтверждает только компиляцию.
- `ChartDataCalculator.CalculateChartData` остается aligned multi-series API; конфигурационный слой `CalculateSingleCurveData` повторяет старую ветку `DataModule` с особым Y-фильтром только для `RHO_SMT` и `TEMPERATURE`.
- Первая UI-вертикаль использует старый default `CurveDefinition`: `AmplitudeRx1`, `T1`, `400 kHz`, единицы `mV`. UI не считает значения сам, а вызывает `ChartDataCalculator.CalculateChartDataFromConfiguration`.
- Для WinUI-дизайна разрешены современная визуальная отделка и Fluent-технологии, но расположение элементов, русские подписи и поведение главного окна берутся из `Amk-Horizon-New-Chart` без перестановок. Даже промежуточные вертикали должны использовать старую схему окна.
- Текущий старый shell-контракт главного окна: меню сверху (`Файл`, `Настройки подключения`, `Метрология`, `Конструктор файлов`, `Заметки`, `Логи`, `Проверить обновления`, `Документация`), затем компактная панель `Двойной режим` / `Схема прибора` / X-range / `Поток` / кнопки опроса и подключения / поля курсора, затем область графика, затем статусная строка.
- Источник Graph 2 изолирован в `AMK-Data-Visualization/Services/ChartWorkspace.cs`; UI не выбирает repository напрямую. Это повторяет старый `ChartManager::GetDataModuleForChart`: Graph 2 читает Graph 1 в linked-режиме и отдельное хранилище только в independent-режиме.
- При переносе `SchematicModule` конфигурации Graph 1 и Graph 2 должны оставаться независимыми, а пустая конфигурация должна показывать «Нажмите 'Схема прибора' для настройки графика».
- Старый `SchematicModule` создает окно через `CreateDialogParam`, поэтому WinUI-версия также остается немодальной и сохраняет редактор отдельно для каждого графика.
- Режимы УЭС/симметричных фаз «на компьютере» не имитируются: пока вычисления и состояние метрологии не перенесены в `AMK.DataVisualization.Metrology`, редактор показывает legacy-предупреждение и возвращает выбор «на приборе».

## Проверки текущей сессии

- Build после удаления тестовых проектов: успешно, `0` warnings / `0` errors. Runtime и работа на реальных файлах в рамках этого изменения не проверялись.
- Build после Charting data-series переноса: успешно, `0` warnings / `0` errors.
- Launch после Charting data-series переноса: успешно, `EnumWindows` подтвердил видимое окно процесса `38272`: title `AMK Data Visualization`, handle `3803448`.
- Build после `ChartConfiguration` переноса: успешно, `0` warnings / `0` errors.
- Build после первой UI-вертикали: успешно, `0` warnings / `0` errors.
- Launch после первой UI-вертикали: успешно, `Get-Process` подтвердил видимое окно процесса `34836`: title `AMK Data Visualization`, handle `4392264`, `Responding=True`.
- Build после исправления legacy shell: успешно, `0` warnings / `0` errors.
- Launch после исправления legacy shell: успешно, `Get-Process` подтвердил видимое окно процесса `26512`: title `Amk-Horizon-New-Chart`, handle `10815326`, `Responding=True`.
- Build после Graph 2 source-state/fallback переноса: успешно, `0` warnings / `0` errors.
- Launch после Graph 2 source-state/fallback переноса: успешно, `Get-Process` подтвердил окно процесса `38784`: title `Amk-Horizon-New-Chart`, handle `722790`, `Responding=True`; повторная проверка через 8 секунд сохранила те же признаки.
- Build после исправления компоновки панелей: успешно, `0` warnings / `0` errors.
- Launch после финального исправления интервала `Поток`: успешно, процесс `3904`, title `Amk-Horizon-New-Chart`, handle `2295478`, `Responding=True`.
- UI Automation после включения `Двойной режим` подтвердила одинаковый интервал: Graph 1 — границы `Поток`/поля `858/863 px`, Graph 2 — `636/641 px`; пересечений нет.
- Финальный build после переноса «Схемы прибора»: успешно, `0` warnings / `0` errors.
- Финальный launch: процесс `24180`, title `Amk-Horizon-New-Chart`, handle `3344204`, `Responding=True`; приложение оставлено запущенным.
- UI Automation подтвердила видимое немодальное окно с заголовками `Схема прибора - График 1` / `Схема прибора - График 2`, доступность быстрых пресетов, списков частоты и X-axis. На Graph 1 пресет «Амплитуды R1» создал пять включенных кривых T1..T5; отдельный незавершенный выбор T1 на Graph 2 не изменил состояние Graph 1.
- Реальные `.bin/.biv/.DEV` в этой проверке не загружались; при пустом repository кнопка `Применить` ожидаемо недоступна.

## Следующий рекомендуемый шаг

Следующий шаг: владельцу проекта вручную проверить комбинированные и быстрые пресеты Graph 1/Graph 2 на реальных приборных файлах. После фиксации parity-результатов переносить метрологический расчет для режимов УЭС/симметричных фаз «на компьютере» и оставшиеся интерактивные функции графиков: zoom, scroll, crosshair и координаты курсора.










