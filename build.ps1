$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\TapoDesktop\TapoDesktop.csproj'
$out = Join-Path $root 'publish'
Write-Host 'Publishing self-contained x64 Windows 10+ WPF application...'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Write-Host "Published app: $out\TapoDesktop.exe"
Write-Host 'Optional: copy a properly licensed ffmpeg.exe into the publish folder for recording.'
Write-Host 'Then compile installer\TapoDesktop.iss with Inno Setup 6.'
