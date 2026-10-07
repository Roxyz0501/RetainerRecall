$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$output = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Force $output | Out-Null
$manifestPath = Join-Path $projectRoot 'bin/Release/RetainerRecall.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Author -ne 'Roxyz0501' -or $manifest.InternalName -ne 'RetainerRecall' -or !$manifest.RepoUrl -or !$manifest.IconUrl) { throw 'Manifest identity is incomplete' }
$zipPath = Join-Path $output "RetainerRecall-$($manifest.AssemblyVersion).zip"
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    $files = @{
        'RetainerRecall.dll' = 'bin/Release/RetainerRecall.dll'
        'RetainerRecall.json' = 'bin/Release/RetainerRecall.json'
        'RetainerRecall.deps.json' = 'bin/Release/RetainerRecall.deps.json'
        'images/icon.png' = 'images/icon.png'
        'LICENSE' = 'LICENSE'
        'THIRD-PARTY-NOTICES.md' = 'THIRD-PARTY-NOTICES.md'
        'README.md' = 'README.md'
    }
    foreach ($name in ($files.Keys | Sort-Object)) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $projectRoot $files[$name]), $name) | Out-Null
    }
} finally { $archive.Dispose(); $stream.Dispose() }
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
