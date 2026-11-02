[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$validator = Join-Path $PSScriptRoot 'Test-GeneratedIconPack.ps1'
$scratchRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$scratchDirectory = Join-Path $scratchRoot ('otp-harbor-pack-test-' + [Guid]::NewGuid().ToString('N'))
[void] (New-Item -ItemType Directory -Path $scratchDirectory)

function Write-ZipEntry {
    param($Archive, [string] $Path, [byte[]] $Bytes)

    $entry = $Archive.CreateEntry($Path)
    $stream = $entry.Open()
    try { $stream.Write($Bytes, 0, $Bytes.Length) }
    finally { $stream.Dispose() }
}

function Write-Fixture {
    param([string] $Name, [bool] $MissingDarkTheme, [bool] $MissingReportAppearance,
        [int] $FormatVersion = 3)

    $packPath = Join-Path $scratchDirectory "$Name.otphicons"
    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=')
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($png)).ToLowerInvariant()
    $assetPath = "runtime/appearance-128/$hash.png"
    $brandCount = 5001
    $brands = [Collections.Generic.List[object]]::new($brandCount)
    $appearances = [Collections.Generic.List[object]]::new(2 * $brandCount)
    $rightsBrands = [Collections.Generic.List[object]]::new($brandCount)
    $assetPaths = [Collections.Generic.List[string]]::new($brandCount)

    for ($index = 0; $index -lt $brandCount; $index++) {
        $id = "brand-$index"
        if ($FormatVersion -eq 1) {
            $iconPath = "icons/$id.svg"
            $brands.Add(@{ id = $id; icon = $iconPath })
            $assetPaths.Add($iconPath)
        }
        elseif ($FormatVersion -eq 2) {
            $runtimePath = "runtime/foreground-128/$id.png"
            $brands.Add(@{ id = $id; runtimeIcon = @{ path = $runtimePath } })
            $assetPaths.Add($runtimePath)
        }
        else {
            $themeAsset = @{ path = $assetPath; sha256 = $hash }
            $themeAppearances = @{ lightTheme = @{ asset = $themeAsset }; darkTheme = @{ asset = $themeAsset } }
            if ($MissingDarkTheme -and $index -eq 0) { $themeAppearances.Remove('darkTheme') }
            $brands.Add(@{ id = $id; appearances = $themeAppearances })
            $appearances.Add(@{ brandId = $id; appearance = 'lightTheme' })
            if (-not ($MissingReportAppearance -and $index -eq 0)) {
                $appearances.Add(@{ brandId = $id; appearance = 'darkTheme' })
            }
        }
        $rightsBrands.Add(@{ brandId = $id })
    }

    $manifest = @{
        formatVersion = $FormatVersion
        brands = $brands
        issuerAliases = @(@{ key = 'brand'; brandId = 'brand-0' })
        sources = @(
            @{ provider = 'aegis' },
            @{ provider = 'dashboard-icons' },
            @{ provider = 'simple-icons' })
    }
    $archive = [IO.Compression.ZipFile]::Open($packPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Write-ZipEntry $archive 'pack.json' ([Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 15 -Compress)))
        if ($FormatVersion -eq 1) {
            [void] $archive.CreateEntry('icons/')
            foreach ($path in $assetPaths) {
                Write-ZipEntry $archive $path ([Text.Encoding]::UTF8.GetBytes('<svg/>'))
            }
        }
        else {
            [void] $archive.CreateEntry('runtime/')
            [void] $archive.CreateEntry($(if ($FormatVersion -eq 2) {
                'runtime/foreground-128/'
            } else {
                'runtime/appearance-128/'
            }))
            if ($FormatVersion -eq 2) {
                foreach ($path in $assetPaths) { Write-ZipEntry $archive $path $png }
            }
            else { Write-ZipEntry $archive $assetPath $png }
        }
    }
    finally { $archive.Dispose() }

    $visualReport = @{
        formatVersion = $FormatVersion
        totalBrands = $brandCount
        unresolvedFailures = 0
    }
    if ($FormatVersion -eq 3) {
        $visualReport.totalAppearances = 2 * $brandCount
        $visualReport.appearances = $appearances
    }
    else { $visualReport.brands = $rightsBrands }
    $rightsReport = @{
        formatVersion = $FormatVersion
        notLegalClearance = $true
        selectedBrands = $brandCount
        brands = $rightsBrands
    }
    [IO.File]::WriteAllText([IO.Path]::ChangeExtension($packPath, '.visual-report.json'),
        ($visualReport | ConvertTo-Json -Depth 15 -Compress))
    [IO.File]::WriteAllText([IO.Path]::ChangeExtension($packPath, '.rights-report.json'),
        ($rightsReport | ConvertTo-Json -Depth 15 -Compress))
    return $packPath
}

function Assert-Fails {
    param([string] $PackPath, [string] $ExpectedMessage)

    try {
        & $validator -PackPath $PackPath | Out-Null
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") { throw }
        return
    }
    throw "Expected '$PackPath' to fail validation: $ExpectedMessage"
}

try {
    & $validator -PackPath (Write-Fixture 'valid-v1' $false $false 1) | Out-Null
    & $validator -PackPath (Write-Fixture 'valid-v2' $false $false 2) | Out-Null
    & $validator -PackPath (Write-Fixture 'valid-v3' $false $false) | Out-Null
    Assert-Fails (Write-Fixture 'missing-dark' $true $false) "missing 'darkTheme' artwork"
    Assert-Fails (Write-Fixture 'missing-report-appearance' $false $true) 'does not cover both themes'
    Write-Host 'Generated pack validator regression fixtures passed.'
}
finally {
    $resolvedScratch = [IO.Path]::GetFullPath($scratchDirectory)
    if ([IO.Path]::GetDirectoryName($resolvedScratch) -cne $scratchRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        -not [IO.Path]::GetFileName($resolvedScratch).StartsWith('otp-harbor-pack-test-', [StringComparison]::Ordinal)) {
        throw "Refusing to remove unexpected test directory '$resolvedScratch'."
    }
    Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
}
