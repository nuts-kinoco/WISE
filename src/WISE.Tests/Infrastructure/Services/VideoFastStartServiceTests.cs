using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using WISE.Infrastructure.Services;
using Xunit;

namespace WISE.Tests.Infrastructure.Services;

public class VideoFastStartServiceTests : IDisposable
{
    private readonly string _tempDir;

    public VideoFastStartServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "wise-faststart-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var size = 8 + payload.Length;
        var box = new byte[size];
        box[0] = (byte)(size >> 24);
        box[1] = (byte)(size >> 16);
        box[2] = (byte)(size >> 8);
        box[3] = (byte)size;
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    // largesize (size==1) box: 8バイトヘッダ + 8バイト64bit実サイズ + payload
    private static byte[] LargeBox(string type, long totalSize)
    {
        var header = new byte[16];
        header[0] = 0; header[1] = 0; header[2] = 0; header[3] = 1; // size32 == 1 (largesize指定)
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(header, 4);
        for (int i = 0; i < 8; i++)
            header[15 - i] = (byte)(totalSize >> (8 * i));
        return header;
    }

    [Fact]
    public void IsFastStart_ShouldReturnTrue_WhenMoovBeforeMdat()
    {
        // ftyp -> moov -> mdat（PFES-058と同じ構造: faststart済み）
        var path = Path.Combine(_tempDir, "faststart.mp4");
        using (var fs = new FileStream(path, FileMode.Create))
        {
            fs.Write(Box("ftyp", new byte[24]));
            fs.Write(Box("moov", new byte[1000]));
            fs.Write(LargeBox("mdat", 16)); // largesizeヘッダのみ（実データは省略）
        }

        VideoFastStartService.IsFastStart(path).Should().BeTrue();
    }

    [Fact]
    public void IsFastStart_ShouldReturnFalse_WhenMdatBeforeMoov()
    {
        // ftyp -> mdat（moovが末尾にある想定。NCYF-023と同じ構造: 非faststart）
        var path = Path.Combine(_tempDir, "notfaststart.mp4");
        using (var fs = new FileStream(path, FileMode.Create))
        {
            fs.Write(Box("ftyp", new byte[24]));
            // mdatをlargesizeでファイル末尾まで伸ばす想定（実際はここでファイルが終わる簡易版）
            var header = new byte[16];
            header[3] = 1;
            System.Text.Encoding.ASCII.GetBytes("mdat").CopyTo(header, 4);
            long total = 32 + 16 + 100; // ftyp(32) + このヘッダ(16) + 適当なpayload
            for (int i = 0; i < 8; i++)
                header[15 - i] = (byte)(total >> (8 * i));
            fs.Write(header);
            fs.Write(new byte[100]);
        }

        VideoFastStartService.IsFastStart(path).Should().BeFalse();
    }

    [Fact]
    public void IsApplicable_ShouldReturnTrue_OnlyForMp4LikeExtensions()
    {
        var svc = CreateService();
        svc.IsApplicable("video.mp4").Should().BeTrue();
        svc.IsApplicable("video.m4v").Should().BeTrue();
        svc.IsApplicable("video.mov").Should().BeTrue();
        svc.IsApplicable("video.mkv").Should().BeFalse();
        svc.IsApplicable("video.avi").Should().BeFalse();
        svc.IsApplicable("archive.zip").Should().BeFalse();
    }

    private static VideoFastStartService CreateService()
        => new(Microsoft.Extensions.Logging.Abstractions.NullLogger<VideoFastStartService>.Instance);

    // 実ライブラリファイルでの回帰確認（開発機のX:ドライブ限定のため、存在しない環境ではスキップする）。
    // NCYF-023はカクつき報告の元になった非faststartファイル、PFES-058は正常なfaststartファイル。
    [Trait("Category", "Integration")]
    [Fact]
    public void IsFastStart_RealLibraryFiles_ShouldMatchKnownStructure()
    {
        const string nonFastStart = @"X:\3次\動画\WISE_Library\宇佐美玲奈\NCYF-023\hhd800.com@NCYF-023.mp4";
        const string fastStart = @"X:\3次\動画\WISE_Library\森日向子\PFES-058\hhd800.com@PFES-058.mp4";

        if (!File.Exists(nonFastStart) || !File.Exists(fastStart))
            return; // 開発機以外ではスキップ

        VideoFastStartService.IsFastStart(nonFastStart).Should().BeFalse();
        VideoFastStartService.IsFastStart(fastStart).Should().BeTrue();
    }

    // ffmpegを実際に呼び出してremuxが機能することを検証する。ffmpegが無い環境ではスキップする。
    [Trait("Category", "Integration")]
    [Fact]
    public async Task EnsureFastStartAsync_ShouldRemux_NonFastStartFileToFastStart()
    {
        const string ffmpegPath = @"C:\Users\nat\.gemini\antigravity\scratch\bin\ffmpeg.exe";
        if (!File.Exists(ffmpegPath)) return; // 開発機以外ではスキップ

        var testFile = Path.Combine(_tempDir, "smoketest.mp4");

        // +faststartを付けずに生成 = デフォルトでmoovが末尾になる合成動画
        var genInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -f lavfi -i testsrc=duration=1:size=320x240:rate=10 -c:v libx264 -pix_fmt yuv420p \"{testFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using (var gen = Process.Start(genInfo)!)
        {
            await gen.StandardError.ReadToEndAsync();
            await gen.WaitForExitAsync();
            gen.ExitCode.Should().Be(0);
        }

        VideoFastStartService.IsFastStart(testFile).Should().BeFalse("生成直後はmoovが末尾にあるはず");

        var svc = CreateService();
        await svc.EnsureFastStartAsync(testFile);

        File.Exists(testFile).Should().BeTrue("remux後も同じパスにファイルが存在するはず");
        VideoFastStartService.IsFastStart(testFile).Should().BeTrue("remux後はmoovが先頭にあるはず");
    }
}
