' Roda scripts/poka-yoke/gate.ps1 sem janela e devolve o exit code (issue 2922).
' wscript.exe e GUI: nao cria console, entao nao rouba o foco. Saida em .build\gate-ultimo.log.
Dim sh, fso, raiz, log, cmd
Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
raiz = fso.GetParentFolderName(fso.GetParentFolderName(fso.GetParentFolderName(WScript.ScriptFullName)))
sh.CurrentDirectory = raiz
If Not fso.FolderExists(raiz & "\.build") Then fso.CreateFolder raiz & "\.build"
log = raiz & "\.build\gate-ultimo.log"
cmd = "cmd /d /c powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\poka-yoke\gate.ps1 > """ & log & """ 2>&1"
WScript.Quit sh.Run(cmd, 0, True)
