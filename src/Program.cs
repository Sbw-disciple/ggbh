using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace GhbgEditor
{
    // ---------- 存档加解密 ----------
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
            for (int i = 0; i < len; i++)
                o[i] = unchecked((byte)(b[Header.Length + i] - KeyBytes[i % KeyBytes.Length]));
            return o;
        }

        public static byte[] Encrypt(byte[] b)
        {
            byte[] o = new byte[b.Length];
            for (int i = 0; i < b.Length; i++)
                o[i] = unchecked((byte)(b[i] + KeyBytes[i % KeyBytes.Length]));
            byte[] r = new byte[Header.Length + o.Length];
            Buffer.BlockCopy(Header, 0, r, 0, Header.Length);
            Buffer.BlockCopy(o, 0, r, Header.Length, o.Length);
            return r;
        }

        public static byte[] GzipDecompress(byte[] b)
        {
            using (var ms = new MemoryStream(b))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var o = new MemoryStream())
            { gz.CopyTo(o); return o.ToArray(); }
        }

        public static byte[] GzipCompress(byte[] b)
        {
            using (var o = new MemoryStream())
            {
                using (var gz = new GZipStream(o, CompressionLevel.Optimal, true))
                    gz.Write(b, 0, b.Length);
                return o.ToArray();
            }
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

        public static string Json(byte[] b) { return Encoding.UTF8.GetString(b); }
        public static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s); }
    }

    // ---------- 替换规则 ----------
    class ReplaceRule
    {
        public string Pattern;   // 支持正则，解析失败时按纯文本
        public string Replace;
        public string Note;
    }

    // ---------- 主窗体 ----------
    public class MainForm : Form
    {
        TextBox txtDir, txtLog;
        CheckBox chkTop;
        DataGridView grid;

        public MainForm()
        {
            Text = "鬼谷八荒存档修改器";
            StartPosition = FormStartPosition.Manual;
            Location = new Point(80, 80);
            Size = new Size(1080, 660);
            TopMost = true;
            BackColor = Color.FromArgb(40, 40, 46);
            ForeColor = Color.WhiteSmoke;
            MinimumSize = new Size(860, 480);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(8),
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            // 第一行：目录选择
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = BackColor
            };
            var lbl = MakeLabel("存档目录:");
            txtDir = new TextBox { Width = 500, BackColor = Color.FromArgb(30, 30, 34), ForeColor = Color.White };
            var btnBrowse = MakeButton("浏览");
            var btnLoad = MakeButton("① 读取存档");
            var btnSave = MakeButton("② 写入存档");
            var btnBackup = MakeButton("备份存档");
            btnBrowse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "选择存档目录（例如 ...\\CacheData\\2_XXXXXX）" })
                    if (d.ShowDialog() == DialogResult.OK) txtDir.Text = d.SelectedPath;
            };
            btnLoad.Click += (s, e) => LoadSave();
            btnSave.Click += (s, e) => WriteSave();
            btnBackup.Click += (s, e) => BackupSave();
            bar.Controls.AddRange(new Control[] { lbl, txtDir, btnBrowse, btnLoad, btnSave, btnBackup });
            root.Controls.Add(bar, 0, 0);

            // 第二行：置顶
            chkTop = new CheckBox { Text = "悬浮窗置顶", Checked = true, AutoSize = true, ForeColor = ForeColor, BackColor = BackColor };
            chkTop.CheckedChanged += (s, e) => TopMost = chkTop.Checked;
            root.Controls.Add(chkTop, 0, 1);

            // 第三行：规则表
            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.FromArgb(30, 30, 34),
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(50, 50, 58);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.DefaultCellStyle.BackColor = Color.FromArgb(30, 30, 34);
            grid.DefaultCellStyle.ForeColor = Color.White;
            grid.Columns.Add("colOld", "查找内容（支持正则）");
            grid.Columns.Add("colNew", "替换为");
            grid.Columns.Add("colNote", "说明");
            grid.Columns[0].FillWeight = 45;
            grid.Columns[1].FillWeight = 35;
            grid.Columns[2].FillWeight = 20;
            root.Controls.Add(grid, 0, 2);

            // 第四行：快捷按钮
            var quick = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = BackColor
            };
            var q1 = MakeButton("灵石 999999"); q1.Click += (s, e) => AddRow(@"\[1,10001,\d+\]", "[1,10001,999999]", "灵石改为999999");
            var q2 = MakeButton("灵石 1亿"); q2.Click += (s, e) => AddRow(@"\[1,10001,\d+\]", "[1,10001,100000000]", "灵石改为1亿");
            var q3 = MakeButton("宗门贡献 999999"); q3.Click += (s, e) => AddRow(@"\[1,10002,\d+\]", "[1,10002,999999]", "宗门贡献");
            var q4 = MakeButton("难度改地狱"); q4.Click += (s, e) => AddRow(@"""gameLevel"":\d+", "\"gameLevel\":5", "5=地狱");
            var q5 = MakeButton("寿命 9999"); q5.Click += (s, e) => AddRow(@"""y"":\d+", "\"y\":9999", "玩家寿命(慎用)");
            quick.Controls.AddRange(new Control[] { q1, q2, q3, q4, q5 });
            root.Controls.Add(quick, 0, 3);

            // 第五行：日志
            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(24, 24, 28),
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9f)
            };
            root.Controls.Add(txtLog, 0, 4);
        }

        Label MakeLabel(string s)
        {
            return new Label { Text = s, AutoSize = true, ForeColor = ForeColor, BackColor = BackColor, Padding = new Padding(0, 8, 0, 0) };
        }

        Button MakeButton(string s)
        {
            return new Button { Text = s, AutoSize = true, BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        }

        void AddRow(string p, string r, string note)
        {
            grid.Rows.Add(p, r, note);
            Log("已添加规则: " + p + "  ->  " + r);
        }

        void Log(string s)
        {
            if (txtLog.IsDisposed) return;
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }

        string GetSaveDir()
        {
            string p = txtDir.Text.Trim();
            if (p.Length == 0) { MessageBox.Show("先选择存档目录"); return null; }
            if (!Directory.Exists(p)) { MessageBox.Show("目录不存在: " + p); return null; }
            return p;
        }

        void BackupSave()
        {
            string dir = GetSaveDir(); if (dir == null) return;
            string parent = Path.GetDirectoryName(dir.TrimEnd('\\', '/')) ?? ".";
            string bak = Path.Combine(parent, Path.GetFileName(dir) + "_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try
            {
                Directory.CreateDirectory(bak);
                foreach (string f in Directory.GetFiles(dir))
                    File.Copy(f, Path.Combine(bak, Path.GetFileName(f)), true);
                Log("已备份到: " + bak);
            }
            catch (Exception ex) { Log("备份失败: " + ex.Message); }
        }

        void LoadSave()
        {
            string dir = GetSaveDir(); if (dir == null) return;
            Log("=== 读取存档: " + dir + " ===");
            int ok = 0, fail = 0;
            foreach (string f in Directory.GetFiles(dir))
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".json")) continue; // 已解密的跳过
                byte[] raw = File.ReadAllBytes(f);
                if (!SaveCipher.LooksEncrypted(raw)) { Log("跳过(未加密): " + name); continue; }
                try
                {
                    byte[] plain = SaveCipher.GzipDecompress(SaveCipher.Decrypt(raw));
                    string json = SaveCipher.Json(plain);
                    string real = SaveCipher.DecryptName(name);
                    string jsonFile = Path.Combine(dir, name + ".json");
                    File.WriteAllText(jsonFile, json, new UTF8Encoding(false));
                    ok++;
                    Log(string.Format("解密 {0}  ({1}) -> {2}  ({3} 字节)", name, real ?? "?", Path.GetFileName(jsonFile), json.Length));
                }
                catch (Exception ex) { fail++; Log("解密失败 " + name + ": " + ex.Message); }
            }
            Log(string.Format("完成: 成功 {0}, 失败 {1}。规则列表填好后点【写入存档】。", ok, fail));
        }

        string ApplyRule(string text, ReplaceRule rule)
        {
            try
            {
                return Regex.Replace(text, rule.Pattern, rule.Replace);
            }
            catch
            {
                // 正则解析失败时按纯文本替换
                return text.Replace(rule.Pattern, rule.Replace);
            }
        }

        void WriteSave()
        {
            string dir = GetSaveDir(); if (dir == null) return;
            Log("=== 写入存档: " + dir + " ===");

            var rules = new List<ReplaceRule>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                string p = Convert.ToString(row.Cells[0].Value);
                string r = Convert.ToString(row.Cells[1].Value);
                string note = Convert.ToString(row.Cells[2].Value);
                if (string.IsNullOrEmpty(p)) continue;
                rules.Add(new ReplaceRule { Pattern = p, Replace = r ?? "", Note = note ?? "" });
            }
            if (rules.Count == 0) { Log("没有替换规则，未做任何修改。"); return; }

            int hit = 0, rep = 0;
            foreach (string jsonFile in Directory.GetFiles(dir, "*.json"))
            {
                string json = File.ReadAllText(jsonFile);
                string original = json;
                foreach (var rule in rules)
                    json = ApplyRule(json, rule);
                if (json != original)
                {
                    File.WriteAllText(jsonFile, json, new UTF8Encoding(false));
                    hit++;
                    rep++;
                    Log("已修改 " + Path.GetFileName(jsonFile));
                }
            }
            Log(string.Format("修改了 {0} 个文件。", hit));

            // 回写 .cache
            int encOk = 0, encFail = 0;
            foreach (string jsonFile in Directory.GetFiles(dir, "*.json"))
            {
                string name = Path.GetFileName(jsonFile);           // xxx.cache.json
                if (!name.EndsWith(".cache.json")) { Log("跳过回写: " + name); continue; }
                string cacheName = name.Substring(0, name.Length - 5); // xxx.cache
                string cachePath = Path.Combine(dir, cacheName);
                try
                {
                    byte[] jsonBytes = File.ReadAllBytes(jsonFile);
                    byte[] enc = SaveCipher.Encrypt(SaveCipher.GzipCompress(jsonBytes));
                    File.WriteAllBytes(cachePath, enc);
                    encOk++;
                }
                catch (Exception ex) { encFail++; Log("回写失败 " + cacheName + ": " + ex.Message); }
            }
            Log(string.Format("回写完成: 成功 {0}, 失败 {1}。", encOk, encFail));
            Log("提示: 游戏内用【导入存档】即可生效；原 .json 文件可保留或手动删除。");
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
