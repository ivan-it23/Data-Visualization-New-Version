# AMK Data Visualization

Актуальное состояние: **14 июля 2026 года**.

`AMK-Data-Visualization` — новая WinUI 3 / .NET 10 реализация приложения
`Amk-Horizon-New-Chart`. Цель переноса — сохранить поведение, форматы данных и
расчётные контракты старого Win32/C++ приложения, одновременно разделив код на
модули и обновив визуальное оформление.

Старый проект остаётся источником истины по поведению:

`C:\Users\Admin\Desktop\Amk-Horizon-New-Chart`

## Что уже работает

- загрузка `.bin`, `.biv` и `.DEV` в canonical `GpToolData`;
- `ToolDataRepository`, metadata/capabilities и экспорт диапазонов;
- один или два графика, linked/independent источник Graph 2;
- несколько кривых, левая и правая Y-оси, X по кадрам или температуре;
- сетка, legacy nice-step, целые подписи кадров и фиксированный числовой формат;
- немодальное окно «Схема прибора» для Graph 1 и Graph 2;
- комбинированный выбор T1..T5, R1/R2 и молчащих каналов;
- полный набор быстрых пресетов, перенесённый на текущем этапе;
- legacy-совместимые легенды быстрых и комбинированных конфигураций;
- поиск COM-портов, подключение 125000/8N1, polling, CRC16 и чтение команд
  `0x11`, `0x13`, `0x16`;
- запись live-кадров в новый canonical `.bin` и дозапись существующего файла;
- окно «Подключение к прибору» и сохранение его параметров в `%LOCALAPPDATA%`.

## Что пока не завершено

- метрологические вычисления УЭС/симметричных фаз «на компьютере»;
- остальные команды прибора, air calibration и журнал обмена;
- zoom, scroll, crosshair, координаты курсора и рабочие range-команды;
- заметки, sidecar-сегменты, конструктор файлов;
- reporting, installer и auto-update;
- полная ручная parity-проверка на наборе реальных файлов и физическом приборе.

Проекты `AMK.DataVisualization.Metrology` и
`AMK.DataVisualization.Reporting` сейчас являются архитектурными контейнерами и
не содержат прикладной реализации.

## Сборка

Требуется Windows и установленный .NET 10 SDK.

```powershell
cd C:\Users\Admin\Desktop\AMK-Data-Visualization
dotnet build .\AMK-Data-Visualization.slnx -c Debug -p:Platform=x64 --no-restore
```

Приложение unpackaged/self-contained, основной исполняемый файл после Debug-сборки:

```text
AMK-Data-Visualization\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AMK.DataVisualization.App.exe
```

Автоматизированных тестовых проектов в решении нет. Успешная сборка подтверждает
компиляцию, но не заменяет ручную проверку UI, реальных файлов и COM-прибора.

## Документация

- [`docs/AMK_DATA_VISUALIZATION_MIGRATION_HANDOFF.md`](docs/AMK_DATA_VISUALIZATION_MIGRATION_HANDOFF.md) — основной вход для новой сессии: архитектура, реализованные сценарии, ограничения и точные точки продолжения.
- [`docs/MIGRATION_PLAN.md`](docs/MIGRATION_PLAN.md) — долгосрочные этапы миграции и их текущий статус.
- [`docs/SESSION_TASKS.md`](docs/SESSION_TASKS.md) — компактный снимок последней сессии, ручные проверки и ближайшие задачи.

Для продолжения работы сначала прочитать handoff, затем `SESSION_TASKS.md`, после
чего проверить `git status --short` и только затем менять код.
