[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require-File {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.PSIsContainer -or $item.Length -eq 0) {
        throw "Expected a non-empty file at '$Path'."
    }

    return $item
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    Require-File -Path $Path | Out-Null
    return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
}

$pack = Require-File -Path $PackPath
if ($pack.Extension -cne '.otphicons') {
    throw "Expected an .otphicons file, received '$($pack.Name)'."
}

$visualReportPath = [IO.Path]::ChangeExtension($pack.FullName, '.visual-report.json')
$rightsReportPath = [IO.Path]::ChangeExtension($pack.FullName, '.rights-report.json')
$visualReport = Read-JsonFile -Path $visualReportPath
$rightsReport = Read-JsonFile -Path $rightsReportPath

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($pack.FullName)
try {
    $manifestEntries = @($archive.Entries | Where-Object FullName -CEQ 'pack.json')
    if ($manifestEntries.Count -ne 1) {
        throw "The pack must contain exactly one pack.json entry; found $($manifestEntries.Count)."
    }

    $reader = [IO.StreamReader]::new(
        $manifestEntries[0].Open(),
        [Text.UTF8Encoding]::new($false, $true),
        $true)
    try {
        $manifest = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }

    if ($manifest.formatVersion -notin @(1, 2, 3)) {
        throw "Unsupported pack format version '$($manifest.formatVersion)'."
    }
    $formatVersion = [int] $manifest.formatVersion

    $brands = @($manifest.brands)
    $aliases = @($manifest.issuerAliases)
    if ($brands.Count -le 5000) {
        throw "The generated catalog must contain more than 5,000 brands; found $($brands.Count)."
    }

    if ($aliases.Count -eq 0) {
        throw 'The generated catalog must contain issuer aliases.'
    }

    $expectedProviders = @('aegis', 'dashboard-icons', 'simple-icons')
    $actualProviders = @($manifest.sources.provider | Sort-Object -CaseSensitive)
    $providerDifference = @(Compare-Object $expectedProviders $actualProviders -CaseSensitive)
    if ($providerDifference.Count -ne 0) {
        throw "Unexpected source providers: $($actualProviders -join ', ')."
    }

    $entryNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $archive.Entries) {
        if (-not $entryNames.Add($entry.FullName)) {
            throw "Duplicate archive entry '$($entry.FullName)'."
        }
    }

    $referencedAssets = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($brand in $brands) {
        if ([string]::IsNullOrWhiteSpace($brand.id)) {
            throw 'The generated catalog contains a brand without an ID.'
        }

        switch ($formatVersion) {
            1 {
                if ([string]::IsNullOrWhiteSpace($brand.icon) -or
                    -not $entryNames.Contains([string] $brand.icon)) {
                    throw "Brand '$($brand.id)' does not reference an existing icon entry."
                }
                [void] $referencedAssets.Add([string] $brand.icon)
            }
            2 {
                if ($brand.PSObject.Properties.Name -ccontains 'icon' -or
                    $brand.PSObject.Properties.Name -cnotcontains 'runtimeIcon' -or
                    $null -eq $brand.runtimeIcon) {
                    throw "Format-2 brand '$($brand.id)' must have a runtimeIcon and no icon field."
                }
                $asset = $brand.runtimeIcon
                $expectedPath = "runtime/foreground-128/$($brand.id).png"
                if ($asset.path -cne $expectedPath -or
                    -not $entryNames.Contains($expectedPath)) {
                    throw "Format-2 brand '$($brand.id)' does not reference its runtime PNG."
                }
                [void] $referencedAssets.Add($expectedPath)
            }
            3 {
                foreach ($forbidden in @('icon', 'backgroundColor', 'runtimeIcon')) {
                    if ($brand.PSObject.Properties.Name -ccontains $forbidden) {
                        throw "Format-3 brand '$($brand.id)' contains forbidden field '$forbidden'."
                    }
                }
                if ($brand.PSObject.Properties.Name -cnotcontains 'appearances' -or
                    $null -eq $brand.appearances) {
                    throw "Format-3 brand '$($brand.id)' has no appearances."
                }
                foreach ($theme in @('lightTheme', 'darkTheme')) {
                    if ($brand.appearances.PSObject.Properties.Name -cnotcontains $theme) {
                        throw "Format-3 brand '$($brand.id)' is missing '$theme' artwork."
                    }
                    $appearance = $brand.appearances.$theme
                    if ($null -eq $appearance -or $null -eq $appearance.asset) {
                        throw "Format-3 brand '$($brand.id)' is missing '$theme' artwork."
                    }
                    $assetPath = [string] $appearance.asset.path
                    if ($assetPath -cnotmatch '^runtime/appearance-128/[a-f0-9]{64}\.png$' -or
                        -not $entryNames.Contains($assetPath) -or
                        $appearance.asset.sha256 -cne [IO.Path]::GetFileNameWithoutExtension($assetPath)) {
                        throw "Format-3 brand '$($brand.id)' has an invalid or missing '$theme' asset."
                    }
                    [void] $referencedAssets.Add($assetPath)
                }
            }
        }
    }

    $assetPrefix = switch ($formatVersion) {
        1 { 'icons/' }
        2 { 'runtime/foreground-128/' }
        3 { 'runtime/appearance-128/' }
    }
    $packagedAssets = @($entryNames | Where-Object {
        $_.StartsWith($assetPrefix, [StringComparison]::Ordinal) -and
        -not $_.EndsWith('/', [StringComparison]::Ordinal)
    })
    if ($packagedAssets.Count -ne $referencedAssets.Count) {
        throw "The pack contains unreferenced or missing format-$formatVersion artwork."
    }
    if ($formatVersion -ne 1 -and
        @($entryNames | Where-Object { $_.EndsWith('.svg', [StringComparison]::OrdinalIgnoreCase) }).Count -ne 0) {
        throw "The PNG-only format-$formatVersion pack contains SVG entries."
    }
    if ($formatVersion -eq 1 -and
        @($entryNames | Where-Object { $_.StartsWith('runtime/', [StringComparison]::Ordinal) }).Count -ne 0) {
        throw 'The format-1 pack contains runtime assets.'
    }
}
finally {
    $archive.Dispose()
}

if (($visualReport.formatVersion -ne $formatVersion) -or
    ($visualReport.unresolvedFailures -ne 0) -or
    ($visualReport.totalBrands -ne $brands.Count)) {
    throw 'The visual verification report does not validate the complete generated catalog.'
}

if ($formatVersion -eq 3) {
    $expectedAppearances = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($brand in $brands) {
        [void] $expectedAppearances.Add("$($brand.id)/lightTheme")
        [void] $expectedAppearances.Add("$($brand.id)/darkTheme")
    }
    $reportedAppearances = @($visualReport.appearances)
    if ($visualReport.totalAppearances -ne (2 * $brands.Count) -or
        $reportedAppearances.Count -ne (2 * $brands.Count)) {
        throw 'The visual verification report does not cover both themes for every brand.'
    }
    foreach ($appearance in $reportedAppearances) {
        if (-not $expectedAppearances.Remove("$($appearance.brandId)/$($appearance.appearance)")) {
            throw "Unexpected or duplicate visual report appearance '$($appearance.brandId)/$($appearance.appearance)'."
        }
    }
    if ($expectedAppearances.Count -ne 0) {
        throw 'The visual verification report is missing theme appearances.'
    }
}
elseif (@($visualReport.brands).Count -ne $brands.Count) {
    throw 'The visual verification report does not cover every brand.'
}

if (($rightsReport.formatVersion -ne $formatVersion) -or
    ($rightsReport.notLegalClearance -ne $true) -or
    ($rightsReport.selectedBrands -ne $brands.Count) -or
    (@($rightsReport.brands).Count -ne $brands.Count)) {
    throw 'The rights assessment report does not cover the complete generated catalog.'
}

Write-Host (
    "Validated '{0}': {1:N0} brands, {2:N0} issuer aliases, {3:N0} bytes." -f `
        $pack.FullName,
        $brands.Count,
        $aliases.Count,
        $pack.Length)
