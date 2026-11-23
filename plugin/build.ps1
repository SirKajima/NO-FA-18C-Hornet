# Builds the BepInEx plugin with any C# compiler that runs on .NET (Unity's bundled Roslyn works).
# Usage (PowerShell, from this folder):
#   .\build.ps1 -NuclearOptionDir "D:\Steam\steamapps\common\Nuclear Option" -Csc "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Data\DotNetSdkRoslyn\csc.dll" -Dotnet "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Data\NetCoreRuntime\dotnet.exe"
param(
  [Parameter(Mandatory=$true)][string]$NuclearOptionDir,
  [Parameter(Mandatory=$true)][string]$Csc,
  [Parameter(Mandatory=$true)][string]$Dotnet
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$nod = $NuclearOptionDir.TrimEnd('\','/') -replace '\\','/'
(Get-Content build.rsp.template -Raw).Replace('<NUCLEAR_OPTION_DIR>', $nod) | Set-Content build.rsp -NoNewline
& $Dotnet $Csc "@build.rsp"
