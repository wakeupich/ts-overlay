param(
    [string]$Source = 'X:\GitProjects\TeamspeakOverlay',
    [string]$Target = 'X:\Work\TeamspeakOverlay',
    [string]$Tag = '',
    [string]$AssetsDir = ''
)

# Снимок релиза по стандарту AGENTS.md §12:
# исходники из тега без .git, dist с ассетами, BACKUP-INFO.txt.

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $Source '.git'))) {
    throw "В источнике нет .git: $Source"
}

$sourceFull = (Resolve-Path $Source).Path.TrimEnd('\')
$targetFull = [System.IO.Path]::GetFullPath($Target).TrimEnd('\')

if ($sourceFull -eq $targetFull) {
    throw 'Источник и цель совпадают — это не снимок.'
}

if (-not $Tag) {
    $Tag = (& git -C $sourceFull describe --tags --abbrev=0 2>$null).Trim()
    if (-not $Tag) {
        throw 'В репозитории нет тегов — сначала сделайте релиз.'
    }
}

& git -C $sourceFull rev-parse "$Tag^{}" 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Тег не найден: $Tag"
}

$commit = (& git -C $sourceFull rev-parse "$Tag^{}").Trim()
$version = 'unknown'
$csprojRaw = (& git -C $sourceFull show "$Tag`:src/TsOverlay/TsOverlay.csproj")
$m = [regex]::Match(($csprojRaw -join "`n"), '<Version>([^<]+)</Version>')
if ($m.Success) {
    $version = $m.Groups[1].Value
}

if (-not $AssetsDir) {
    $AssetsDir = Join-Path $sourceFull 'dist'
}

$stage = Join-Path $env:TEMP ("ts-overlay-snapshot-" + [guid]::NewGuid().ToString('N'))
$zip = "$stage.zip"

Write-Host "Снимок: $Tag (commit $commit, version $version)"
Write-Host "Источник: $sourceFull"
Write-Host "Цель:     $targetFull"

try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    & git -C $sourceFull archive --format=zip --output=$zip $Tag
    if ($LASTEXITCODE -ne 0) {
        throw "git archive завершился с ошибкой (код $LASTEXITCODE)"
    }

    Expand-Archive -Path $zip -DestinationPath $stage -Force
    Remove-Item $zip -Force

    # Ассеты релиза (если сборка лежит рядом).
    $assetsCopied = @()
    if (Test-Path $AssetsDir) {
        $stageDist = Join-Path $stage 'dist'
        New-Item -ItemType Directory -Path $stageDist -Force | Out-Null
        foreach ($pattern in @('*.msi', '*.exe', '*.zip', 'SHA256SUMS.txt')) {
            Get-ChildItem -Path $AssetsDir -Filter $pattern -File -ErrorAction SilentlyContinue |
                ForEach-Object {
                    Copy-Item $_.FullName -Destination $stageDist -Force
                    $assetsCopied += $_.Name
                }
        }
    }

    if ($assetsCopied.Count -eq 0) {
        Write-Warning "Ассеты не найдены в $AssetsDir — снимок будет без dist."
    }

    $assetLines = @()
    $stageDist = Join-Path $stage 'dist'
    if (Test-Path $stageDist) {
        foreach ($file in Get-ChildItem $stageDist -File | Sort-Object Name) {
            $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLower()
            $assetLines += "  $hash  $($file.Name)"
        }
    }

    $info = @(
        "backup: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
        "source: $sourceFull",
        "repo: https://github.com/wakeupich/ts-overlay",
        "commit: $commit",
        "tag: $Tag",
        "version: $version",
        "standard: working copy X:\GitProjects\<Project>; release snapshot X:\Work\<Project> (no .git, with dist assets)"
    )
    if ($assetLines.Count -gt 0) {
        $info += 'assets:'
        $info += $assetLines
    }

    $info | Set-Content (Join-Path $stage 'BACKUP-INFO.txt') -Encoding UTF8

    if (Test-Path $targetFull) {
        # /MIR: заменяем прошлый снимок актуальным (старые версии есть в git-тегах).
        robocopy $stage $targetFull /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS | Out-Null
        if ($LASTEXITCODE -ge 8) {
            throw "robocopy завершился с ошибкой (код $LASTEXITCODE)"
        }
    } else {
        Move-Item $stage $targetFull
    }

    Write-Host "Готово. tag=$Tag version=$version assets=$($assetsCopied.Count)"
} finally {
    if (Test-Path $stage) {
        Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $zip) {
        Remove-Item $zip -Force -ErrorAction SilentlyContinue
    }
}
