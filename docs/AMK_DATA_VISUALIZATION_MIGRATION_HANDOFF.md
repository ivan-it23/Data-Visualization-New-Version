# AMK Data Visualization — handoff для новой сессии

Последняя актуализация: **4 августа 2026 года**.

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
`0.1.7`, executable — `DataVisualization.exe`.

Важно: релизные setup `0.1.7` и descriptor собраны 4 августа 2026 года из текущего
дерева; SHA-256 (`72B32794…DAE122A1`) совпадает с независимо пересчитанным
хэшем. Артефакты готовы к публикации; за владельцем остаются загрузка в
HTTP-каталог и проверка установки/автообновления. Любая последующая правка
дерева требует нового номера версии — заменять содержимое уже использованного
номера нельзя.

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
- при X-scroll ручное Y-окно сохраняется: `ChartViewportState.ClampYRangesToData`
  оставляет его высоту и вписывает окно в Y-разброс новых видимых записей (или
  принимает разброс целиком, если он меньше окна), поэтому вертикальный скроллбар
  не пропадает. Автоматический Y-диапазон остаётся автоматическим и
  пересчитывается по новому участку. Legacy восстанавливает так только левую ось,
  а правую возвращает без вписывания — здесь обе оси обрабатываются одинаково;
- автоматический Y-padding 5%;
- `Поток` показывает последние N записей, следует за live-данными, блокирует
  ручное изменение X, но сохраняет Y-only zoom/scroll;
- WinUI ScrollBar принудительно удерживается в desktop-состоянии
  `MouseIndicator`;
- клик по графику устанавливает crosshair и заполняет cursor-поля, кнопка `⌖`
  сбрасывает оба состояния;
- при двух осях Y crosshair показывает координаты обеих осей: поле Y2 и его
  разделитель появляются только при `HasRightAxis` и скрываются при сбросе
  (legacy `UpdateClickCoordinates` / `ShowDualAxisControls`);
- выборочный zoom оси Y и пошаговый осевой undo — см. отдельные разделы ниже;
- контекстное меню crosshair переносит его X в начальную или конечную границу
  диапазона и открывает создание заметки;
- заметки совместимы с legacy append-only `.notes.jsonl`, фильтруются по
  конфигурации кривых, поддерживают маркеры, hit-test, редактирование, удаление
  и журнал с восстановлением конфигурации;
- кнопка «Заметки» открывает журнал напрямую, без промежуточного flyout;
- системная кнопка папки в статусной строке открывает каталог файла Graph 1 или
  COM-записи; при двух независимых файлах показывает выбор Graph 1/Graph 2.

Zoom, прокрутка и автомасштабирование подтверждены владельцем.

### Выборочный zoom оси Y и осевой undo

Перенесено из legacy (`ChartManager::GetEffectiveYZoomTarget`,
`HandleAxisUndoZoomStep`, `PushAxisHistory`, `PopAxisHistoryX/Y`,
`UIModule::ShowAxisUndoButtons`).

Цель zoom по Y:

- список «Zoom оси Y» с элементами «По двум осям» / «По левой оси» /
  «По правой оси» расположен в правом верхнем углу графика перед кнопкой `↶`;
  виден только при `HasDualAxis`, значение хранится в сессии per-graph и по
  умолчанию «По двум осям» (не персистится, как в legacy);
- `MainWindow.GetEffectiveYZoomTarget` возвращает `Both` для single-axis
  независимо от выбранного значения;
- нецелевая ось не пересчитывается: `ChartViewportState.TryResolveZoomedYRanges`
  закрепляет её на диапазоне, из которого она сейчас рисуется. Этот диапазон
  приходит из контрола в `ChartZoomRequestedEventArgs.LeftAxisBaseRange` /
  `RightAxisBaseRange` как `requested…Range ?? GetRawAxisRange(group)` —
  **до** display padding, потому что `Render` добавляет 5% сам; передача уже
  padded-диапазона расширяла бы закреплённую ось на каждый zoom;
- вертикальный скроллбар двигает только целевую ось:
  `LineChartControl.VerticalScrollTarget` выбирает primary-контекст и
  подавляет диапазон закреплённой оси (`null`), а
  `TrySetScrolledYRanges` не трогает ось, для которой пришёл `null`.

Осевой undo:

- `ChartViewportState` ведёт три стека: общий `zoomHistory` для кнопки `↶` и
  `xZoomHistory` / `yZoomHistory` для осевого undo. Все три пишутся вместе в
  `ApplyDragZoom`; `TryUndoZoom` снимает по одному элементу с каждого, чтобы
  стеки не расходились;
- `TryUndoZoomX` восстанавливает только X-окно, `TryUndoZoomY` — только
  диапазоны Y. Пустой стек возвращает ось к исходному состоянию (X — на весь
  файл, Y — к авто-подгонке), как в legacy;
- стеки осей очищаются везде, где сбрасывается диапазон:
  `TryApplyXRange`, `TrySetFlow`, `ResetRange` (и, через неё, `ResetZoom` /
  `ResetAll`);
- кнопки `−` живут в `LineChartControl` рядом со своими скроллбарами
  (`HorizontalUndoRequested` / `VerticalUndoRequested`) и показываются ровно
  тогда, когда виден соответствующий скроллбар (`SyncAxisUndoButtons`);
- вертикальный скроллбар начинается ниже своей кнопки `−`
  (`plot.Top + AxisUndoButtonSize + 2`), иначе кнопка попадала бы в
  верхнюю правую панель главного окна.

### On-chart оверлеи выбора кривых

Дополнительный способ настройки вывода кривых, не требующий открытия окна
«Схема прибора». Разделение ответственности:

- `LineChartControl` знает геометрию заголовка и легенды и владеет
  интерактивным слоем `OverlayCanvas` (hit-transparent поверх `ChartCanvas`,
  чтобы клики не попадали в drag-zoom). На нём: «шестерёнка» сразу после подписи
  графика (`DisplaySettingsRequested`), «глаз» после последней кривой легенды
  (`CurveVisibilityRequested`) и два пустых хоста, которые контрол только
  позиционирует, — `LeftOverlayHost` (в одну линию справа от шестерёнки) и
  `CenterOverlayHost` (по центру верхней границы). Свойство
  `ShowCurveVisibilityToggle` включает «глаз», `LegendTopReserve` опускает
  легенду ниже верхнего ряда оверлеев;
- `MainWindow` держит логику и наполняет хосты контрола: в `LeftOverlayHost` —
  `DropDownButton` быстрых пресетов и `DropDownButton` частоты (в этом порядке),
  в `CenterOverlayHost` — `InlineSchematicBar`; реагирует на события и
  переиспользует `SchematicConfigurationEditor` / `CurveSelectionService` /
  `ChartWorkspace`;
- выпадающие списки без заголовка: контент показывает текущее значение (имя
  пресета из `CurrentPresetDisplayName` или «Быстрые пресеты»; частота
  400/2000 кГц) и авто-подстраивает ширину; смена частоты переприменяет текущий
  выбор на новой частоте;
- `InlineSchematicBar` (`Controls/InlineSchematicBar.cs`) — компактная строка
  элементов схемы (T1–T5, R1/R2, молчащие, ширина молчащих 58 px под подпись)
  с инлайн-кнопкой «Применить» рядом с «Молч. R2», появляющейся при наличии
  несохранённого изменения;
- быстрые пресеты и комбинированный выбор на схеме — независимые ветви: пока
  активен быстрый пресет (`CurrentPreset != None`), элементы схемы не
  подсвечиваются (`UpdateState(..., showSelection)`);
- после инлайн-применения редактор схемы НЕ пересобирается: `inlineSchematicBaseConfig`
  переводится на только что применённую `ChartConfiguration`, поэтому
  `EnsureInlineEditor` не перевыводит выбор из кривых (перевывод давал ложную
  подсветку). Пересборка происходит только при внешней смене конфигурации;
  live-кадры не сбрасывают несохранённые правки;
- «глаз» напрямую переключает `CurveDefinition.Enabled` в текущей конфигурации и
  перерисовывает график (`GroupCurvesByAxis` исключает выключенные кривые);
- «глаз» виден только при включённой опции списка пресетов и/или верхней схемы;
  «шестерёнка» видна всегда; частота на «глаз» не влияет;
- выбор трёх опций для каждого графика сохраняется на диск
  (`Services/ChartDisplaySettingsStore.cs` →
  `%LOCALAPPDATA%\AMK-Data-Visualization\chart-display-settings.json`);
- оверлеи Graph 2 живут внутри его контрола и скрываются вместе с ним вне
  двойного режима.

#### Провенанс конфигурации и подсветка схемы

Каждая применённая конфигурация помечается источником
(`ChartWorkspace.ApplyConfigurationForGraph(..., ChartConfigurationSource)`):
`CurveSetupWindow`, `OnChartOverlay` или `NoteSnapshot`. Поверхность подсвечивает
элементы схемы только для конфигурации, которую построила сама: комбинированный
выбор (например T4 + молч. R1) невозможно однозначно восстановить из кривых —
`RestoreSelectionFromCurves` выводил из него R1/R2/молч. R1 и подсвечивал
элементы, которых пользователь не выбирал.

`SchematicConfigurationEditor` принимает `restoreSchematicSelection`. При `false`
он берёт из конфигурации только частоту, ось X и список кривых, а `Selection`
остаётся пустой — окно и инлайн-схема открываются без подсветки. Правило
симметрично: окно не подсвечивает конфигурацию с графика, инлайн-схема — из
окна и из заметки.

#### Ось X в панели управления графиком

Четвёртая опция флайаута шестерёнки (`GraphDisplayOptions.ShowXAxisDropdown`,
персистится вместе с остальными). Для Graph 1 список «Ось X» выводится справа от
кнопки «Включить/Отключить», для Graph 2 — в конце его панели, то есть за
кнопкой режима данных и кнопкой папки. Смена значения пишет
`ChartConfiguration.XAxisType`, синхронизирует inline-редактор, сбрасывает сессию
окна настройки и перерисовывает график; zoom при этом не сбрасывается — так же,
как при смене оси X внутри окна настройки.

#### Capability-awareness оверлеев

Оверлеи обязаны подчиняться возможностям прибора так же, как окно «Схема
прибора». Единый источник — `ChartWorkspace`:

- `GetAvailableTransmitterCountForGraph(graphNumber)` — число передатчиков по
  metadata нулевого кадра репозитория графа, при отсутствии данных
  `SchematicSelection.UiTransmitterCount`. Это единственное место резолвинга:
  и `MainWindow.EnsureInlineEditor`, и `SchematicWindow.ActivateEditorForGraph`
  вызывают его, а не читают metadata сами;
- `DropUnsupportedCurvesForGraph(graphNumber)` — удаляет из применённой
  конфигурации кривые передатчиков, которых у источника нет, и пересчитывает
  оси. Вызывается в начале `RenderGraph`, поэтому конфигурация, построенная для
  5Tx и оставшаяся после перехода на `Картограф LWD 4tx`, теряет кривые T5 до
  того, как их прочитают легенда, схема и список видимости.

Число передатчиков входит в ключ кэша inline-редактора наряду с
`ReferenceEquals` базовой конфигурации. Это обязательно: редактор создаётся до
загрузки данных, поэтому без этой проверки он навсегда оставался бы
пятипередатчиковым и давал активный T5 на схеме и лишний чекбокс T5 в списке
видимых кривых. Возможности стабильны при потоковом приёме, поэтому live-кадры
редактор не пересобирают и несохранённые инлайн-правки не сбрасывают; при
реальной смене числа передатчиков редактор пересобирается, а флаг pending
сбрасывается.

`SchematicConfigurationEditor.LoadExistingConfiguration` дополнительно
отбрасывает кривые неподдерживаемых передатчиков (`IsSupportedByCapabilities`),
поэтому ни inline-редактор, ни окно схемы не восстанавливают T5 из ранее
применённой конфигурации или из снимка заметки, построенного
`BuildFullCurveSet`.

Реализовано и build-verified; ручная визуальная проверка владельцем ожидается.

Не реализовано:

- LOD/downsampling и cache;
- отдельно выделенная общая geometry-модель для draw/hit-test;
- размещение частоты и выбора оси X на графике (следующий шаг по плану владельца).

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

### Минимальный размер главного окна

`Services/MinimumWindowSize.cs` не даёт сжать главное окно меньше 90% ширины и
80% высоты того монитора, на котором оно находится: панели управления и два
графика иначе не помещаются. Windows App SDK не имеет свойства минимального
размера, поэтому оконная процедура субклассится (`SetWindowSubclass`) и
`WM_GETMINMAXINFO` заполняется вручную. Предел пересчитывается на каждое
сообщение, поэтому перенос окна на другой монитор подхватывает его разрешение;
`WM_GETMINMAXINFO` и прямоугольник монитора используют одни единицы (физические
пиксели), пересчёт DPI не нужен. Делегат хранится в поле — субклass держит на
него нативный указатель. Максимизация не затрагивается: ограничивается только
`ptMinTrackSize`.

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

Релизные setup `0.1.7` и descriptor сформированы 4 августа 2026 года штатным
скриптом; имя файла и SHA-256 (`72B32794…DAE122A1`) совпали с независимо
пересчитанным хэшем, размер setup — 62 347 297 байт, версия EXE в payload —
`0.1.7.0`, все обязательные PRI/XBF проверены скриптом. Release payload успешно
запущен; установка setup не выполнялась. Публикация в HTTP-каталог, code signing
и полный цикл update/uninstall между версиями остаются за владельцем.

## 12. Реализовано частично или не начато

- Graph 1 range save/cut, append и segment-layout реализованы; ручная parity-проверка остаётся;
- «Документация» открывает `FilesForProgramVisualisation/Документация.pdf` в
  браузере по умолчанию (перенос legacy `OpenDocumentation`), при неудаче
  показывает сообщение; собственной встроенной документации по-прежнему нет;
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
