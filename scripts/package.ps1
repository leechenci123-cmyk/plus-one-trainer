param([string]$Version = '1.0.0-beta.4')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repositoryRoot 'artifacts\publish\win-x86'
$packageRoot = Join-Path $repositoryRoot "artifacts\package\Plus-One-Trainer-$Version-win-x86"
$zipPath = "$packageRoot.zip"

Push-Location $repositoryRoot
try {
    if (Test-Path -LiteralPath $publishRoot) { Remove-Item -LiteralPath $publishRoot -Recurse -Force }
    if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
    dotnet publish src\PlusOneTrainer\PlusOneTrainer.csproj `
        -c Release -r win-x86 --self-contained true `
        -p:Platform=x86 -p:PublishSingleFile=true `
        -p:Version=$Version -p:FileVersion=1.0.0.0 `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $publishRoot --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

    New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
    Copy-Item (Join-Path $publishRoot 'PlusOneTrainer.exe') $packageRoot
    Copy-Item README.md, README.en.md, LICENSE, THIRD_PARTY_NOTICES.md $packageRoot
    Copy-Item docs (Join-Path $packageRoot 'docs') -Recurse
    $nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages'
    Copy-Item (Join-Path $nugetRoot 'microsoft.netcore.app.runtime.win-x86\8.0.30\LICENSE.TXT') `
        (Join-Path $packageRoot 'DOTNET-RUNTIME-LICENSE.txt')
    Copy-Item (Join-Path $nugetRoot 'microsoft.netcore.app.runtime.win-x86\8.0.30\THIRD-PARTY-NOTICES.TXT') `
        (Join-Path $packageRoot 'DOTNET-RUNTIME-THIRD-PARTY-NOTICES.txt')
    Copy-Item (Join-Path $nugetRoot 'microsoft.windowsdesktop.app.runtime.win-x86\8.0.30\LICENSE') `
        (Join-Path $packageRoot 'WINDOWS-DESKTOP-RUNTIME-LICENSE.txt')
    Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') `
        -Value "$(Get-FileHash (Join-Path $packageRoot 'PlusOneTrainer.exe') -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  PlusOneTrainer.exe" `
        -Encoding ascii
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
    Compress-Archive -Path "$packageRoot\*" -DestinationPath $zipPath -CompressionLevel Optimal
    Set-Content -LiteralPath "$zipPath.sha256" `
        -Value "$(Get-FileHash $zipPath -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  $(Split-Path -Leaf $zipPath)" `
        -Encoding ascii
    Write-Host "Created $zipPath"
}
finally {
    Pop-Location
}
