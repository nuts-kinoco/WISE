using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace WISE.Infrastructure.Services
{
    public class FFmpegThumbnailService
    {
        private readonly string _ffmpegPath = @"C:\Users\nat\.gemini\antigravity\scratch\bin\ffmpeg.exe";

        public async Task<string> GenerateThumbnailAsync(string videoPath, string outputDirectory)
        {
            if (!File.Exists(_ffmpegPath))
            {
                throw new FileNotFoundException($"FFmpeg executable not found at {_ffmpegPath}");
            }

            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException($"Video file not found at {videoPath}");
            }

            Directory.CreateDirectory(outputDirectory);
            // 固定ファイル名: 再実行時に上書きすることで重複アセットを防ぐ
            var outputFilename = "thumbnail.jpg";
            var outputPath = Path.Combine(outputDirectory, outputFilename);

            // Command: ffmpeg -ss 00:00:10 -i input.mp4 -frames:v 1 -q:v 2 output.jpg
            // Extracts a frame at 10 seconds. In reality, you might want to calculate 10% or just use a fixed 30s.
            var arguments = $"-y -ss 00:01:00 -i \"{videoPath}\" -frames:v 1 -q:v 2 \"{outputPath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start FFmpeg process.");
            }

            // Avoid deadlock by reading the streams before waiting for exit
            var errorTask = process.StandardError.ReadToEndAsync();
            var outputTask = process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();

            var error = await errorTask;
            var output = await outputTask;

            if (process.ExitCode != 0)
            {
                throw new Exception($"FFmpeg failed with exit code {process.ExitCode}: {error}");
            }

            return outputPath;
        }
    }
}
