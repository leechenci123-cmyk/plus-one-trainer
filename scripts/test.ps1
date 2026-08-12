$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testOutput = Join-Path $repositoryRoot 'artifacts\tests'
Push-Location $repositoryRoot
try {
    if (Test-Path -LiteralPath $testOutput) { Remove-Item -LiteralPath $testOutput -Recurse -Force }
    dotnet publish tests\PlusOneTrainer.Tests\PlusOneTrainer.Tests.csproj `
        -c Release -r win-x86 --self-contained true `
        -p:Platform=x86 -p:PublishSingleFile=true `
        -o $testOutput --nologo
    if ($LASTEXITCODE -ne 0) { throw "Test publish failed with exit code $LASTEXITCODE." }
    & (Join-Path $testOutput 'PlusOneTrainer.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
