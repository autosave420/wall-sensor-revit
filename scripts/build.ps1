param(
    [string]$RevitInstallPath = 'C:\Program Files\Autodesk\Revit 2022',
    [switch]$UseNuGetReferences
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$referenceDirectory = $RevitInstallPath
if ($UseNuGetReferences) {
    $cacheDirectory = Join-Path $projectRoot '.cache'
    $referenceDirectory = Join-Path $cacheDirectory 'revit-api-2022.1.0\lib\net48'
    $packagePath = Join-Path $cacheDirectory 'revit-api-2022.1.0.zip'
    $expectedHash = '07A48FDE8C8CD4D3E1EABFE3C41C362351C237A8B8D2BA61474BFE7A1858A0D0'
    New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
    if (-not (Test-Path -LiteralPath $packagePath)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/revit_all_main_versions_api_x64/2022.1.0/revit_all_main_versions_api_x64.2022.1.0.nupkg' -OutFile $packagePath
    }
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'Revit API reference package checksum mismatch. No package scripts will be run.'
    }
    Expand-Archive -LiteralPath $packagePath -DestinationPath (Join-Path $cacheDirectory 'revit-api-2022.1.0') -Force
    Write-Host 'Using third-party NuGet compile references. This does not install or license Revit.'
}
$apiPath = Join-Path $referenceDirectory 'RevitAPI.dll'
$uiPath = Join-Path $referenceDirectory 'RevitAPIUI.dll'
if (-not (Test-Path -LiteralPath $apiPath) -or -not (Test-Path -LiteralPath $uiPath)) {
    throw 'Revit 2022 API references were not found. Set -RevitInstallPath or explicitly use -UseNuGetReferences for compilation only.'
}
$apiVersion = [Reflection.AssemblyName]::GetAssemblyName($apiPath).Version
if ($apiVersion.Major -ne 22) { throw "Expected Revit 2022 API, found $apiVersion" }
$outputDirectory = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\WallSensorDemo') -Filter '*.cs' | ForEach-Object FullName)
$arguments = @('/nologo', '/target:library', '/platform:x64', '/langversion:5', '/optimize+',
    ('/out:' + (Join-Path $outputDirectory 'WallSensorDemo.dll')),
    ('/reference:' + $apiPath), ('/reference:' + $uiPath),
    '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll') + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
Write-Host "Built $outputDirectory\WallSensorDemo.dll against Revit API $apiVersion"

