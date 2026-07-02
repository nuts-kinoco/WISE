using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WISE.Infrastructure.Services;

/// <summary>
/// MP4/MOVコンテナの moov atom（再生に必須のタイミング/オフセット情報）が
/// ファイル先頭に配置されているか（faststart）を判定し、そうでなければ
/// ffmpeg でコンテナのみ書き換えて先頭に移動する（`-c copy` のためストリームの
/// 再エンコードは発生せず、ロスレス・高速）。
///
/// 背景: <c>VideoStreamCache</c> はファイル先頭32MBのみをメモリキャッシュする。
/// moov atomが末尾にある非faststartファイルは、moov読み取りのたびにキャッシュミスして
/// 巨大ファイル末尾への物理シークが発生し、再生開始の遅延や再生中の間欠的な引っかかりの
/// 原因になる（HDD上の数GB級ファイルで特に顕著）。インポート時に一度だけ変換しておくことで、
/// 以降の全ての再生アクセスでこの問題を構造的に回避する。
/// </summary>
public class VideoFastStartService
{
    private readonly string _ffmpegPath = @"C:\Users\nat\.gemini\antigravity\scratch\bin\ffmpeg.exe";
    private readonly ILogger<VideoFastStartService> _logger;

    // moov/ftyp/mdat による box 構造を持つコンテナのみ対象（mkv/avi/wmv 等は対象外）
    private static readonly string[] ApplicableExtensions = { ".mp4", ".m4v", ".mov" };

    // 先頭スキャンの上限（この範囲内で moov/mdat のどちらかが見つからなければ判定不能として諦める）
    private const int ScanLimitBytes = 8 * 1024 * 1024; // 8MB

    public VideoFastStartService(ILogger<VideoFastStartService> logger)
    {
        _logger = logger;
    }

    public bool IsApplicable(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return Array.IndexOf(ApplicableExtensions, ext) >= 0;
    }

    /// <summary>
    /// moov atom がファイル先頭側（mdatより前）にあるかどうかを、
    /// 先頭のトップレベルboxを順に読みながら判定する。ffprobe等の外部プロセスは使わず、
    /// 生バイトのbox headerのみを読む軽量な実装（巨大ファイルでも高速）。
    /// 判定不能（スキャン上限に達した等）の場合は null を返す。
    /// </summary>
    public static bool? IsFastStart(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[16];
            long pos = 0;

            while (pos < ScanLimitBytes && pos + 8 <= fs.Length)
            {
                fs.Seek(pos, SeekOrigin.Begin);
                int read = fs.Read(header, 0, 8);
                if (read < 8) break;

                uint size32 = ReadUInt32BigEndian(header, 0);
                string boxType = System.Text.Encoding.ASCII.GetString(header, 4, 4);

                long boxSize;
                long nextPos;
                if (size32 == 1)
                {
                    // 64-bit largesize: 続く8バイトが実サイズ
                    if (fs.Read(header, 8, 8) < 8) break;
                    boxSize = (long)ReadUInt64BigEndian(header, 8);
                    nextPos = pos + boxSize;
                }
                else if (size32 == 0)
                {
                    // このboxがファイル末尾まで続く（通常はmdatがこの形）
                    boxSize = fs.Length - pos;
                    nextPos = fs.Length;
                }
                else
                {
                    boxSize = size32;
                    nextPos = pos + boxSize;
                }

                if (boxType == "moov") return true;
                if (boxType == "mdat") return false;

                if (boxSize < 8 || nextPos <= pos) break; // 不正なbox、これ以上進めない
                pos = nextPos;
            }

            return null; // 判定不能
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 対象外の拡張子、または既にfaststart済みの場合は何もしない。
    /// 非faststartと判定された場合のみ、ffmpegでコンテナを書き換える。
    /// 失敗しても例外は投げず（インポート処理全体を止めないため）、ログのみ出力する。
    /// </summary>
    public async Task EnsureFastStartAsync(string filePath, CancellationToken ct = default)
    {
        if (!IsApplicable(filePath)) return;
        if (!File.Exists(_ffmpegPath))
        {
            _logger.LogDebug("[FastStart] ffmpeg not found at {Path}, skipping faststart check", _ffmpegPath);
            return;
        }

        bool? fastStart;
        try
        {
            fastStart = IsFastStart(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FastStart] Failed to inspect {File}", filePath);
            return;
        }

        if (fastStart != false) return; // true（既にOK）または null（判定不能）なら何もしない

        // 拡張子を維持したファイル名にする（.tmp 等の非標準拡張子だとffmpegが
        // 出力コンテナ形式を自動判定できずエラーになるため）
        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath);
        var tempPath = Path.Combine(dir, $"{nameWithoutExt}.faststart_tmp{ext}");
        try
        {
            _logger.LogInformation("[FastStart] Remuxing (moov atom not at front): {File}", Path.GetFileName(filePath));

            var arguments = $"-y -i \"{filePath}\" -c copy -movflags +faststart \"{tempPath}\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogWarning("[FastStart] Failed to start ffmpeg process for {File}", filePath);
                return;
            }

            var errorTask = process.StandardError.ReadToEndAsync(ct);
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var error = await errorTask;
            _ = await outputTask;

            if (process.ExitCode != 0 || !File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                _logger.LogWarning("[FastStart] ffmpeg remux failed for {File} (exit={Exit}): {Error}",
                    filePath, process.ExitCode, error);
                TryDelete(tempPath);
                return;
            }

            // 元ファイルと入れ替える（File.Replace はバックアップなしの単純上書きより原子的）
            File.Replace(tempPath, filePath, null);
            _logger.LogInformation("[FastStart] Remux complete: {File}", Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FastStart] Unexpected error remuxing {File}", filePath);
            TryDelete(tempPath);
        }
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogDebug(ex, "[FastStart] Failed to clean up temp file {Path}", path); }
    }

    private static uint ReadUInt32BigEndian(byte[] buffer, int offset)
        => (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3]);

    private static ulong ReadUInt64BigEndian(byte[] buffer, int offset)
    {
        ulong high = ReadUInt32BigEndian(buffer, offset);
        ulong low = ReadUInt32BigEndian(buffer, offset + 4);
        return (high << 32) | low;
    }
}
