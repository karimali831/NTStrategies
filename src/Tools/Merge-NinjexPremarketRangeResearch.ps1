param(
    [Parameter(Mandatory = $false)]
    [string]$SourceFolder = "$env:USERPROFILE\Documents\NinjaTrader 8\NinjexData",

    [Parameter(Mandatory = $false)]
    [string]$OutputFolder = "$env:USERPROFILE\Documents\NinjaTrader 8\NinjexData\Combined"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null

$suffixes = @(
    "sessions",
    "breakouts_audit",
    "breakouts_final",
    "candidates",
    "trades",
    "daily",
    "manifest"
)

foreach ($suffix in $suffixes) {
    $files = Get-ChildItem -Path $SourceFolder -Filter "*_${suffix}.csv" -File |
        Sort-Object FullName

    if ($files.Count -eq 0) {
        Write-Host "No files found for $suffix"
        continue
    }

    $outputPath = Join-Path $OutputFolder "premarket_range_research_combined_${suffix}.csv"
    $headerWritten = $false

    $writer = [System.IO.StreamWriter]::new($outputPath, $false, [System.Text.UTF8Encoding]::new($false))
    try {
        foreach ($file in $files) {
            $reader = [System.IO.StreamReader]::new($file.FullName)
            try {
                $header = $reader.ReadLine()
                if (-not $headerWritten) {
                    $writer.WriteLine($header)
                    $headerWritten = $true
                }

                while (($line = $reader.ReadLine()) -ne $null) {
                    if (-not [string]::IsNullOrWhiteSpace($line)) {
                        $writer.WriteLine($line)
                    }
                }
            }
            finally {
                $reader.Dispose()
            }
        }
    }
    finally {
        $writer.Dispose()
    }

    Write-Host "Combined $($files.Count) files -> $outputPath"
}
