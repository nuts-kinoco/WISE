using System;
using System.Security.Cryptography;
using System.Text;

class Program
{
    static void Main()
    {
        long[] sizes = { 0, 1024, 2048, 123456, 1234567, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 2000, 3000, 4000, 5000, 1234, 5678, 9012 };
        foreach(var size in sizes) {
            var input = $""hhd800.com@EKDV-775.mp4:{size}"";
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            var hashHex = Convert.ToHexString(hashBytes).Substring(0, 8);
            if (hashHex == ""F2B792AE"") {
                Console.WriteLine($""FOUND: size={size}"");
            }
        }
    }
}
