param([string]$ExePath)
$ErrorActionPreference = 'Stop'

if (-not $ExePath) {
  $candidates = @(
    (Join-Path (Split-Path $PSScriptRoot -Parent) 'LumaFinal\LumaFinal.exe'),
    (Join-Path $PSScriptRoot 'LumaFinal.exe'),
    (Join-Path $PSScriptRoot 'Luma.exe'),
    (Join-Path $PSScriptRoot 'MysticLightShortcut.exe'),
    (Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\MysticLightShortcut.exe'),
    (Join-Path $PSScriptRoot 'bin\x64\Release\net8.0-windows\MysticLightShortcut.exe')
  )
  $ExePath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $ExePath -or -not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
  throw 'Не найден Luma.exe. Укажите путь явно: .\RegisterHotkeyTasks.ps1 -ExePath "C:\путь\Luma.exe"'
}
$exe = (Resolve-Path -LiteralPath $ExePath).Path
$user = "$env:USERDOMAIN\$env:USERNAME"
$items = @(
 @{Name='Red';Args='255 0 0'}, @{Name='Blue';Args='0 80 255'}, @{Name='Orange';Args='255 50 0'},
 @{Name='White';Args='255 255 255'}, @{Name='Turquoise';Args='64 224 208'}, @{Name='Off';Args='off'},
 @{Name='BrightnessUp';Args='brightness-up'}, @{Name='BrightnessDown';Args='brightness-down'}
)
$svc = New-Object -ComObject 'Schedule.Service'
$svc.Connect()
try { $folder = $svc.GetFolder('\MysticLightShortcuts') } catch { $folder = $svc.GetFolder('\').CreateFolder('MysticLightShortcuts', $null) }
foreach($item in $items) {
  $definition = $svc.NewTask(0)
  $definition.RegistrationInfo.Description = 'MSI Mystic Light shortcut action launched by user hotkey.'
  $definition.Principal.UserId = $user
  $definition.Principal.LogonType = 3
  $definition.Principal.RunLevel = 0
  $definition.Settings.Enabled = $true
  $definition.Settings.AllowDemandStart = $true
  $definition.Settings.MultipleInstances = 3
  $definition.Settings.DisallowStartIfOnBatteries = $false
  $definition.Settings.StopIfGoingOnBatteries = $false
  $definition.Settings.ExecutionTimeLimit = 'PT2M'
  $action = $definition.Actions.Create(0)
  $action.Path = $exe
  $action.Arguments = $item.Args
  $action.WorkingDirectory = Split-Path $exe
  try {
    $folder.RegisterTaskDefinition($item.Name, $definition, 6, $user, $null, 3, $null) | Out-Null
  } catch {
    Write-Warning "Не удалось обновить задачу $($item.Name): $($_.Exception.Message)"
  }
}

# Keep shell hotkeys in Start Menu, where Windows registers shortcut keys.
$startMenuLuma = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Luma'
New-Item -ItemType Directory -Path $startMenuLuma -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcutItems = @(
 @{Name='Подсветка — красный';Args='255 0 0';Hotkey='Alt+Ctrl+Shift+3'},
 @{Name='Подсветка — синий';Args='0 80 255';Hotkey='Alt+Ctrl+Shift+5'},
 @{Name='Подсветка — оранжевый';Args='255 50 0';Hotkey='Alt+Ctrl+Shift+4'},
 @{Name='Подсветка — белый';Args='255 255 255';Hotkey='Alt+Ctrl+W'},
 @{Name='Подсветка — бирюзовый';Args='64 224 208';Hotkey='Alt+Ctrl+Shift+2'},
 @{Name='Подсветка — выключить';Args='off';Hotkey='Alt+Ctrl+Shift+0'},
 @{Name='Подсветка — ярче 10%';Args='brightness-up';Hotkey='Alt+Ctrl+Shift+Up'},
 @{Name='Подсветка — тусклее 10%';Args='brightness-down';Hotkey='Alt+Ctrl+Shift+Down'}
)
foreach ($item in $shortcutItems) {
  $shortcut = $shell.CreateShortcut((Join-Path $startMenuLuma ($item.Name + '.lnk')))
  $shortcut.TargetPath = $exe
  $shortcut.Arguments = $item.Args
  $shortcut.WorkingDirectory = Split-Path $exe
  $shortcut.Hotkey = $item.Hotkey
  $shortcut.Save()
}

# Start after sign-in with a short delay so MSI Center and its SDK are ready.
$startup = $svc.NewTask(0)
$startup.RegistrationInfo.Description = 'Starts Luma in the notification area after sign-in.'
$startup.Principal.UserId = $user
$startup.Principal.LogonType = 3
$startup.Principal.RunLevel = 0
$startup.Settings.Enabled = $true
$startup.Settings.StartWhenAvailable = $true
$startup.Settings.AllowDemandStart = $true
$startup.Settings.MultipleInstances = 2
$startup.Settings.ExecutionTimeLimit = 'PT0S'
$trigger = $startup.Triggers.Create(9)
$trigger.UserId = $user
$trigger.Delay = 'PT20S'
$startupAction = $startup.Actions.Create(0)
$startupAction.Path = $exe
$startupAction.WorkingDirectory = Split-Path $exe
$startupTaskRegistered = $false
try {
  $folder.RegisterTaskDefinition('LumaStartup', $startup, 6, $user, $null, 3, $null) | Out-Null
  $startupTaskRegistered = $true
} catch {
  Write-Warning "Не удалось создать задачу отложенного автозапуска: $($_.Exception.Message)"
}

# Avoid launching the tray app twice at sign-in.
$startupLink = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\Luma.lnk'
if ($startupTaskRegistered -and (Test-Path -LiteralPath $startupLink)) {
  Move-Item -LiteralPath $startupLink -Destination ($startupLink + '.backup') -Force
}
