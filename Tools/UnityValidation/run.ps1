param(
    [Parameter(Mandatory = $true)][string]$Unity,
    [string]$HostDirectory = (Join-Path $env:TEMP ('FrameworkSetupValidation-' + [guid]::NewGuid().ToString('N'))),
    [switch]$DisplayOnly
)
$ErrorActionPreference = 'Stop'
if (!$DisplayOnly) {
    try { $models = Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 3 }
    catch { throw 'This live smoke test requires a running local Ollama service.' }
    if ($models.models.name -notcontains 'qwen3:0.6b') { throw 'Install qwen3:0.6b before running this live smoke test.' }
}
$frameworkRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (Test-Path -LiteralPath $HostDirectory) { throw 'Choose a new directory; this script never resets an existing project.' }
foreach ($directory in @('Assets/Editor', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $HostDirectory $directory) -Force | Out-Null
}
$manifest = @{
    dependencies = @{
        'com.hatomaru.ai.framework' = 'file:' + (Join-Path $frameworkRoot 'src/AIDrivenFramework').Replace('\', '/')
        'com.cysharp.unitask' = 'https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#a77a1b4a89824fb6df293099de3060daf2a432c1'
        'com.annulusgames.lit-motion' = 'https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion#422eb124051c81a9bc3f422ebf190703c5053514'
        'com.unity.test-framework' = '1.6.0'
        'com.unity.ugui' = '2.0.0'
        'com.unity.modules.uielements' = '1.0.0'
        'com.unity.modules.screencapture' = '1.0.0'
        'com.unity.modules.imageconversion' = '1.0.0'
    }
    testables = @('com.hatomaru.ai.framework')
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $HostDirectory 'Packages/manifest.json') -Encoding utf8
'm_EditorVersion: 6000.3.10f1' | Set-Content -LiteralPath (Join-Path $HostDirectory 'ProjectSettings/ProjectVersion.txt') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SetupValidation.cs.txt') -Destination (Join-Path $HostDirectory 'Assets/Editor/SetupValidation.cs')
$env:SETUP_PACKAGE_PATH = Join-Path $frameworkRoot 'src/AIDrivenFramework/Editor/Packages/AIDriven_AISetup.unitypackage'
$env:SETUP_QA_OUTPUT = Join-Path $HostDirectory 'Proof'
function Invoke-ValidationUnity([string[]]$Extra, [string]$LogName) {
    $arguments = @('-batchmode', '-projectPath', ('"' + $HostDirectory + '"'), '-logFile', ('"' + (Join-Path $HostDirectory $LogName) + '"')) + $Extra
    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Unity failed; inspect $LogName in $HostDirectory" }
}
Invoke-ValidationUnity @('-nographics', '-executeMethod', 'SetupValidation.Import') 'import.log'
Invoke-ValidationUnity @('-nographics', '-runTests', '-testPlatform', 'EditMode', '-testFilter', 'AIDrivenFW.Tests.Unit', '-testResults', ('"' + (Join-Path $HostDirectory 'unit.xml') + '"')) 'unit.log'
$runner = if ($DisplayOnly) { 'SetupDisplayRunner.cs.txt' } else { 'SetupSmokeRunner.cs.txt' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot $runner) -Destination (Join-Path $HostDirectory 'Assets/SetupSmokeRunner.cs')
Invoke-ValidationUnity @('-quit', '-executeMethod', 'SetupValidation.Build') 'build.log'
$player = Start-Process -FilePath (Join-Path $HostDirectory 'Build/SetupValidation.exe') -ArgumentList @('-screen-width', '1440', '-screen-height', '900', '-screen-fullscreen', '0', '-logFile', ('"' + (Join-Path $HostDirectory 'player.log') + '"')) -WindowStyle Hidden -Wait -PassThru
if ($player.ExitCode -ne 0) { throw "Player validation failed; inspect $HostDirectory" }
Write-Output "Validation complete. Results and screenshots: $HostDirectory"
