$parentDir = Split-Path -Path (Get-Location) -Parent
$Assembly = [Reflection.Assembly]::LoadFile("$parentDir\App\bin\Release\net5.0-windows\win-x64\PingLogger.dll")
$version = $Assembly.GetName().Version;
$version | ConvertTo-Json | Out-File -FilePath "./legacy.json"