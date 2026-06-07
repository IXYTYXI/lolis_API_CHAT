$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_PACKAGES = Join-Path $root ".nuget-packages"
$env:APPDATA = Join-Path $root ".appdata"
$env:LOCALAPPDATA = Join-Path $root ".localappdata"

.\.dotnet\dotnet.exe restore .\VPet.Plugin.LLMChat\VPet.Plugin.LLMChat.csproj --configfile .\NuGet.Config
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

.\.dotnet\dotnet.exe build .\VPet.Plugin.LLMChat\VPet.Plugin.LLMChat.csproj -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$dist = Join-Path $root "dist\1110_LLMChat"
$pluginDist = Join-Path $dist "plugin"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
New-Item -ItemType Directory -Force -Path $pluginDist | Out-Null
Copy-Item -Force .\VPet.Plugin.LLMChat\1110_LLMChat\* $dist
Copy-Item -Force .\VPet.Plugin.LLMChat\bin\Release\net8.0-windows\VPet.Plugin.LLMChat.dll $pluginDist
Remove-Item -LiteralPath (Join-Path $dist "VPet.Plugin.LLMChat.dll") -ErrorAction SilentlyContinue

exit 0
