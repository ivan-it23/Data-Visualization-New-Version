# AMK Data Visualization — оперативный снимок

Последнее обновление: **19 июля 2026 года**.

## Назначение

Этот файл — короткий вход в новую сессию. Подробные контракты находятся в
`AMK_DATA_VISUALIZATION_MIGRATION_HANDOFF.md`, долгосрочный маршрут — в
`MIGRATION_PLAN.md`.

## Текущее состояние

- WinUI 3 / .NET 10 / x64;
- unpackaged, Windows App SDK self-contained;
- пользовательское имя «Визуализация данных»;
- executable `DataVisualization.exe`;
- версия `0.1.1` берётся из `AMK-Data-Visualization.csproj`;
- all-users installer Inno Setup 6;
- автоматизированных тестовых проектов нет;
- перед работой обязательно выполнить `git status --short` и сохранить все
  пользовательские изменения.

## Подтверждённый функционал

- чтение `.bin/.biv/.DEV` и построение нескольких series;
- Graph 1/Graph 2, linked/independent источник второго графика;
- окно «Схема прибора», комбинированные кривые и быстрые пресеты;
- X Range, drag-zoom, undo/reset, X/Y-scroll и `Поток`;
- автоматический Y-fit видимого X-участка с 5%-м padding;
- zoom/scroll/автомасштабирование проверены владельцем;
- окно подключения, базовый COM live и canonical recording;
- базовый live-приём `Картограф LWD 4tx` и Q12-нормализация;
- окно метрологии, 240-байтовые файлы и расчётная ветка `FromComputer`;
- сохранение и вырезание диапазона Graph 1, включая append-варианты;
- конструктор файлов, `%LOCALAPPDATA%`-описания сегментов и границы сегментов на графиках;
- сборка и запуск установленного приложения версии `0.1.1` через setup и
  ярлыки подтверждены владельцем;
- ручная и фоновая проверка обновлений реализованы.

## Не считать завершённым

- полный parity всех кривых на контрольных файлах;
- численный parity `FromSonde` / `FromComputer`;
- полный COM reconnect и метрологические команды на физическом приборе;
- LOD/cache и единая формализованная geometry-модель;
- пользовательское окно логов и встроенная документация;
- reporting;
- code signing и полный install/update/uninstall release-цикл.

## Актуальное техническое ограничение

`DeviceProtocolLog` ищет корень решения и при developer-запуске пишет
`CartographLwd4Tx.log` туда. В установленной копии корень решения отсутствует,
поэтому fallback указывает на каталог приложения в Program Files. Запись может
быть отклонена правами и молча пропущена. Следующее изменение логирования должно
перенести установленный runtime-лог в `%LOCALAPPDATA%`.

## Рекомендуемый следующий блок

При отсутствии другой пользовательской задачи:

1. parity `FromSonde` / `FromComputer` на одном реальном файле и метрологии;
2. полный повторный COM-тест `Картограф LWD 4tx`;
3. перенос лога в `%LOCALAPPDATA%` и немодальное окно журнала;
4. общая geometry-модель и LOD/cache;
5. reporting и release-проверка.

## Начало новой сессии

```powershell
cd C:\Users\Admin\Desktop\AMK-Data-Visualization
git status --short
dotnet build .\AMK-Data-Visualization.slnx -c Debug -p:Platform=x64 --no-restore
```

Порядок чтения:

1. `docs/AMK_DATA_VISUALIZATION_MIGRATION_HANDOFF.md`;
2. этот файл;
3. `docs/MIGRATION_PLAN.md` при изменении статуса этапа;
4. исходники конкретной вертикали;
5. legacy-код перед переносом поведения.

Build-успех не следует записывать как runtime/parity-успех.
