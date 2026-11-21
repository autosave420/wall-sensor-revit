param([string]$RevitInstallPath = 'C:\Program Files\Autodesk\Revit 2022')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $projectRoot 'bin\WallSensorDemo.dll'
if (-not (Test-Path -LiteralPath (Join-Path $RevitInstallPath 'Revit.exe'))) {
    throw 'Revit 2022 was not found. Install it with a valid Autodesk entitlement before registering the add-in.'
}
$apiVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $RevitInstallPath 'RevitAPI.dll')).Version
if ($apiVersion.Major -ne 22) { throw "Expected Revit 2022, found API $apiVersion." }
if (-not (Test-Path -LiteralPath $assemblyPath)) { throw 'Run scripts/build.ps1 first.' }
$addinDirectory = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2022'
New-Item -ItemType Directory -Path $addinDirectory -Force | Out-Null
$manifestPath = Join-Path $addinDirectory 'WallSensorDemo.addin'
if (Test-Path -LiteralPath $manifestPath) { throw "Manifest already exists: $manifestPath. Review it before replacing it." }
$safeAssembly = [Security.SecurityElement]::Escape($assemblyPath)
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Command">
    <Text>Build Wall Sensor Demo</Text>
    <Assembly>$safeAssembly</Assembly>
    <AddInId>39c77e7c-c716-4b07-8487-d21401d237f8</AddInId>
    <FullClassName>WallSensorDemo.BuildDemoCommand</FullClassName>
    <VendorId>WSDE</VendorId>
    <VendorDescription>Wall sensor study</VendorDescription>
  </AddIn>
  <AddIn Type="Command">
    <Text>Check Wall Sensor Demo</Text>
    <Assembly>$safeAssembly</Assembly>
    <AddInId>e58640c5-fffd-4b06-bd86-a2f73816f03f</AddInId>
    <FullClassName>WallSensorDemo.CheckDemoCommand</FullClassName>
    <VendorId>WSDE</VendorId>
    <VendorDescription>Wall sensor study</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
[IO.File]::WriteAllText($manifestPath, $manifest, (New-Object Text.UTF8Encoding($false)))
Write-Host "Registered $manifestPath. Restart Revit 2022. Keep this checkout at its current path."

