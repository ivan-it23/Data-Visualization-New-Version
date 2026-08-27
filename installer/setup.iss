; ============================================================================
; «Визуализация данных» — all-users Inno Setup installer
; ============================================================================

#define MyAppId          "{{BC8F93C7-591A-4A5B-8A3B-D298E5201D2B}"
#define MyAppName        "Визуализация данных"
#define MyAppExeName     "DataVisualization.exe"
#define MyAppIconName    "DataVisualization.ico"
#define MyAppPublisher   'ООО НПФ "АМК ГОРИЗОНТ"'
#define MyAppDescription "Приложение для визуализации и анализа данных геофизических приборов"
#define MyAppCopyright   'Copyright (C) 2026 ООО НПФ "АМК ГОРИЗОНТ"'
#define PublishDir       "..\artifacts\publish\win-x64"
#define AppIconPath      "..\AMK-Data-Visualization\Assets\DataVisualization.ico"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.2"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright={#MyAppCopyright}
AppUpdatesURL=http://ivanbohg.beget.tech/FilesForProgramVisualisation/

; Per-machine install: приложение и ярлыки доступны всем пользователям.
PrivilegesRequired=admin
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowNoIcons=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Не 10.0: Windows App SDK требует Windows 10 1809 (сборка 17763), и это же
; значение стоит в TargetPlatformMinVersion проекта. С прежним 10.0 установка
; на более раннюю сборку проходила, а приложение потом не запускалось.
MinVersion=10.0.17763

CloseApplications=force
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no

OutputDir=Output
OutputBaseFilename=DataVisualization-Setup-{#MyAppVersion}
SetupIconFile={#AppIconPath}
Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes

WizardStyle=modern
ShowLanguageDialog=auto
SetupLogging=yes
Uninstallable=yes
UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExeName}

VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright={#MyAppCopyright}
VersionInfoDescription=Установщик приложения «{#MyAppName}»

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startmenuicon"; Description: "Создать ярлык в меню «Пуск»"; GroupDescription: "Ярлыки приложения:"; Flags: checkedonce
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки приложения:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#AppIconPath}"; DestDir: "{app}\Assets"; DestName: "{#MyAppIconName}"; Flags: ignoreversion

[Icons]
Name: "{commonprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\{#MyAppIconName}"; Comment: "{#MyAppDescription}"; Tasks: startmenuicon
Name: "{commonprograms}\{#MyAppName}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\{#MyAppIconName}"; Comment: "{#MyAppDescription}"; Tasks: desktopicon

[Run]
; Обычная установка: мастер предлагает запуск через checkbox.
; runasoriginaluser обязателен в обеих строках. Установщик работает с правами
; администратора, и без этого флага приложение наследует его токен: окно
; запущенного так приложения не принимает перетаскивание файлов из Проводника,
; а при разных учётных записях пользователя и администратора настройки и журналы
; уходят в профиль администратора, а не того, кто работает.
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; AutoUpdate запускает setup с /UPDATE=1: после тихого обновления приложение
; стартует заново без дополнительного пользовательского шага.
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Flags: nowait runasoriginaluser; Check: IsAutoUpdateMode

[Code]
function IsAutoUpdateMode: Boolean;
begin
  Result := CompareText(ExpandConstant('{param:update|0}'), '1') = 0;
end;
