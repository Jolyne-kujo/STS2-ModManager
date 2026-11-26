param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [string]$CecilAssembly
)
$ErrorActionPreference = 'Stop'
if (-not $CecilAssembly) {
    $CecilAssembly = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.net.illink.tasks') -Filter Mono.Cecil.dll -Recurse |
        Where-Object FullName -Match 'tools[\\/]net9\.0[\\/]' |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $CecilAssembly) { throw 'Build once to restore the .NET publish tools, or supply -CecilAssembly.' }
Add-Type -Path $CecilAssembly
$dll = Join-Path $GameDirectory 'data_sts2_windows_x86_64\sts2.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
    function Find-Type([string]$Name) {
        $type = $assembly.MainModule.Types | Where-Object FullName -eq $Name
        if (-not $type) { throw "Missing type: $Name" }
        return $type
    }
    function Assert-Member($Type, [string]$Name) {
        if (-not (($Type.Fields | Where-Object Name -eq $Name) -or ($Type.Properties | Where-Object Name -eq $Name))) {
            throw "Missing member: $($Type.FullName).$Name"
        }
    }
    $manager = Find-Type 'MegaCrit.Sts2.Core.Modding.ModManager'
    foreach ($name in @('Mods', '_mods', '_settings')) { Assert-Member $manager $name }
    $load = @($manager.Methods | Where-Object { $_.Name -eq 'TryLoadMod' -and $_.IsStatic -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'MegaCrit.Sts2.Core.Modding.Mod' })
    if ($load.Count -ne 1 -or $load[0].ReturnType.FullName -ne 'System.Void') { throw 'TryLoadMod signature changed.' }
    $sort = @($manager.Methods | Where-Object { $_.Name -eq 'SortModList' -and $_.IsStatic -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'System.Collections.Generic.List`1<MegaCrit.Sts2.Core.Modding.SettingsSaveMod>' })
    if ($sort.Count -ne 1) { throw 'SortModList signature changed.' }
    if (-not ($manager.Methods | Where-Object { $_.Name -eq 'RemoveDisabledMods' -and $_.IsStatic -and $_.Parameters.Count -eq 0 })) { throw 'RemoveDisabledMods signature changed.' }
    $mod = Find-Type 'MegaCrit.Sts2.Core.Modding.Mod'
    foreach ($name in @('manifest', 'modSource', 'state')) { Assert-Member $mod $name }
    $states = Find-Type 'MegaCrit.Sts2.Core.Modding.ModLoadState'
    foreach ($name in @('None', 'Loaded', 'Failed', 'Disabled', 'DisabledDuplicate')) { Assert-Member $states $name }
    $manifest = Find-Type 'MegaCrit.Sts2.Core.Modding.ModManifest'
    foreach ($name in @('id', 'name', 'author', 'version', 'description')) { Assert-Member $manifest $name }
    $save = Find-Type 'MegaCrit.Sts2.Core.Saves.SaveManager'
    foreach ($name in @('Instance', 'SettingsSave', '_settingsSaveManager')) { Assert-Member $save $name }
    $settings = Find-Type 'MegaCrit.Sts2.Core.Modding.ModSettings'
    foreach ($name in @('ModList', 'PlayerAgreedToModLoading')) { Assert-Member $settings $name }
    if (-not ($settings.Properties | Where-Object Name -eq 'ModList').SetMethod) { throw 'ModList setter hook is unavailable.' }
    $savedMod = Find-Type 'MegaCrit.Sts2.Core.Modding.SettingsSaveMod'
    foreach ($name in @('Id', 'Source', 'IsEnabled')) { Assert-Member $savedMod $name }
    $writer = Find-Type 'MegaCrit.Sts2.Core.Saves.Managers.SettingsSaveManager'
    $writeMethod = $writer.Methods | Where-Object { $_.Name -eq 'SaveSettings' -and $_.Parameters.Count -eq 0 }
    if (-not $writeMethod -or $writeMethod.ReturnType.FullName -ne 'System.Void' -or $writeMethod.Body.ExceptionHandlers.Count -ne 0) {
        throw 'Synchronous exception-propagating settings writer contract changed.'
    }
    Write-Host 'PASS installed game loader, manifest, state and synchronous save API contracts'
    Get-Content -LiteralPath (Join-Path $GameDirectory 'release_info.json')
    Get-FileHash -LiteralPath $dll -Algorithm SHA256
} finally { $assembly.Dispose() }
