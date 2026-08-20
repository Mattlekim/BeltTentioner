; Inno Setup script for the Belt Tensioner app.
; Build via ..\build-installer.ps1 (it publishes the app first and passes
; AppVersion / SourceDir defines), or manually:
;   ISCC.exe /DAppVersion=1.0.0 /DSourceDir=..\publish\BeltTensioner BeltTensioner.iss
;
; Per-user install (no admin prompt): the app's OpenXR layer registration is
; HKCU-only, so a per-user install keeps everything consistent. The layer is
; also pre-registered here so the overlay works even before first launch, and
; the uninstaller removes the registration again.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\BeltTensioner"
#endif

#define AppName "Belt Tensioner"
#define AppExeName "BeltTensionTest.WPF.exe"

[Setup]
; Never change this AppId — it is how upgrades find the existing install.
AppId={{843AD397-8D7B-4043-9EFE-8156AF2DF457}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=mattlekim
DefaultDirName={autopf}\BeltTensioner
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\publish
OutputBaseFilename=BeltTensionerSetup-{#AppVersion}
SetupIconFile=..\BeltTensionTest.WPF\Assets\Icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Close a running copy before installing, restart it after.
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Register the bundled MonoXR OpenXR implicit layer for this user (DWORD 0 =
; enabled). The app re-asserts this on every startup (MonoXRLayerInstaller),
; but registering here means VR overlays work even before first launch, and
; uninsdeletevalue guarantees the uninstaller cleans it up.
Root: HKCU; Subkey: "Software\Khronos\OpenXR\1\ApiLayers\Implicit"; ValueType: dword; ValueName: "{app}\MonoXR.json"; ValueData: 0; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
