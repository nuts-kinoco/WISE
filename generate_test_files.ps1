$testDataDir = "C:\Users\nat\.gemini\antigravity\scratch\WISE\TestData"
if (!(Test-Path -Path $testDataDir)) {
    New-Item -ItemType Directory -Path $testDataDir | Out-Null
}

$commercialPrefixes = @("IPX", "SSIS", "SONE", "MIAA", "MIDV", "JUQ", "PRED")
$fc2Prefixes = @("FC2-PPV", "FC2")
$extensions = @(".mp4", ".jpg", ".zip")

$totalFiles = 1000
$count = 0

Write-Host "Generating $totalFiles test files in $testDataDir ..."

for ($i = 1; $i -le $totalFiles; $i++) {
    $rand = Get-Random -Minimum 1 -Maximum 100

    $filename = ""
    if ($rand -le 40) {
        # Commercial
        $prefix = $commercialPrefixes | Get-Random
        $num = Get-Random -Minimum 1 -Maximum 999
        $filename = "$prefix-{0:D3}" -f $num
    } elseif ($rand -le 70) {
        # FC2
        $prefix = $fc2Prefixes | Get-Random
        $num = Get-Random -Minimum 1000000 -Maximum 9999999
        $filename = "$prefix-$num"
    } elseif ($rand -le 90) {
        # Date format (e.g. 100115-001)
        $dateStr = Get-Date (Get-Date).AddDays(-(Get-Random -Minimum 0 -Maximum 3650)) -Format "yyMMdd"
        $num = Get-Random -Minimum 1 -Maximum 999
        $filename = "$dateStr-{0:D3}" -f $num
    } else {
        # Unknown format (e.g. Heyzo, 10musume, Pacopacomama, random names)
        $unknownNames = @("Heyzo-1234", "10musume-010120_01", "Pacopacomama-12345", "random_video_xyz", "MyVacation2023")
        $filename = $unknownNames | Get-Random
    }

    $ext = $extensions | Get-Random
    $fullPath = Join-Path $testDataDir "$filename$ext"

    # Create empty file
    New-Item -ItemType File -Path $fullPath -Force | Out-Null
    $count++
}

# Add some duplicates explicitly
New-Item -ItemType File -Path (Join-Path $testDataDir "IPX-001.mp4") -Force | Out-Null
New-Item -ItemType File -Path (Join-Path $testDataDir "IPX-001.jpg") -Force | Out-Null
New-Item -ItemType File -Path (Join-Path $testDataDir "IPX-001-sub.mp4") -Force | Out-Null
$count += 3

Write-Host "Generated $count test files successfully."
