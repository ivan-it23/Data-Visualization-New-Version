# AMK Data Visualization — handoff для новой сессии

Снимок состояния: **2026-07-15**.

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

Версия приложения в `.csproj` — `0.1.1`. Она является единственным источником
версии для `DataVisualization.exe` и Inno Setup installer.

## 4. Архитектура нового проекта

| Слой | Ключевые файлы | Фактическое состояние |
| --- | --- | --- |
| App | `MainWindow.*`, `Views/*.xaml`, `Controls/LineChartControl.*` | shell, два графика, схема, подключение и метрология |
| App services | `Services/ChartWorkspace.cs`, `Services/AutoUpdateService.cs` | источники Graph 1/Graph 2 и проверка/загрузка обновлений |
| Core | `Binary/BinaryStructs.cs`, `Adapters/ToolDataAdapterRegistry.cs`, `Access/ToolDataAccessView.cs` | binary contracts и semantic access |
| IO | `DataFiles/ToolDataFileReader.cs`, `ToolDataRepository.cs`, `ToolDataRangeExporter.cs` | файлы, repository, range export |
| Charting | `Series/ChartDataCalculator.cs`, `Configuration/*.cs` | series, конфигурации, presets |
| Hardware | `Serial/SerialDeviceService.cs`, `LegacyToolSerialTransport.cs`, `DeviceProtocolLog.cs` | dual COM transport, polling, recording и файловая диагностика |
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

### Картограф в режиме LWD 4tx

- источник структуры кадра и особенностей live-обмена:
  `LOOP_NEXT/src/LOOP.Next.Core/Models/ToolIdentity.cs` и
  `LOOP_NEXT/src/LOOP.Next.Hardware/ToolPort.cs`;
- критерий режима берётся только из сигнатуры: тип `Картограф` и 4 Tx;
  модификация намеренно не участвует в распознавании;
- такая сигнатура materialize в общий 240-байтовый `GpToolData`; T5 считается
  недоступным;
- логический `ToolDataKind` этой ветки — `Lwd`, физический `DeviceType` —
  `Cartograph`;
- semantic access-view применяет к активным и молчащим амплитудам коэффициент
  `1 / 4096`, переводя Q12-представление Картографа в общий LWD-контракт `mV`;
  отношение амплитуд остаётся безразмерным, а фазы, УЭС и температура не
  масштабируются;
- обычный `Картограф 5tx` не принимается этим adapter, потому что использует
  другую бинарную структуру;
- при live-опросе нулевой CRC допускается только для этого режима; после такого
  приёма сигнатура кадра обязана совпасть с сигнатурой метрологии/прибора;
- `LOOP_NEXT` transport читает фиксированный ответ
  `[CMD:1][PAYLOAD:242][CRC16:2]`: первые два байта payload служебные, следующие
  240 байт — `GpToolData`/`MetrologyData`; поле `LEN` в этой ветке не
  интерпретируется;
- фактический успешный ответ `LOOP_NEXT` для сигнатуры `0x00055349` начинается
  `11 05 06 49 53 05 00`; прежний общий parser ошибочно принимал `05 06` за
  длину `1541`, поэтому не мог принять ни одного такого кадра;

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
  → normal: packet length / CRC16 validation
  → Cartograph 4tx: fixed 242-byte LOOP_NEXT payload / CRC16 validation
  → ParsedToolFrame
  → primary ToolDataRepository
  → Graph 1 и linked Graph 2
```

Параллельно сервис может создавать canonical `.bin` или дописывать выбранный
существующий файл. `DeviceProtocolLog` с первой попытки подключения пишет в
`<корень проекта>\CartographLwd4Tx.log` последовательность TX/RX, размеры,
сырые head/tail, CRC, таймауты, выбор transport и причины отклонения кадров.

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
- отдельные `ChartViewportState` для Graph 1 и Graph 2;
- X Range работает по индексам записей, включая пустой `To` как диапазон до
  последней записи;
- drag-zoom задаёт X/Y, а при активном `Поток` сохраняет X и меняет только Y;
- каждый drag-zoom сохраняет предыдущее состояние в отдельной истории графика,
  а кнопка `↶` возвращает zoom по одному шагу;
- `Поток` показывает последние N записей и следует за live-кадрами;
- X Range и `Поток` автомасштабируют выбранные записи на всю ширину графика;
- автоматический Y-диапазон получает 5% padding сверху и снизу до построения
  шкалы, поэтому экстремумы кривых не совпадают с рамкой plot-area;
- drag-zoom показывает X/Y-скроллбары; X сохраняет ширину выбранного окна и
  перемещает его по полному файлу, Y перемещает выбранное окно по диапазону
  данных текущего X-участка;
- применённый через поля X Range также показывает X-скроллбар по полному файлу;
  в режиме `Поток` X-скролл скрыт, поскольку окно следует за последними N
  записями, но Y-only drag-zoom и вертикальный Y-скролл остаются доступны;
- standalone WinUI `ScrollBar` закреплены в состоянии `MouseIndicator` и подняты
  над canvas, чтобы `Visibility=Visible` не оставлял их в скрытом `NoIndicator`;
- горизонтальная прокрутка выполняет legacy-аналог `AutoFitYForCurrentX`:
  сохранённый drag-zoom по Y освобождается, после чего левая и правая оси
  пересчитываются по min/max кривых нового видимого X-участка с 5% padding;
- reset range/zoom сохраняет X-окно `Поток`, как в legacy;
- Graph 1 сохраняет выбранный участок в canonical `.bin` и вырезает его из
  текущего файла либо сохраняет копию без участка.

Не реализовано:

- crosshair и заполнение cursor-полей;
- LOD/downsampling;
- hit-test и notes geometry;
- полная общая geometry-модель для draw/hit-test/notes/crosshair.

## 9. Текущее окно подключения

Ключевые файлы:

- `AMK-Data-Visualization/Views/ConnectionSettingsWindow.xaml`;
- `AMK-Data-Visualization/Views/ConnectionSettingsWindow.xaml.cs`;
- `src/AMK.DataVisualization.Hardware/Serial/SerialDeviceService.cs`;
- `src/AMK.DataVisualization.Hardware/Serial/SerialConnectionSettingsStore.cs`;
- `src/AMK.DataVisualization.Hardware/Serial/SerialRecordingFolder.cs`;
- `src/AMK.DataVisualization.Hardware/Serial/LegacyToolSerialTransport.cs`;
- `src/AMK.DataVisualization.Hardware/Serial/DeviceProtocolLog.cs`.

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

Папка записи:

- default — системный рабочий стол + `AMK_Data`; папка создаётся при загрузке
  настроек, конфигурировании записи и повторно непосредственно перед созданием
  файла;
- путь, выбранный пользователем через `Папка...`, сохраняется в settings и
  используется при следующих запусках;
- старое значение, равное корню рабочего стола и сохранённое прежней WinUI-
  реализацией, одноразово мигрирует в `Desktop\AMK_Data`; версия настройки не
  позволяет этой миграции переопределить последующий явный выбор пользователя.

Протокол:

- 125000 baud, 8N1, no handshake;
- обычный AMK transport: DTR/RTS enabled и packet
  `[CMD:1][LEN:2][PAYLOAD][CRC16:2]`;
- `Картограф LWD 4tx`: DTR/RTS disabled как в `LOOP_NEXT`, фиксированный packet
  `[CMD:1][PAYLOAD:N][CRC16:2]`, запрос `{05 00}`, ответ `N=242`, canonical
  структура со смещения 2;
- CRC16 с initial `0xFFFF` и polynomial `0xA001`;
- `0x11`: canonical frame;
- `0x13`: metrology;
- `0x16`: signature;
- polling interval 500–30000 мс;
- автоматическое переподключение после ошибок.

## 10. Текущее окно «Метрология», FromComputer и ограничения

Ключевые файлы:

- `AMK-Data-Visualization/Views/MetrologyWindow.xaml`;
- `AMK-Data-Visualization/Views/MetrologyWindow.xaml.cs`;
- `src/AMK.DataVisualization.Metrology/MetrologySession.cs`;
- `src/AMK.DataVisualization.Metrology/MetrologyCalculator.cs`;
- `src/AMK.DataVisualization.Hardware/Serial/SerialDeviceService.cs`;
- `src/AMK.DataVisualization.Charting/Series/ChartDataCalculator.cs`.

Текущий UI-контракт:

- окно немодальное, повторное открытие использует существующий экземпляр;
- базовый размер 980×642, обязательная клиентская область 964×604, размер
  пересчитывается с учётом фактического DPI;
- группы `Геометрия зонда`, `Воздушные калибровки` и `Идентификация зонда`
  имеют полужирные русские заголовки;
- `Air_zz` отображается как `Поправка фазы`, `Air_zz_amt` — как
  `Порог амплитуды`, с подсказками о назначении;
- L1/L2/F1/F2 по умолчанию read-only; установленный флажок
  `L/F только для чтения` фиксирует блокировку, снятый разрешает редактирование;
- ComboBox используют штатный шаблон WinUI без переопределения высоты и
  padding; типы прибора: `Автономный`, `LWD`, `Картограф`; число передатчиков:
  3/4/5; модификации: 1..9; положения R1: `R1→T1` / `R1→T2`;
- поле сигнатуры начинается по левой границе `Порог амплитуды F1` и
  заканчивается по правой границе `Порог амплитуды F2`; палитра заканчивается в
  начале сигнатуры;
- доступны команды `Считать из зонда`, `Записать в зонд`, `Открыть файл`,
  `Сохранить файл`, `Закрыть`, `Автокалибровка нулей воздуха`.

Файловый и приборный контракт:

- метрологический файл — ровно 240 байт;
- чтение выполняется командой `0x13`;
- запись обычному прибору отправляет `0x13` с payload 240 байт; legacy-ветка
  отправляет `0x13` с `{F5 00}` + 240 байт и принимает двухбайтовый ответ, после
  чего обе ветки верифицируются повторным чтением;
- selected key `0x17` и автокалибровка воздуха `0x1A` реализованы для обычного
  length-framed transport; для legacy-ветки в `LOOP_NEXT` нет подтверждённого
  аппаратного контракта, поэтому команды не отправляются вслепую и возвращают
  `InvalidResponse` с записью причины в журнал;
- для `Картограф -> LWD-4TX` (тип `Картограф`, 4 Tx, любая модификация) кадр
  `0x11` разбирается как canonical `GpToolData`; допускается нулевой CRC с
  обязательной проверкой совпадения signature;
- успешное чтение из файла или зонда обновляет `MetrologySession` и открывает в
  схеме режимы УЭС/симметризованных фаз `FromComputer`.

### Metrology / FromComputer

`SmtComputationMode.FromComputer` подключён к `ChartDataCalculator` через
`MetrologyCalculator`. Используются AIR-поправка, condition-byte и точные
матрицы 3/4/5Tx. Режим доступен в окне схемы только после чтения метрологии;
signature кадра должна совпасть с метрологией, иначе действует legacy fallback
к canonical `FromSonde`.

Runtime подтверждён для открытия окна, итоговой компоновки, полного содержимого
ComboBox и переключения read-only всех 12 полей L1/L2/F1/F2. Новый фиксированный
transport собран и статически проверен на фактической сигнатуре `0x00055349`, но
ещё не подтверждён повторным запуском этого приложения с физическим прибором.
Численный parity `FromComputer` со старым приложением также ещё не выполнен.

### Частично реализованные controls shell

- range apply/reset, drag-zoom, X/Y-scroll, пошаговый undo/reset zoom, `Поток`,
  сохранение и вырезание
  участка подключены; append-сценарии выбранного/исключённого диапазона и
  sidecar-перенос ещё не реализованы;
- cursor-поля read-only, но crosshair не реализован;
- notes controls не имеют storage/rendering логики;
- меню конструктора, логов и документации не перенесены; проверка обновлений
  подключена.

### Поставка, брендинг и обновление

- продукт, окно и профиль запуска называются «Визуализация данных»;
- основной executable — `DataVisualization.exe`;
- `Assets/DataVisualization.svg` и точная копия выбранного `small.ico`
  `Assets/CompanyEmblem.ico` являются исходниками иконки; предоставленные
  `small.ico` и `Amk-Horizon-New-Chart.ico` были идентичны по SHA-256;
  `scripts/Generate-AppIcon.ps1` размещает эмблему в правом верхнем углу и
  создаёт многоразмерный ICO и WinUI PNG-assets;
- `scripts/Build-Installer.ps1` публикует self-contained `Release x64` в
  изолированный каталог, переносит созданные WinUI `DataVisualization.pri` и
  `*.xbf` в payload, проверяет полный комплект и компилирует `installer/setup.iss`;
- installer устанавливается для всех пользователей, создаёт стандартную запись
  удаления и позволяет независимо выбрать общие ярлыки Start Menu/Desktop;
- update feed:
  `FilesForProgramVisualisation/DataVisualization/data-visualization-version.txt`;
  обязательны версия и имя setup, третьей строкой допускается SHA-256;
- после трёхсекундной фоновой либо ручной проверки новый setup скачивается во
  временный каталог, валидируется и запускается с UAC; приложение закрывается,
  Inno Setup заменяет весь комплект файлов и запускает его снова;
- ручной запуск установленной версии `0.1.0` выявил отсутствие PRI/XBF в payload
  и падение в `MainWindow.InitializeComponent`; формирование payload исправлено
  в `0.1.1`, но новая сборка runtime не запускалась по прямому требованию владельца.

### Пустые проекты

- `AMK.DataVisualization.Reporting` — только `.csproj`.

## 11. Что подтверждено, а что нет

Подтверждено:

- решение собирается в `Debug x64` с `0 warnings / 0 errors`;
- self-contained `Release x64` publish и Inno Setup compile выполняются;
- основное окно, схема, подключение и метрология запускались;
- владелец вручную подтвердил актуальную компоновку окна схемы и окна
  подключения, а также финальную компоновку и ComboBox окна метрологии;
- UI Automation ранее подтверждала независимость editor-состояний Graph 1/Graph 2.
- UI Automation для метрологии подтвердила четыре полных списка ComboBox,
  read-only переход 12/12 → 0/12 → 12/12 и совпадение заданных границ полей.

Не считать подтверждённым без новой ручной фиксации:

- полный паритет всех series на реальных `.bin/.biv/.DEV`;
- linked/independent Graph 2 с двумя контрольными файлами;
- range export/cut на всех форматах;
- повторный COM-тест fixed transport `Картограф LWD 4tx`, reconnect и запись на
  физическом приборе с анализом `CartographLwd4Tx.log`;
- создание `Desktop\AMK_Data` по умолчанию и сохранение вручную выбранной папки
  записи после перезапуска;
- parity результатов `FromComputer` со старым приложением на реальных данных;
- производительность на больших файлах.

## 12. Рекомендуемый следующий блок

Сначала вручную проверить быстрые и комбинированные пресеты `Картограф LWD 4tx`
после Q12-нормализации амплитуд. Затем выполнить ручную parity-проверку
`from_sonde`/`from_computer` на одинаковых реальных данных и проверить
чтение/запись метрологии на физическом приборе. Следующие блоки:
немодальное окно лога → интерактивная geometry графика → notes/sidecar → file
constructor → reporting; поставка уже реализована и требует ручной release-проверки.

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

После WinUI-изменений по умолчанию выполнить сборку и реальный запуск. Если
владелец явно просит не запускать приложение, ограничиться сборкой и в итоговом
сообщении явно указать, что runtime не подтверждён.

## 15. Правило обновления документов

- этот handoff обновлять при изменении архитектурных контрактов, реализованных
  вертикалей и точки продолжения;
- `MIGRATION_PLAN.md` обновлять при смене статуса этапов;
- `SESSION_TASKS.md` обновлять после каждой завершённой пользовательской задачи;
- `README.md` держать кратким входом и не превращать в журнал истории.
