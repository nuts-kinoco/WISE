$testDir = "X:\3次\動画\TEST"
if (Test-Path $testDir) { Remove-Item -Path $testDir -Recurse -Force }
New-Item -Path $testDir -ItemType Directory -Force

$files = @(
    "EKDV-775.mp4",
    "FTAV-012.mp4",
    "POW-007.mp4",
    "POW-008.mp4",
    "FC2-PPV-4841573.mp4",
    "FC2-PPV-4844845.mp4",
    "RJ123456.mp4"
)

foreach ($f in $files) {
    # Generate 1-second blank mp4
    & "C:\Users\nat\.gemini\antigravity\scratch\bin\ffmpeg.exe" -f lavfi -i color=c=black:s=1280x720:d=1 -f lavfi -i anullsrc=r=44100:cl=stereo -c:v libx264 -c:a aac -shortest "$testDir\$f"
}
