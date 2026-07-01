using System;
using System.Text.RegularExpressions;

class Program {
    static void Main() {
        var rx = new Regex(@""\b([A-Z]{2,6})-(\d{2,})\b"", RegexOptions.IgnoreCase);
        var match = rx.Match(""hhd800.com@EKDV-775.mp4"");
        Console.WriteLine($""Match: {match.Success}, Value: {match.Value}"");
    }
}
