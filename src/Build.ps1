$ErrorActionPreference = 'Stop'
$sourceDirectory = $PSScriptRoot
$applicationDirectory = Split-Path -Parent $sourceDirectory
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}
$sources = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' | ForEach-Object { $_.FullName })
$binaryPath = Join-Path $applicationDirectory 'ReforgerModSync.exe'
$manifestPath = Join-Path $sourceDirectory 'app.manifest'
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 "/out:$binaryPath" "/win32manifest:$manifestPath" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built $binaryPath"
