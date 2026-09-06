param(
    [Parameter(Mandatory = $false)]
    [string]$SourceFolder =
        "$env:USERPROFILE\Documents\NinjaTrader 8\NinjexData",

    [Parameter(Mandatory = $false)]
    [string]$OutputFolder =
        "$env:USERPROFILE\Documents\NinjaTrader 8\NinjexData\Combined"
)

$ErrorActionPreference = "Stop"


New-Item `
    -ItemType Directory `
    -Force `
    -Path $OutputFolder |
    Out-Null


$suffixes = @(
    "sessions",
    "breakouts_audit",
    "breakouts_final",
    "candidates",
    "trades",
    "risk_scenarios",
    "daily",
    "manifest",
    "execution_equity"
)


foreach ($suffix in $suffixes) {

    $files = @(
        Get-ChildItem `
            -Path $SourceFolder `
            -Filter "*_${suffix}.csv" `
            -File |
        Sort-Object FullName
    )


    if ($files.Count -eq 0) {
        Write-Host "No files found for $suffix"
        continue
    }


    $outputPath =
        Join-Path `
            $OutputFolder `
            "premarket_range_research_combined_${suffix}.csv"


    $expectedHeader = $null


    $writer =
        [System.IO.StreamWriter]::new(
            $outputPath,
            $false,
            [System.Text.UTF8Encoding]::new($false)
        )


    try {

        foreach ($file in $files) {

            $reader =
                [System.IO.StreamReader]::new(
                    $file.FullName
                )


            try {

                $header =
                    $reader.ReadLine()


                if ([string]::IsNullOrWhiteSpace($header)) {
                    throw (
                        "File has no CSV header: " +
                        $file.FullName
                    )
                }


                if ($null -eq $expectedHeader) {

                    $expectedHeader =
                        $header

                    $writer.WriteLine(
                        $header
                    )
                }
                elseif ($header -ne $expectedHeader) {

                    throw (
                        "CSV header mismatch for suffix '$suffix'." +
                        "`n`nExpected:`n$expectedHeader" +
                        "`n`nActual:`n$header" +
                        "`n`nFile:`n$($file.FullName)"
                    )
                }


                while (($line = $reader.ReadLine()) -ne $null) {

                    if (
                        -not [string]::IsNullOrWhiteSpace(
                            $line
                        )
                    ) {
                        $writer.WriteLine(
                            $line
                        )
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


    Write-Host (
        "Combined $($files.Count) files -> " +
        $outputPath
    )
}