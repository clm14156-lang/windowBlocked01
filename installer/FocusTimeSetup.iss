#define ServiceDirectory "{commonpf64}\FocusApp Service"

[Setup]
AppId={{9ED4ACF8-F19E-4DFD-863D-38E7CA3DEA62}
AppName=专注时光
AppVersion={#AppReleaseVersion}
AppVerName=专注时光 {#AppReleaseVersion}
DefaultDirName={commonpf64}\FocusApp
DefaultGroupName=专注时光
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\FocusTime.ico
UninstallFilesDir={#ServiceDirectory}
PrivilegesRequired=admin
SetupArchitecture=x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
SetupMutex=Global\FocusApp.Setup
UsePreviousAppDir=yes
DisableWelcomePage=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableFinishedPage=no
DisableStartupPrompt=yes
AllowUNCPath=no
AllowNetworkDrive=no
RedirectionGuard=yes
CloseApplications=no
RestartApplications=no
SetupLogging=yes
WizardStyle=modern light hidebevels
WizardSizePercent=100,112
OutputDir={#DistributionDir}
OutputBaseFilename=FocusTime-Setup-{#AppReleaseVersion}-win-x64
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#AppLogo}"; DestName: "FocusTimeLogo.png"; Flags: dontcopy
Source: "{#InstallerHelpersDir}\InstallerLifecycle.ps1"; Flags: dontcopy
Source: "{#InstallerHelpersDir}\FocusAppInstallDirectory.ps1"; Flags: dontcopy
Source: "{#PayloadDir}\FocusTime.InstallManifest.txt"; DestName: "FocusTime.ClientFiles.txt"; Flags: dontcopy
Source: "{#RegisterServiceScript}"; Flags: dontcopy
Source: "{#ServiceArchive}"; Flags: dontcopy nocompression
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "FocusTime.InstallManifest.txt"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\FocusTime.InstallManifest.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#InstallerHelpersDir}\*.ps1"; DestDir: "{#ServiceDirectory}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\专注时光"; Filename: "{app}\FocusApp.Agent.exe"; Parameters: "--open"; WorkingDir: "{app}"; IconFilename: "{app}\FocusTime.ico"
Name: "{autodesktop}\专注时光"; Filename: "{app}\FocusApp.Agent.exe"; Parameters: "--open"; WorkingDir: "{app}"; IconFilename: "{app}\FocusTime.ico"; Check: CreateDesktopShortcut

[Code]
#include "includes\DirectoryHandles.iss"
#include "includes\Wizard.iss"
#include "includes\Lifecycle.iss"
