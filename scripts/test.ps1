$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputDirectory = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$executable = Join-Path $outputDirectory 'ContractTests.exe'
& $compiler /nologo /target:exe /langversion:5 /warnaserror+ "/out:$executable" (Join-Path $projectRoot 'src\WallSensorDemo\Contract.cs') (Join-Path $projectRoot 'tests\ContractTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw "Contract tests failed ($LASTEXITCODE)." }

