using System;
using System.Text.RegularExpressions;

namespace WISE.Domain.Services
{
    public class IdentifierParser
    {
        // 商業AV: IPX, SSIS, SONE, MIAA, MIDV, JUQ, PRED (例: IPX-001)
        private static readonly Regex CommercialRegex = new Regex(@"\b(IPX|SSIS|SONE|MIAA|MIDV|JUQ|PRED)-\d{3,}\b", RegexOptions.IgnoreCase);
        
        // FC2 / FC2-PPV (例: FC2-PPV-1234567, FC2-1234567)
        private static readonly Regex Fc2Regex = new Regex(@"\b(FC2-PPV|FC2)-\d+\b", RegexOptions.IgnoreCase);
        
        // 日付形式 (例: 100115-001)
        private static readonly Regex DateRegex = new Regex(@"\b\d{6}-\d{3,}\b", RegexOptions.IgnoreCase);

        public static string Parse(string filename)
        {
            var commercialMatch = CommercialRegex.Match(filename);
            if (commercialMatch.Success) return commercialMatch.Value.ToUpper();

            var fc2Match = Fc2Regex.Match(filename);
            if (fc2Match.Success) return fc2Match.Value.ToUpper();

            var dateMatch = DateRegex.Match(filename);
            if (dateMatch.Success) return dateMatch.Value;

            // Unknown Identifier (ランダムまたはファイル名ハッシュなどで扱うが、v1.0ではUNKN-GUIDとする)
            return $"UNKNOWN-{Guid.NewGuid().ToString().Substring(0, 8)}".ToUpper();
        }
    }
}
