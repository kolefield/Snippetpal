Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "FileFunc.nsh"
!include "WordFunc.nsh"

Name "Snippetpal"
OutFile "${OutputFile}"
InstallDir "$LOCALAPPDATA\Programs\Snippetpal"
InstallDirRegKey HKCU "Software\Snippetpal\Installer" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetOverwrite on
ShowInstDetails show
ShowUninstDetails show
Icon "${SourceRoot}\src\Snippetpal\Assets\Snippetpal.ico"
UninstallIcon "${SourceRoot}\src\Snippetpal\Assets\Snippetpal.ico"
VIProductVersion "${AppVersion}.0"
VIAddVersionKey /LANG=1033 "ProductName" "Snippetpal"
VIAddVersionKey /LANG=1033 "CompanyName" "kolefield"
VIAddVersionKey /LANG=1033 "FileDescription" "Snippetpal installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${AppVersion}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright 2026"

!define UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\Snippetpal"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\Snippetpal.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

Function .onInit
    SetShellVarContext current
    SetRegView 64
    ${IfNot} ${RunningX64}
        MessageBox MB_OK|MB_ICONSTOP "Snippetpal requires 64-bit Windows." /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
    ${IfNot} ${AtLeastWin10}
        MessageBox MB_OK|MB_ICONSTOP "Snippetpal requires Windows 10 or later." /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
    ReadRegStr $0 HKCU "${UninstallKey}" "DisplayVersion"
    ${If} $0 != ""
        ${VersionCompare} $0 "${AppVersion}" $1
        ${If} $1 == 1
            MessageBox MB_OK|MB_ICONSTOP "A newer version of Snippetpal is already installed." /SD IDOK
            SetErrorLevel 2
            Abort
        ${EndIf}
    ${EndIf}
FunctionEnd

!macro CheckAppClosed Prefix
Function ${Prefix}CheckAppClosed
    IfFileExists "$INSTDIR\Snippetpal.exe" 0 ready
    ClearErrors
    FileOpen $0 "$INSTDIR\Snippetpal.exe" a
    IfErrors locked
    FileClose $0
    Goto ready
locked:
    MessageBox MB_OK|MB_ICONSTOP "Please quit Snippetpal from its system tray menu, then try again. Also check that you have permission to change the install folder." /SD IDOK
    SetErrorLevel 2
    Abort
ready:
FunctionEnd
!macroend
!insertmacro CheckAppClosed ""
!insertmacro CheckAppClosed "un."

Section "Snippetpal" Main
    Call CheckAppClosed
    SetOutPath "$INSTDIR"
    File "${PublishDir}\Snippetpal.exe"
    File "${PublishDir}\README.md"
    File "${PublishDir}\THIRD-PARTY-NOTICES.md"
    SetOutPath "$INSTDIR\notices"
    File "${PublishDir}\notices\*"
    SetOutPath "$INSTDIR"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateShortcut "$SMPROGRAMS\Snippetpal.lnk" "$INSTDIR\Snippetpal.exe"
    WriteRegStr HKCU "Software\Snippetpal\Installer" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UninstallKey}" "DisplayName" "Snippetpal"
    WriteRegStr HKCU "${UninstallKey}" "DisplayVersion" "${AppVersion}"
    WriteRegStr HKCU "${UninstallKey}" "Publisher" "kolefield"
    WriteRegStr HKCU "${UninstallKey}" "DisplayIcon" "$INSTDIR\Snippetpal.exe"
    WriteRegStr HKCU "${UninstallKey}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UninstallKey}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "${UninstallKey}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
    WriteRegStr HKCU "${UninstallKey}" "URLInfoAbout" "https://github.com/kolefield/Snippetpal"
    WriteRegDWORD HKCU "${UninstallKey}" "NoModify" 1
    WriteRegDWORD HKCU "${UninstallKey}" "NoRepair" 1
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    WriteRegDWORD HKCU "${UninstallKey}" "EstimatedSize" $0
SectionEnd

Function un.onInit
    SetShellVarContext current
    SetRegView 64
    Call un.CheckAppClosed
FunctionEnd

Section "Uninstall"
    Delete "$SMPROGRAMS\Snippetpal.lnk"
    Delete "$INSTDIR\Snippetpal.exe"
    Delete "$INSTDIR\README.md"
    Delete "$INSTDIR\THIRD-PARTY-NOTICES.md"
    Delete "$INSTDIR\notices\microsoft.netcore.app.runtime.win-x64-LICENSE.TXT"
    Delete "$INSTDIR\notices\microsoft.netcore.app.runtime.win-x64-THIRD-PARTY-NOTICES.TXT"
    Delete "$INSTDIR\notices\microsoft.windowsdesktop.app.runtime.win-x64-LICENSE.TXT"
    Delete "$INSTDIR\notices\microsoft.windowsdesktop.app.runtime.win-x64-THIRD-PARTY-NOTICES.TXT"
    Delete "$INSTDIR\notices\NSIS-COPYING.txt"
    RMDir "$INSTDIR\notices"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
    DeleteRegKey HKCU "${UninstallKey}"
    DeleteRegKey HKCU "Software\Snippetpal\Installer"
    DeleteRegKey /ifempty HKCU "Software\Snippetpal"
    ; The user's library in $LOCALAPPDATA\Snippetpal is intentionally retained.
SectionEnd
