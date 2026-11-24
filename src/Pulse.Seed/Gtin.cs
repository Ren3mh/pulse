using System.Security.Cryptography;
using System.Text;

namespace Pulse.Seed;

// Fake but valid EAN-13 barcodes. Prefix 20-29 is GS1's in-store range, so
// they can never clash with a real product.
public static class Gtin
{
    public static string FromSku(string sku)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sku));
        var body = "20" + (BitConverter.ToUInt64(hash) % 10_000_000_000UL).ToString("D10");
        return body + CheckDigit(body);
    }

    public static int CheckDigit(string first12)
    {
        var sum = first12.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return (10 - sum % 10) % 10;
    }
}
