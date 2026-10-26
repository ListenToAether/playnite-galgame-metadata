param([string]$Probe = (Join-Path $PSScriptRoot 'probe\Probe.exe'))

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $Probe
[void][Reflection.Assembly]::LoadFrom((Join-Path $dir 'Playnite.SDK.dll'))
$asm = [Reflection.Assembly]::LoadFrom($Probe)

$settingsType = $asm.GetType('GalgameMetadata.Playnite.PluginSettings')
$viewType = $asm.GetType('GalgameMetadata.Playnite.SettingsView')
if ($null -eq $settingsType -or $null -eq $viewType) { throw 'SettingsView types not found in probe assembly.' }

$settings = [Activator]::CreateInstance($settingsType)

# Plain view (Ymgal style) and token view (TouchGAL style), created the same way Playnite does it.
$plain = [Activator]::CreateInstance($viewType, @($settings, 'Ymgal', 'https://www.ymgal.games/', $false))
$token = [Activator]::CreateInstance($viewType, @($settings, 'TouchGAL', 'https://www.touchgal.ink/', $true))

"plain view content: " + ($null -ne $plain.Content)
"token view content: " + ($null -ne $token.Content)
$panel = $plain.Content
if ($panel -is [System.Windows.Controls.ScrollViewer]) { $panel = $panel.Content }
"plain panel children: " + $panel.Children.Count
"token panel children: " + $token.Content.Children.Count
$tokenPanel = $token.Content
if ($tokenPanel -is [System.Windows.Controls.ScrollViewer]) { $tokenPanel = $tokenPanel.Content }
"token panel children: " + $tokenPanel.Children.Count
$texts = New-Object System.Collections.Generic.List[string]
foreach ($child in $tokenPanel.Children) {
    if ($child -is [System.Windows.Controls.TextBlock]) { $texts.Add($child.Text) }
}
"token view labels: " + ($texts -join ' / ')
"OK"
