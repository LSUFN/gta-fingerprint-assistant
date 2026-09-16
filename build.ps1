[CmdletBinding()]
param(
    [ValidateSet("Release", "Test")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$outputDir = Join-Path $projectRoot "bin"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$references = "/reference:System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Web.Extensions.dll,System.Security.dll"

if ($Configuration -eq "Release") {
    & $compiler /nologo /target:winexe /optimize+ /unsafe+ $references "/out:$outputDir\FingerprintAssistant.exe" (Join-Path $projectRoot "Program.cs")
} else {
    & $compiler /nologo /target:exe /optimize+ /unsafe+ $references "/main:GtaCasinoAssistant.OfflineTestProgram" "/out:$outputDir\FingerprintAssistant.Tests.exe" (Join-Path $projectRoot "Program.cs") (Join-Path $projectRoot "OfflineTest.cs")
}

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

$templateOutput = Join-Path $outputDir "templates"
New-Item -ItemType Directory -Force -Path $templateOutput | Out-Null
Copy-Item -LiteralPath (Get-ChildItem (Join-Path $projectRoot "templates") -File |
    Where-Object { $_.Name -match '^target_[1-4]_(master|slice_[1-4])\.png$' } |
    Select-Object -ExpandProperty FullName) -Destination $templateOutput -Force

Write-Host "Built $Configuration output in $outputDir" -ForegroundColor Green
