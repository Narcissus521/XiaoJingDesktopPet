$ErrorActionPreference = 'Stop'
$petDirectory = $PSScriptRoot
$petCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $petCompiler)) { throw 'Windows .NET Framework 4.8 is required.' }
$petSources = @(Get-ChildItem -LiteralPath (Join-Path $petDirectory 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
& $petCompiler /nologo /nowarn:0649 /target:winexe /optimize+ /platform:anycpu /win32manifest:"$petDirectory\src\app.manifest" /win32icon:"$petDirectory\assets\pet.ico" /out:"$petDirectory\XiaoJingDesktopPet.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.Xml.dll @petSources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'Created XiaoJingDesktopPet.exe.'
