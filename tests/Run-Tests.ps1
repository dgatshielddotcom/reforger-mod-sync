[CmdletBinding()]
param(
    [switch]$Live,
    [switch]$Recycle
)

$ErrorActionPreference = 'Stop'
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
$sourceDirectory = Join-Path $repositoryDirectory 'src'
$testOutputDirectory = Join-Path $repositoryDirectory 'work\test-binaries'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}
New-Item -ItemType Directory -Path $testOutputDirectory -Force | Out-Null

function Run-CSharpTest {
    param([string]$Name, [string[]]$EngineSources, [string[]]$Arguments = @())
    $testExecutable = Join-Path $testOutputDirectory "$Name.exe"
    $testSources = @($EngineSources | ForEach-Object { Join-Path $sourceDirectory $_ })
    $testSources += Join-Path $PSScriptRoot "$Name.cs"
    & $compilerPath /nologo /target:exe /warn:4 "/out:$testExecutable" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll $testSources
    if ($LASTEXITCODE -ne 0) { throw "$Name compilation failed." }
    & $testExecutable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name failed." }
}

$engineArguments = @()
if ($Recycle) { $engineArguments += '--recycle' }
Run-CSharpTest -Name 'EngineTests' -EngineSources @('Models.cs', 'ModEngine.cs') -Arguments $engineArguments
Run-CSharpTest -Name 'ManualTests' -EngineSources @('Models.cs', 'ManualModList.cs')
$providerArguments = @(Join-Path $PSScriptRoot 'fixtures\server-detail.json')
if ($Live) { $providerArguments += '--live' }
Run-CSharpTest -Name 'ServerTests' -EngineSources @('Models.cs', 'ServerSource.cs') -Arguments $providerArguments
Write-Output 'All requested tests passed.'

