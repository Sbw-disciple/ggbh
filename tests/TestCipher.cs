using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

static class SaveCipher
{
    public const string ValidationKey = "2a;ad.,&fSf^SX.,:12@D";
    public const string CacheEncryPassword = "5:.A%KL;,.?<aH._=-/DF4s";
    static readonly byte[] Header = Encoding.UTF8.GetBytes(ValidationKey).Select(b => (byte)(b ^ 3)).ToArray();
    static readonly byte[] KeyBytes = Encoding.ASCII.GetBytes(CacheEncryPassword);
    public static bool LooksEncrypted(byte[] b)
    {
        if (b.Length < Header.Length) return false;
        for (int i = 0; i < Header.Length; i++) if (b[i] != Header[i]) return false;
        return true;
    }
    public static byte[] Decrypt(byte[] b)
    {
        int len = b.Length - Header.Length;
        byte[] o = new byte[len];
        for (int i = 0; i < len; i++) o[i] = unchecked((byte)(b[Header.Length + i] - KeyBytes[i % KeyBytes.Length]));
        return o;
    }
    public static byte[] Encrypt(byte[] b)
    {
        byte[] o = new byte[b.Length];
        for (int i = 0; i < b.Length; i++) o[i] = unchecked((byte)(b[i] + KeyBytes[i % KeyBytes.Length]));
        byte[] r = new byte[Header.Length + o.Length];
        Buffer.BlockCopy(Header, 0, r, 0, Header.Length);
        Buffer.BlockCopy(o, 0, r, Header.Length, o.Length);
        return r;
    }
    public static byte[] GzipDecompress(byte[] b)
    {
        using (var ms = new MemoryStream(b))
        using (var gz = new GZipStream(ms, CompressionMode.Decompress))
        using (var o = new MemoryStream()) { gz.CopyTo(o); return o.ToArray(); }
    }
    public static byte[] GzipCompress(byte[] b)
    {
        using (var o = new MemoryStream())
        { using (var gz = new GZipStream(o, CompressionLevel.Optimal, true)) gz.Write(b, 0, b.Length); return o.ToArray(); }
    }
    public static string DecryptName(string name)
    {
        string b64 = name.Replace("@", "/");
        try
        {
            byte[] data = Convert.FromBase64String(b64);
            byte[] hash = MD5.Create().ComputeHash(KeyBytes);
            using (var des = DES.Create())
            {
                des.Mode = CipherMode.CBC; des.Padding = PaddingMode.PKCS7;
                des.Key = hash.Take(8).ToArray(); des.IV = hash.Skip(8).Take(8).ToArray();
                var dec = des.CreateDecryptor();
                byte[] plain = dec.TransformFinalBlock(data, 0, data.Length);
                return Encoding.UTF8.GetString(plain);
            }
        }
        catch { return null; }
    }
}

static class Test
{
    static int Main(string[] args)
    {
        // 1) 真实存档文件名解密
        string[] names = {
            "O33a@9fkh++YwffVNeV1RA==.cache",
            "eMZk7avfjaaSiFBwLwtZHg==.cache",
            "1V5i793M2Ol7H@CBpcLu+A==.cache",
            "soegnXJ65qU3ZS4O9xLDgg==.cache",
            "W5hmZoonQsc=.cache",
            "soegnXJ65qUCT4iKXaSMyA==.cache",
            "soegnXJ65qUWQ4n8nKNSZPdGJBGYyoQB.cache",
            "soegnXJ65qVvJ6@ttzq7KFMeZbNxECzf.cache",
            "soegnXJ65qXeZOR5PIl6Kg==.cache",
            "ay+Buyfz1LSFydRUQcbLfg==.cache",
            "LC9TKXHyJ2XWT6Fdkgxs+A==.cache",
            "O33a@9fkh+8yY0g1c2@89Q==.cache",
            "O33a@9fkh+9d5nka84Pi7w==.cache"
        };
        Console.WriteLine("=== 文件名解密 ===");
        foreach (var n in names) Console.WriteLine(n + "  ->  " + SaveCipher.DecryptName(n.Replace(".cache", "")));

        // 2) 真实文件解密 + 往返
        string dir = args.Length > 0 ? args[0] : null;
        if (dir != null)
        {
            Console.WriteLine("=== 真实文件往返 ===");
            foreach (string f in Directory.GetFiles(dir))
            {
                string n = Path.GetFileName(f);
                if (n.EndsWith(".json") || n == "CacheFixVer") continue;
                byte[] raw = File.ReadAllBytes(f);
                if (!SaveCipher.LooksEncrypted(raw)) { Console.WriteLine("skip " + n); continue; }
                byte[] plain = SaveCipher.GzipDecompress(SaveCipher.Decrypt(raw));
                byte[] reenc = SaveCipher.Encrypt(SaveCipher.GzipCompress(plain));
                bool same = raw.Length == reenc.Length && raw.SequenceEqual(reenc);
                Console.WriteLine(n + "  明文 " + plain.Length + " 字节  往返 " + (same ? "OK" : "DIFF"));
            }
        }
        return 0;
    }
}
