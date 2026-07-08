# AMK-Data-Visualization Migration Handoff

> Статус на 2026-07-08. Документ фиксирует контекст переноса логики из
> `C:\Users\Admin\Desktop\Amk-Horizon-New-Chart` в новый проект
> `C:\Users\Admin\Desktop\AMK-Data-Visualization`.
>
> В новой сессии желательно открыть рабочей папкой именно
> `C:\Users\Admin\Desktop\AMK-Data-Visualization`, потому что текущая сессия
> имеет право записи только в старый проект.

## Цель миграции

Новый проект должен воспроизвести поведение старого приложения без изменения
математики и пользовательской логики, но перенести ее в современную модульную
архитектуру WinUI/C#:

- `Core` - бинарные контракты, типы данных, capabilities, безопасные access-view.
- `IO` - чтение `.bin/.biv/.DEV`, repository-state, range export.
- `Hardware` - будущий COM/polling слой.
- `Metrology` - метрология и вычисления, связанные с прибором.
- `Charting` - расчеты серий, конфигурации кривых, оси, геометрия графиков.
- `Reporting` - будущие отчеты/экспорт.
- WinUI App - оболочка, открытие файлов, визуализация и взаимодействие.

Поведение старого C++ проекта считается источником истины. Любое расхождение
должно быть либо исправлено, либо явно задокументировано как намеренное решение.

## Что уже перенесено в новый проект

Состояние ниже зафиксировано по предыдущей рабочей сессии. Перед продолжением
нужно быстро перепроверить файлы и тесты в `AMK-Data-Visualization`.

### 1. Базовая структура решения

В новом проекте создана модульная структура:

- `AMK-Data-Visualization\AMK-Data-Visualization.csproj`
- `src\AMK.DataVisualization.Core`
- `src\AMK.DataVisualization.IO`
- `src\AMK.DataVisualization.Hardware`
- `src\AMK.DataVisualization.Metrology`
- `src\AMK.DataVisualization.Charting`
- `src\AMK.DataVisualization.Reporting`
- `tests\AMK.DataVisualization.Core.Tests`
- `tests\AMK.DataVisualization.IO.Tests`
- `tests\AMK.DataVisualization.Charting.Tests`

Документы нового проекта:

- `docs\MIGRATION_PLAN.md`
- `docs\SESSION_TASKS.md`

### 2. Core: бинарные контракты и semantic access

Перенесены ключевые контракты:

- `GpToolData`
- `MetrologyData`
- `BinaryLayoutConstants`
- `BinaryLayoutVerifier`
- `FileFormat`
- `DeviceType`
- `ToolDataKind`
- `ToolCapabilities`
- `ToolRecordMetadata`
- `ToolIdentity`
- `UniversalCanonicalAdapter`
- `ToolDataAdapterRegistry`
- `ParsedToolFrame`
- `ToolDataAccessView`
- `MeasurementView`

Важные инварианты:

- canonical payload остается `240` байт.
- `.DEV` имеет физический кадр `251` байт: `11` байт заголовка + `240` байт payload.
- Семантический доступ к данным должен идти через access-view, а не через
  произвольные индексы массивов.

Основные C++ источники:

- [`ToolDataTypes.h`](../Amk-Horizon-New-Chart/ToolDataTypes.h#L6) - `GP_TOOL_DATA`.
- [`ToolDataAdapters.h`](../Amk-Horizon-New-Chart/ToolDataAdapters.h#L10) - `ParsedToolFrame`, `UniversalCanonicalAdapter`, registry.
- [`ToolDataAdapters.cpp`](../Amk-Horizon-New-Chart/ToolDataAdapters.cpp#L5) - registry singleton и парсинг.
- [`ToolDataAccess.h`](../Amk-Horizon-New-Chart/ToolDataAccess.h#L10) - `ToolDataAccessView`.
- [`ToolDataAccess.h`](../Amk-Horizon-New-Chart/ToolDataAccess.h#L129) - `MeasurementView`.

### 3. IO: загрузка файлов и repository-state

Перенесена загрузка `.bin/.biv/.DEV`:

- `.bin/.biv`: физический кадр `240` байт, signature с offset `0`.
- `.DEV`: физический кадр `251` байт, payload с offset `11`.
- Результат загрузки материализуется в canonical `GpToolData` и
  `ToolRecordMetadata`.

Перенесен repository-state:

- `ApplyLoadedData`
- `ClearData`
- `AddData`
- `AddDataBatch`
- `AddParsedData`
- `DataVersion`
- имя загруженного файла
- доступ к records и metadata
- summary по диапазонам metadata

Основные C++ источники:

- [`DataModule.h`](../Amk-Horizon-New-Chart/DataModule.h#L103) - public API загрузки.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L548) - `LoadDataFromFile`.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L568) - `LoadDataFromFileResult`.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L259) - `ParseRawFrameDetailed`.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L763) - `GetMeasurementView`.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L777) - `GetDataView`.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L782) - `AddParsedData`.

### 4. IO: range export

Перенесены:

- `SaveDataRange` - сохраняет выбранный диапазон как canonical `.bin`.
- `SaveFileExcludingRange` - копирует исходные физические кадры и сохраняет
  container layout, включая `.DEV` header/frame shape.

Основные C++ источники:

- [`DataModule.h`](../Amk-Horizon-New-Chart/DataModule.h#L138) - public API range export.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L806) - `SaveDataRange`.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L832) - `SaveFileExcludingRange`.

### 5. Charting: первый слой расчета серий

Перенесен первый слой `ChartDataCalculator`:

- `PhaseDifference`
- `AmplitudeRatio`
- `AmplitudeRx1`
- `AmplitudeRx2`
- `PhaseSmt`
- `RhoSmt`
- silent amplitude RX1/RX2
- silent phase difference
- `Temperature`

Ключевые правила:

- X-alignment сохраняется через `NaN` для отсутствующих значений.
- Нормализация фазы соответствует старому `NormalizePhase`.
- Перевод нормализованной фазы в millidegrees использует коэффициент
  `57295.7795`.
- Для `AmplitudeRatio` сохранен epsilon `1e-10f`, как в старом derived-cache пути.
- Количество transmitter-серий определяется по metadata/capabilities, fallback - `5`.

Основные C++ источники:

- [`DataModule.h`](../Amk-Horizon-New-Chart/DataModule.h#L142) - `CalculateChartData` overloads.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L907) - общий entrypoint `CalculateChartData`.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L919) - расчет по enabled transmitters и диапазонам.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L1329) - `CalculateRatioData`.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L1370) - `CalculateDifferenceData`.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L1567) - `GetTemperatureData`.

### 6. Charting: слой конфигурации кривых

Перенесены:

- `CurveDefinition`
- `ChartConfiguration`
- `ElementType`
- `XAxisType`
- `SmtComputationMode`
- `PresetType`
- `UpdateAxisConfiguration`
- `GroupCurvesByAxis`
- `CalculateSingleCurveData`
- `CalculateCustomCurveData`
- `CalculateChartDataFromConfiguration`

Ключевые правила:

- disabled curves пропускаются.
- `yAxisGroup == 0` идет в левую ось, `yAxisGroup == 1` - в правую ось.
- остальные `yAxisGroup` игнорируются.
- порядок кривых внутри оси сохраняется.
- `valueScale` применяется после расчета.
- в конфигурационном `CalculateSingleCurveData` только `RHO_SMT` и `TEMPERATURE` удаляют значения вне `minY/maxY`; остальные типы сохраняют aligned-ряд.

### 7. Проверка на реальных данных

Для IO и Charting использовались реальные файлы из:

`C:\Users\Admin\Desktop\AMK_Data`

Последняя зафиксированная проверка:

- `dotnet build` нового решения: успешно, `0 warnings / 0 errors`.
- `AMK.DataVisualization.Core.Tests`: успешно, `GpToolData=240; MetrologyData=240`.
- `AMK.DataVisualization.IO.Tests`: успешно, real-data scan прошел: `files=163; records=25307; bin=163; biv=0; dev=0`.
- `AMK.DataVisualization.Charting.Tests`: успешно, включая synthetic configuration tests и real-data smoke.

### 8. Первая WinUI-вертикаль

Реализовано:

- `CommandBar` с действием `Open`.
- `FileOpenPicker` для `.bin`, `.biv`, `.DEV`.
- загрузка выбранного файла через `ToolDataRepository`.
- построение default `ChartConfiguration` по старому дефолту `CurveDefinition`: `AmplitudeRx1`, `T1`, `400 kHz`, `mV`.
- расчет ряда через `ChartDataCalculator.CalculateChartDataFromConfiguration`.
- отображение одного графика через `LineChartControl`.
- панель источника показывает имя файла, количество записей, signature и тип прибора.

Проверено:

- build: успешно, `0 warnings / 0 errors`.
- Core/IO/Charting smoke tests: успешно.
- WinUI launch: успешно, окно `AMK Data Visualization` открывается и отвечает.

Не зафиксировано автоматизированно:

- ручной выбор реального файла через picker и визуальное сравнение с текущим C++ приложением.

## Следующий шаг переноса

Следующий шаг: зафиксировать выбор реального файла через WinUI picker, затем переносить graph data source для одного/двух графиков:

`Open file -> ToolDataRepository -> ChartConfiguration -> ChartDataCalculator -> one rendered chart`

После этого переносить graph 2 linked/independent mode, UI-обвязку выбора кривых, zoom, координаты курсора и parity-сравнение с текущим приложением.

## Уже использованные C++ участки для ChartConfiguration

### CurveDefinition

Источник: [`SchematicModule.h`](../Amk-Horizon-New-Chart/SchematicModule.h#L35)

Поля, которые нужно сохранить:

- `id`
- `displayName`
- `units`
- `yAxisGroup`
- `enabled`
- `color`
- `lineStyle`
- `sourceElement`
- `elementIndex`
- `dataType`
- `frequency`
- `smtMode`
- `valueScale`

Конструкторы:

- [`SchematicModule.cpp`](../Amk-Horizon-New-Chart/SchematicModule.cpp#L300) - default constructor.
- [`SchematicModule.cpp`](../Amk-Horizon-New-Chart/SchematicModule.cpp#L309) - constructor with id/name/units/source/type/frequency.

Default values:

- `yAxisGroup = 0`
- `enabled = false`
- `sourceElement = TRANSMITTER`
- `elementIndex = 0`
- `dataType = AMPLITUDE_RX1`
- `frequency = FREQ_400_KHZ`
- `smtMode = FROM_SONDE`
- `valueScale = 1.0f`

### ChartConfiguration

Источник: [`SchematicModule.h`](../Amk-Horizon-New-Chart/SchematicModule.h#L114)

Поля, которые нужно сохранить:

- `curves`
- `hasDualAxis`
- `leftAxisUnits`
- `rightAxisUnits`
- `xAxisType`
- `activePreset`
- `presetDisplayName`

Методы:

- [`SchematicModule.cpp`](../Amk-Horizon-New-Chart/SchematicModule.cpp#L390) - constructor.
- [`SchematicModule.cpp`](../Amk-Horizon-New-Chart/SchematicModule.cpp#L396) - `Clear`.
- [`SchematicModule.cpp`](../Amk-Horizon-New-Chart/SchematicModule.cpp#L406) - `IsEmpty`.
- [`SchematicModule.cpp`](../Amk-Horizon-New-Chart/SchematicModule.cpp#L410) - `Copy`.
- [`SchematicModule.cpp`](../Amk-Horizon-New-Chart/SchematicModule.cpp#L422) - `AddCurve`.
- [`SchematicModule.cpp`](../Amk-Horizon-New-Chart/SchematicModule.cpp#L427) - `RemoveCurve`.
- [`SchematicModule.cpp`](../Amk-Horizon-New-Chart/SchematicModule.cpp#L434) - `UpdateAxisConfiguration`.

Поведение `UpdateAxisConfiguration`:

- disabled curves пропускаются.
- `yAxisGroup == 0` включает левую ось.
- `yAxisGroup == 1` включает правую ось.
- `leftAxisUnits` и `rightAxisUnits` берутся с первой включенной кривой
  соответствующей оси.
- `hasDualAxis = hasLeftAxis && hasRightAxis`.
- units неиспользуемых осей очищаются.

### GroupCurvesByAxis

Источник: [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L1307)

Правила:

- output списки очищаются.
- disabled curves пропускаются.
- `yAxisGroup == 0` идет в left axis.
- `yAxisGroup == 1` идет в right axis.
- остальные значения игнорируются.

### CalculateCustomCurveData

Источники:

- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L1148) - overload без диапазонов.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L1162) - overload с `minX/maxX/minY/maxY`.

Правила:

- `leftAxisData` и `rightAxisData` очищаются.
- если данных нет или список кривых пустой - return.
- кривые группируются через `GroupCurvesByAxis`.
- данные левой оси рассчитываются в порядке `leftAxisCurves`.
- данные правой оси рассчитываются в порядке `rightAxisCurves`.
- каждая кривая рассчитывается через `CalculateSingleCurveData`.

### CalculateSingleCurveData

Источники:

- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L1190) - overload без диапазонов.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L1201) - overload с диапазонами.

Dispatch по `curve.dataType`:

- `AMPLITUDE_RATIO` -> `CalculateRatioData(curve.elementIndex, curve.frequency, ...)`
- `PHASE_DIFFERENCE` -> `CalculateDifferenceData(curve.elementIndex, curve.frequency, ...)`
- `AMPLITUDE_RX1` -> `CalculateAmplitudeData(curve.elementIndex, 0, curve.frequency, ...)`
- `AMPLITUDE_RX2` -> `CalculateAmplitudeData(curve.elementIndex, 1, curve.frequency, ...)`
- `PHASE_SMT` -> `CalculatePhaseData(curve.elementIndex, 0, curve.frequency, ...)`
- `RHO_SMT` -> direct `TryGetRhoSmt(...)` path
- `SILENT_AMPLITUDE_RX1` -> `CalculateSilentAmplitudeData(0, curve.frequency, ...)`
- `SILENT_AMPLITUDE_RX2` -> `CalculateSilentAmplitudeData(1, curve.frequency, ...)`
- `SILENT_PHASE_DIFFERENCE` -> `CalculateSilentPhaseDifferenceData(curve.frequency, ...)`
- `TEMPERATURE` -> `GetTemperatureData(...)` + `minY/maxY` filter

Совместимость, которую нельзя потерять:

- Для большинства серий Y-фильтр не применяется в `CalculateSingleCurveData`;
  используются helper-методы, которые сохраняют X-alignment.
- Для `RHO_SMT` и `TEMPERATURE` значения вне `minY/maxY` не заменяются `NaN`,
  а удаляются из выходного массива.
- После расчета применяется `curve.valueScale`, если он не равен `1.0f`.
- `NaN * valueScale` остается `NaN`; это ожидаемое поведение.

### CalculateChartDataFromConfiguration

Источники:

- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L1536) - overload без диапазонов.
- [`DataModule.cpp`](../Amk-Horizon-New-Chart/DataModule.cpp#L1550) - overload с диапазонами.

Правила:

- очищает axis data.
- если данных нет или `config.curves.empty()` - return.
- делегирует в `CalculateCustomCurveData(config.curves, ...)`.

## Следующие блоки после ChartConfiguration

### Presets и CurveSelectionModule

Переносить после базового `ChartConfiguration`:

- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L871) - `CreatePresetConfiguration`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L919) - `BuildFullCurveSet`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1021) - `ValidateConfiguration`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1070) - `CalculateCurveData`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1197) - zoom-aware `CalculateCurveData`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1618) - silent amplitude curve factory.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1631) - silent phase curve factory.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1651) - `GetCurveColorForAxis`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1747) - `RefreshCurveColors`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1786) - `CreateAllAmplitudesR1Preset`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1805) - `CreateAllAmplitudesR2Preset`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1824) - `CreateAmplitudeRatiosPreset`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1843) - `CreatePhaseDifferencesPreset`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1863) - `CreateRhoSmtPreset`.
- [`CurveSelectionModule.cpp`](../Amk-Horizon-New-Chart/CurveSelectionModule.cpp#L1872) - `CreatePhaseSmtPreset`.

### ChartManager / ChartModule rendering pipeline

Переносить после расчетов и preset слоя:

- [`ChartManager.cpp`](../Amk-Horizon-New-Chart/ChartManager.cpp#L853) - cache invalidation.
- [`ChartManager.cpp`](../Amk-Horizon-New-Chart/ChartManager.cpp#L1045) - curve snapshot cache.
- [`ChartManager.cpp`](../Amk-Horizon-New-Chart/ChartManager.cpp#L1100) - temperature axis cache.
- [`ChartManager.cpp`](../Amk-Horizon-New-Chart/ChartManager.cpp#L1914) - `LoadDataFromFile`.
- [`ChartManager.cpp`](../Amk-Horizon-New-Chart/ChartManager.cpp#L4077) - `ApplyCurveConfiguration`.
- [`ChartManager.cpp`](../Amk-Horizon-New-Chart/ChartManager.cpp#L4092) - `GetChartConfiguration`.
- [`ChartModule.cpp`](../Amk-Horizon-New-Chart/ChartModule.cpp#L1420) - dual-axis chart draw path.
- [`ChartModule.cpp`](../Amk-Horizon-New-Chart/ChartModule.cpp#L2188) - dual-axis legend.
- [`ChartModule.cpp`](../Amk-Horizon-New-Chart/ChartModule.cpp#L3002) - temperature-X dual-axis overload.
- [`ChartModule.cpp`](../Amk-Horizon-New-Chart/ChartModule.cpp#L3133) - temperature grid.
- [`ChartModule.cpp`](../Amk-Horizon-New-Chart/ChartModule.cpp#L3386) - temperature data drawing.

## Тестовое покрытие ChartConfiguration

После переноса `ChartConfiguration` добавлены проверки в
`tests\AMK.DataVisualization.Charting.Tests`:

1. `ChartConfiguration.UpdateAxisConfiguration`:
   - disabled curves игнорируются.
   - left/right units берутся с первой включенной кривой.
   - `hasDualAxis` выставляется только при наличии обеих осей.
   - units очищаются при отсутствии соответствующей оси.

2. `GroupCurvesByAxis`:
   - порядок кривых сохраняется.
   - disabled curves игнорируются.
   - неизвестный `yAxisGroup` игнорируется.

3. `CalculateSingleCurveData`:
   - dispatch по всем уже перенесенным `DataType`.
   - `valueScale` применяется после расчета.
   - `NaN` сохраняется при масштабировании.
   - `TEMPERATURE` и `RHO_SMT` используют старое поведение с удалением точек
     вне `minY/maxY`.

4. `CalculateChartDataFromConfiguration`:
   - левая/правая ось получают массивы в порядке сгруппированных кривых.
   - пустые данные и пустой config возвращают пустые axis data.

5. Real-data smoke:
   - использовать `C:\Users\Admin\Desktop\AMK_Data`.
   - проверить минимум один `.bin` файл с несколькими curves и двумя осями.

Следующий тестовый блок нужен уже для первой UI-вертикали: открыть файл, заполнить `ToolDataRepository`, построить конфигурационный ряд и отрисовать один график.

## Команды проверки

В новой сессии из `C:\Users\Admin\Desktop\AMK-Data-Visualization`:

```powershell
dotnet build .\AMK-Data-Visualization.slnx -p:Platform=x64 -p:UseSharedCompilation=false
dotnet run --project .\tests\AMK.DataVisualization.Core.Tests\AMK.DataVisualization.Core.Tests.csproj
dotnet run --project .\tests\AMK.DataVisualization.IO.Tests\AMK.DataVisualization.IO.Tests.csproj
dotnet run --project .\tests\AMK.DataVisualization.Charting.Tests\AMK.DataVisualization.Charting.Tests.csproj
```

Если меняется WinUI startup или визуальная вертикаль, после build нужно реально
запустить приложение и подтвердить окно `AMK Data Visualization`, а не считать
один только build полной проверкой.

## Правила ведения документации при переносе

В новом проекте после каждого шага обновлять:

- `docs\MIGRATION_PLAN.md` - долгосрочный план и статус этапов.
- `docs\SESSION_TASKS.md` - текущие задачи, что сделано в этой сессии, что
  осталось, какие проверки прошли.

В документации всегда разделять:

- уже реализовано;
- реализовано, но не проверено на реальных данных;
- build-verified;
- runtime-verified;
- осталось перенести.

Нельзя заменять source-backed поведение предположениями. Если логика кажется
странной, сначала найти соответствующий C++ участок и перенести поведение
точь-в-точь, затем отдельно обсуждать возможное улучшение.
