# AMK Data Visualization — план миграции

Дата создания: 2026-07-06

Последняя актуализация: **2026-07-15**

- исходный проект: `C:\Users\Admin\Desktop\Amk-Horizon-New-Chart`;
- C++-исходники: `C:\Users\Admin\Desktop\Amk-Horizon-New-Chart\Amk-Horizon-New-Chart`;
- целевой проект: `C:\Users\Admin\Desktop\AMK-Data-Visualization`.

## 1. Цель

Перенести пользовательскую, файловую, приборную и расчётную логику
`Amk-Horizon-New-Chart` из Win32/C++ в модульное приложение WinUI 3 / .NET 10.
Первый приоритет — поведенческий паритет. Современный дизайн допустим только как
визуальная реализация старых сценариев и не должен незаметно менять бизнес-логику.

## 2. Непереговорные правила

- Старое приложение остаётся источником истины до ручной parity-проверки.
- Binary layout, размеры кадров, смещения, индексация каналов, единицы и формулы
  переносятся без смысловых изменений.
- Любое намеренное улучшение поведения документируется отдельно от миграции.
- UI не должен самостоятельно расшифровывать canonical данные или выполнять
  расчёты, принадлежащие Core/Charting/Metrology.
- Успешная сборка подтверждает компиляцию, но не работу на реальном файле,
  корректность графика или COM-обмен.
- Автоматизированных тестовых проектов в решении нет по решению владельца;
  функциональный паритет проверяется вручную.

## 3. Источники истины старого проекта

### Документы

- `doc/README.md` — обзор приложения;
- `doc/TECHNICAL_REFERENCE.md` — UI, графики, координаты и инварианты;
- `doc/ANALYSIS_GP_TOOL_DATA.md` — `GP_TOOL_DATA`, `METROLOGY_DATA`, capabilities;
- `doc/FILE_FORMATS_ANALYSIS.md` — `.bin`, `.biv`, `.DEV`;
- `doc/FILE_VALIDATION_FOR_5TX_MODEL.md` — валидация 5Tx;
- `doc/COM_5TX_GP_POLLING_AND_METROLOGY.md` — polling и метрология;
- `doc/ues_phase_from_computer_5tx.md` — УЭС/фазы «на компьютере»;
- `doc/PROJECT_STATUS.md` — актуальные ограничения старого приложения.

### C++-модули

- `ToolDataTypes.*`, `ToolDataAdapters.*`, `ToolDataAccess.h` — binary contracts;
- `DataModule.*` — файлы, canonical storage, расчёты и range export;
- `ChartManager.*`, `ChartModule.*` — источники графиков, geometry и rendering;
- `CurveSelectionModule.*`, `SchematicModule.*` — выбор кривых и пресеты;
- `SerialModule.*` — протокол, команды, polling и запись;
- `MetrologyModule.*` — метрологические формулы и параметры;
- `DeviceExchangeLog.*`, `DeviceLogPanel.*` — журнал обмена;
- `NotesModule.*` — заметки и sidecar;
- `FileConstructorModule.*` — конструктор файлов;
- `AutoUpdateModule.*`, `installer/setup.iss` — обновление и поставка.

## 4. Целевая архитектура

| Проект | Назначение | Состояние |
| --- | --- | --- |
| `AMK.DataVisualization.App` | WinUI shell, окна, orchestration | частично реализован |
| `AMK.DataVisualization.Core` | binary layout, domain models, adapters, access-view | базовый объём реализован |
| `AMK.DataVisualization.IO` | чтение файлов, repository, range export | базовый объём реализован |
| `AMK.DataVisualization.Hardware` | COM, packet protocol, polling, recording | первая вертикаль реализована |
| `AMK.DataVisualization.Charting` | конфигурации и подготовка серий | базовый объём реализован |
| `AMK.DataVisualization.Metrology` | метрологические файлы, состояние и вычисления «на компьютере» | прикладная вертикаль реализована |
| `AMK.DataVisualization.Reporting` | отчёты, печать, экспорт | пока пустой контейнер |

## 5. Этапы

### Этап 0. Базовая конфигурация — выполнен

- [x] WinUI 3 / `net10.0-windows10.0.26100.0`.
- [x] Только `x64`.
- [x] Unpackaged/self-contained Windows App SDK.
- [x] Модульное решение `.slnx`.
- [x] Явные зависимости Windows App SDK и `System.IO.Ports`.

### Этап 1. Фиксация parity — выполняется вручную

- [ ] Утвердить постоянный набор контрольных `.bin/.biv/.DEV`.
- [ ] Зафиксировать по каждому файлу signature, capabilities, число кадров,
  диапазон температур и ожидаемые series.
- [ ] Составить таблицу UI-сценариев Graph 1/Graph 2 и COM live/file.
- [ ] Фиксировать результаты ручных сравнений со старым приложением.

### Этап 2. Core — базовый объём выполнен

- [x] `GpToolData` и `MetrologyData`, по 240 байт.
- [x] Semantic accessors для frequency/transmitter и silent-каналов.
- [x] `ToolCapabilities`, `ToolRecordMetadata`, `ParsedToolFrame`.
- [x] `UniversalCanonicalAdapter`, registry и access-view.
- [x] `Картограф` в режиме `LWD 4tx`: тип и 4 Tx определяются из signature без
  привязки к модификации; используется общий `GpToolData`, T5 отсутствует.
- [x] Q12-амплитуды `Картограф LWD 4tx` нормализуются в semantic access-view
  коэффициентом `1 / 4096`, после чего используют общие LWD-пресеты и единицы;
  исходные canonical-кадры остаются без преобразования.
- [ ] Следующие типы приборов добавлять только через adapter/capabilities слой.

### Этап 3. IO — базовый объём выполнен, sidecar не перенесён

- [x] Валидация `.bin`, `.biv`, `.DEV`.
- [x] Физические кадры 240 и 251 байт, `.DEV` payload offset 11.
- [x] Materialize в canonical records + metadata.
- [x] Stateful `ToolDataRepository` и `DataVersion`.
- [x] Metadata summary диапазонов.
- [x] `SaveDataRange` и `SaveFileExcludingRange`.
- [ ] Ручная проверка всех форматов и range export.
- [ ] Sidecar-сегменты и миграция старого пространства хранения.

### Этап 4. Metrology — кодовая вертикаль реализована

- [x] Строгий 240-байтовый `MetrologyData`, включая `work_type`, `Rx_Position`
  и 78 служебных `ushort`.
- [x] Чтение/запись 240-байтовых файлов и проверка signature.
- [x] Общее состояние метрологии; режимы «на компьютере» доступны только после
  чтения из зонда или файла.
- [x] Перенесены condition-byte, AIR-поправка и матрицы симметризации 3/4/5Tx.
- [x] Перенесены `SIGNAL` и обратный расчёт УЭС с legacy-константами и пределами.
- [x] `ChartDataCalculator` учитывает `curve.SmtMode`; при несовпадении signature
  сохраняется legacy fallback к canonical данным прибора.
- [x] Расчётная smoke-проверка: 240 байт, condition `0x1F/0x07`, контрольная
  матрица `[-1,0,0,0,1]`, обратный расчёт 25 → 24,9951.
- [x] Немодальное DPI-aware окно метрологии: русские смысловые подписи,
  штатные WinUI ComboBox, типы `Автономный` / `LWD` / `Картограф`, геометрия и
  частоты только для чтения при установленном `L/F только для чтения`.
- [x] Runtime-путь открытия окна и переключение 12 полей L1/L2/F1/F2 проверены;
  layout сигнатуры, палитры и воздушных калибровок выровнен по фактическим
  границам контролов.
- [ ] Сравнить `from_sonde` и `from_computer` со старым приложением на одном
  реальном файле и метрологии.

### Этап 5. Hardware — первая вертикаль реализована

- [x] COM discovery.
- [x] 125000/8N1, transport-specific DTR/RTS, таймауты.
- [x] Обычный `[CMD][LEN][PAYLOAD][CRC16]` и фиксированный transport
  `LOOP_NEXT` `[CMD][PAYLOAD:N][CRC16]`, CRC16 Modbus polynomial `0xA001`.
- [x] `0x11` — чтение canonical кадра.
- [x] `0x13` — чтение метрологии.
- [x] `0x13` с payload 240 байт — запись метрологии и повторная верификация.
- [x] `0x16` — чтение signature.
- [x] `0x17` — selected key.
- [x] `0x1a` — автокалибровка нулей воздуха.
- [x] Polling 500–30000 мс и автоматическое переподключение.
- [x] Для `Картограф -> LWD-4TX` перенесён полный fixed-length путь `LOOP_NEXT`:
  запросы `{05 00}`, ответы payload 242 байта, canonical offset 2, разрешение
  нулевого CRC только на кадрах данных, повтор после таймаута и обязательная
  проверка signature кадра без привязки к модификации.
- [x] Корневой `CartographLwd4Tx.log`: TX/RX, chunks, CRC, таймауты, signature,
  выбор transport и причина приёма/отклонения кадра.
- [x] Новый canonical `.bin` и дозапись существующего файла; default-папка
  `Desktop\AMK_Data` создаётся автоматически.
- [x] `%LOCALAPPDATA%` settings store запоминает выбранную пользователем папку
  записи и повторно использует её после перезапуска.
- [x] Legacy-структура окна «Подключение к прибору».
- [ ] Ручная проверка на физическом приборе.
- [ ] Немодальное пользовательское окно лога; файловая диагностика transport
  уже реализована.

### Этап 6. Charting — данные и статический renderer реализованы частично

- [x] Типы series и частоты 400/2000 кГц.
- [x] Aligned series и `NaN`-разрывы.
- [x] Phase normalization и `57295.7795 mdeg/rad`.
- [x] `CurveDefinition`, `ChartConfiguration`, axis grouping.
- [x] Комбинированные конфигурации и preset-слой.
- [x] Graph 1/Graph 2 с независимыми конфигурациями.
- [x] Linked/independent repository Graph 2.
- [x] Single/dual Y-axis.
- [x] X по кадрам или температуре.
- [x] Grid, legacy nice-step и фиксированный числовой формат.
- [x] Legacy-совместимые легенды.
- [ ] Полная ручная parity-проверка на реальных файлах.
- [ ] Surf/crossplot X-axis.
- [x] X Range, drag-zoom, пошаговый undo/reset zoom и независимое viewport-состояние двух графиков.
- [x] `Поток`: последние N записей, follow-latest, Y-only drag-zoom и Y-scroll.
- [x] X/Y-scroll для zoom и X-scroll для диапазона из полей; в `Поток` X-scroll скрыт, Y-scroll доступен.
- [ ] Crosshair и cursor coordinates.
- [ ] LOD/downsampling и cache-инварианты.
- [ ] Общий geometry path для draw/hit-test/notes/crosshair.

### Этап 7. UI — shell и два ключевых окна реализованы частично

- [x] Legacy-порядок главного shell и статусная строка.
- [x] Двойной режим и controls Graph 2.
- [x] Открытие `.bin/.biv/.DEV` через picker.
- [x] Немодальная «Схема прибора» с отдельным состоянием Graph 1/Graph 2.
- [x] Полный текущий набор быстрых пресетов.
- [x] Capability-aware T1..T5 и единая жёлтая рамка выбора.
- [x] Собственная taskbar-кнопка окна схемы.
- [x] Выверенная владельцем компоновка окна схемы, включая DPI-safe footer.
- [x] Прямое открытие и legacy-компоновка окна подключения.
- [x] Немодальное окно «Метрология» с исходной расстановкой полей и кнопок.
- [x] Чтение/запись зонда, файл, установленный по умолчанию режим
  `L/F только для чтения` и автокалибровка воздуха.
- [x] Финальная компоновка окна метрологии проверена владельцем после серии
  DPI/layout-уточнений 14–15 июля 2026 года.
- [x] Range apply/reset, save selected range и cut current/copy для Graph 1.
- [ ] Append-варианты range/cut, sidecar-сегменты и cursor controls.
- [ ] Notes UI и маркеры.
- [ ] Конструктор файлов.
- [ ] Логи, документация и проверка обновлений.

### Этап 8. Reporting, installer, update — не начат

- [ ] Отчёты, печать и экспорт.
- [ ] Inno Setup installer.
- [ ] Стратегия автообновления.
- [ ] Синхронное versioning приложения, installer и документации.

## 6. Текущие рабочие вертикали

### Файл → график

`FileOpenPicker` → `ToolDataFileReader` → `ToolDataRepository` →
`ChartConfiguration` → `ChartDataCalculator` → `LineChartControl`.

### Прибор → live-график и запись

`SerialDeviceService` → normal length-framed или fixed `LOOP_NEXT` packet/CRC
validation → `ParsedToolFrame` → primary
`ToolDataRepository` → Graph 1 / linked Graph 2; параллельно canonical recording.

### Метрология → вычисления на компьютере

`MetrologyWindow` → `SerialDeviceService` или 240-байтовый файл →
`MetrologySession` → `ChartDataCalculator` → `MetrologyCalculator` → УЭС или
симметризованные фазы `FromComputer`. До успешного чтения метрологии эти режимы
недоступны; при несовпадении signature сохраняется legacy fallback к данным
прибора.

## 7. Границы готовности

Функция считается:

- **реализованной**, когда кодовая вертикаль существует;
- **build-verified**, когда всё решение собирается без ошибок;
- **runtime-verified**, когда открыто нужное окно и пройден UI-сценарий;
- **parity-verified**, только после сравнения со старым приложением на тех же
  данных или приборе.

В документации нельзя заменять один статус другим.

## 8. Основные риски

- Тихая ошибка binary layout даст визуально правдоподобные, но неверные графики.
- `FromComputer` нельзя имитировать чтением `FromSonde`.
- Graph 2 и COM live/file нельзя сводить к одному глобальному repository.
- Фиксированные размеры WinUI нужно проверять при фактическом DPI-масштабировании.
- Производительность нужно измерять на больших реальных файлах после переноса
  LOD/cache, а не выводить из текущей сборки.

## 9. Порядок продолжения

Если владелец не назначил другую пользовательскую проблему:

1. вручную проверить все пресеты `Картограф LWD 4tx` после Q12-нормализации
   активных и молчащих амплитуд;
2. выполнить parity-проверку Metrology / `FromComputer` на реальных данных;
3. проверить чтение и запись метрологии на физическом приборе;
4. добавить немодальное окно лога;
5. перенести интерактивную геометрию графика;
6. перенести notes/sidecar и конструктор файлов;
7. затем reporting, installer и update.

Оперативное состояние и конкретный следующий шаг фиксируются в
`SESSION_TASKS.md`.
