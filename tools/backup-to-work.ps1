param(
    [string]$Source = 'X:\GitProjects\TeamspeakOverlay',
    [string]$Target = 'X:\Work\TeamspeakOverlay'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $Source '.git'))) {
    throw "В источнике нет .git: $Source"
}

$sourceFull = (Resolve-Path $Source).Path.TrimEnd('\')
$targetFull = [System.IO.Path]::GetFullPath($Target).TrimEnd('\')

if ($sourceFull -eq $targetFull) {
    throw 'Источник и цель совпадают — это не бэкап.'
}

if (-not (Test-Path $targetFull)) {
    New-Item -ItemType Directory -Path $targetFull -Force | Out-Null
}

Write-Host "Бэкап: $sourceFull -> $targetFull"

# /MIR: точная копия; билд-мусор и старые сборки не тащим.
robocopy $sourceFull $targetFull /MIR `
    /XD bin obj .vs dist-standalone `
    /XF *.user *.suo `
    /R:2 /W:2 /NFL /NDL /NJH /NJS | Out-Null

$code = $LASTEXITCODE
if ($code -ge 8) {
    throw "robocopy завершился с ошибкой (код $code)"
}

$commit = (git -C $sourceFull rev-parse --short HEAD).Trim()
$tag = (git -C $sourceFull describe --tags --always).Trim()
$version = 'unknown'
$csproj = Join-Path $sourceFull 'src\TsOverlay\TsOverlay.csproj'
if (Test-Path $csproj) {
    $m = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>'
    if ($m) { $version = $m.Matches[0].Groups[1].Value }
}

$info = @(
    "backup: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
    "source: $sourceFull",
    "commit: $commit",
    "tag: $tag",
    "version: $version"
)
$info | Set-Content (Join-Path $targetFull 'BACKUP-INFO.txt') -Encoding utf8

Write-Host "Готово. commit=$commit tag=$tag version=$version"
Write-Host "Проверка: $(Test-Path (Join-Path $targetFull 'src\TsOverlay\TsOverlay.csproj'))"
