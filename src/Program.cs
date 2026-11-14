using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
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
    }

    // ---------- 替换规则 ----------
    class ReplaceRule
    {
        public string Pattern;
        public string Replace;
        public string Note;
    }

    // ---------- 内存扫描与读写 ----------
    static class MemEditor
    {
        const uint PROCESS_VM_READ = 0x10;
        const uint PROCESS_VM_WRITE = 0x20;
        const uint PROCESS_VM_OPERATION = 0x8;
        const uint PROCESS_QUERY_INFORMATION = 0x400;
        const uint MEM_COMMIT = 0x1000;
        const uint PAGE_GUARD = 0x100;

        [StructLayout(LayoutKind.Sequential)]
        struct MBI64
        {
            public ulong BaseAddress;
            public ulong AllocationBase;
            public uint AllocationProtect;
            public uint Align1;
            public ulong RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
            public uint Align2;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MBI32
        {
            public uint BaseAddress;
            public uint AllocationBase;
            public uint AllocationProtect;
            public uint RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", EntryPoint = "VirtualQueryEx", SetLastError = true)]
        static extern int VirtualQueryEx64(IntPtr h, IntPtr addr, out MBI64 mbi, int len);

        [DllImport("kernel32.dll", EntryPoint = "VirtualQueryEx", SetLastError = true)]
        static extern int VirtualQueryEx32(IntPtr h, IntPtr addr, out MBI32 mbi, int len);

        public static IntPtr Open(int pid)
        {
            return OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, false, pid);
        }

        public static void Close(IntPtr h)
        {
            if (h != IntPtr.Zero) CloseHandle(h);
        }

        static bool IsReadable(uint protect)
        {
            if ((protect & PAGE_GUARD) != 0) return false;
            uint p = protect & 0xFF;
            switch (p)
            {
                case 0x02: case 0x04: case 0x08:
                case 0x20: case 0x40: case 0x80:
                    return true;
                default:
                    return false;
            }
        }

        public static int TypeSize(string type)
        {
            if (type == "int64" || type == "double") return 8;
            return 4;
        }

        static long ParseLong(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.Parse(s.Substring(2), System.Globalization.NumberStyles.HexNumber);
            return long.Parse(s);
        }

        static int ParseInt(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return (int)uint.Parse(s.Substring(2), System.Globalization.NumberStyles.HexNumber);
            return int.Parse(s);
        }

        public static byte[] ToBytes(string type, string value)
        {
            if (type == "int32") return BitConverter.GetBytes(ParseInt(value));
            if (type == "int64") return BitConverter.GetBytes(ParseLong(value));
            if (type == "float") return BitConverter.GetBytes(float.Parse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture));
            if (type == "double") return BitConverter.GetBytes(double.Parse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture));
            throw new ArgumentException("unknown type: " + type);
        }

        static object BytesToValue(byte[] b, string type)
        {
            if (type == "int32") return BitConverter.ToInt32(b, 0);
            if (type == "int64") return BitConverter.ToInt64(b, 0);
            if (type == "float") return BitConverter.ToSingle(b, 0);
            if (type == "double") return BitConverter.ToDouble(b, 0);
            return null;
        }

        public static string ReadValue(IntPtr h, IntPtr addr, string type)
        {
            int size = TypeSize(type);
            byte[] buf = new byte[size];
            int read;
            if (!ReadProcessMemory(h, addr, buf, size, out read) || read != size) return "?";
            object v = BytesToValue(buf, type);
            return v == null ? "?" : v.ToString();
        }

        public static bool WriteValue(IntPtr h, IntPtr addr, string type, string value)
        {
            byte[] b;
            try { b = ToBytes(type, value); }
            catch { return false; }
            int written;
            return WriteProcessMemory(h, addr, b, b.Length, out written) && written == b.Length;
        }

        static int QueryRegion(IntPtr h, long addr, out long baseAddr, out long regionSize, out uint state, out uint protect, out uint type)
        {
            if (IntPtr.Size == 8)
            {
                MBI64 mbi;
                int sz = VirtualQueryEx64(h, (IntPtr)addr, out mbi, Marshal.SizeOf(typeof(MBI64)));
                baseAddr = sz == 0 ? 0 : (long)mbi.BaseAddress;
                regionSize = sz == 0 ? 0 : (long)mbi.RegionSize;
                state = sz == 0 ? 0 : mbi.State;
                protect = sz == 0 ? 0 : mbi.Protect;
                type = sz == 0 ? 0 : mbi.Type;
                return sz;
            }
            else
            {
                MBI32 mbi;
                int sz = VirtualQueryEx32(h, (IntPtr)addr, out mbi, Marshal.SizeOf(typeof(MBI32)));
                baseAddr = sz == 0 ? 0 : mbi.BaseAddress;
                regionSize = sz == 0 ? 0 : mbi.RegionSize;
                state = sz == 0 ? 0 : mbi.State;
                protect = sz == 0 ? 0 : mbi.Protect;
                type = sz == 0 ? 0 : mbi.Type;
                return sz;
            }
        }

        static int IndexOfPattern(byte[] buf, int start, byte[] pattern)
        {
            int max = buf.Length - pattern.Length;
            for (int i = start; i <= max; i++)
            {
                bool ok = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buf[i + j] != pattern[j]) { ok = false; break; }
                }
                if (ok) return i;
            }
            return -1;
        }

        // 首次扫描：遍历可读内存，返回匹配地址
        public static List<IntPtr> FirstScan(IntPtr h, string type, string value, Action<string> log)
        {
            var results = new List<IntPtr>();
            byte[] pattern;
            try { pattern = ToBytes(type, value); }
            catch (Exception ex) { log("数值解析失败: " + ex.Message); return results; }

            long addr = 0x10000;
            long max = IntPtr.Size == 8 ? 0x7FFFFFFFFFFF : 0x7FFFFFFF;
            long scannedRegions = 0;
            const long CHUNK = 1024 * 1024;

            while (addr < max)
            {
                long baseAddr, regionSize;
                uint state, protect, regType;
                if (QueryRegion(h, addr, out baseAddr, out regionSize, out state, out protect, out regType) == 0)
                    break;
                if (regionSize <= 0)
                {
                    addr = baseAddr + 0x1000;
                    continue;
                }
                if (state == MEM_COMMIT && IsReadable(protect))
                {
                    long remaining = regionSize;
                    long off = 0;
                    while (remaining > 0)
                    {
                        int readSize = (int)Math.Min(remaining, CHUNK);
                        byte[] buf = new byte[readSize];
                        int read;
                        if (ReadProcessMemory(h, (IntPtr)(baseAddr + off), buf, readSize, out read) && read > 0)
                        {
                            int idx = 0;
                            while ((idx = IndexOfPattern(buf, idx, pattern)) >= 0)
                            {
                                results.Add((IntPtr)(baseAddr + off + idx));
                                idx += 1;
                            }
                        }
                        remaining -= readSize;
                        off += readSize;
                    }
                }
                scannedRegions++;
                if (scannedRegions % 2000 == 0) log("已扫描 " + results.Count + " 个候选...");
                addr = baseAddr + regionSize;
                if (addr <= 0) break;
            }
            return results;
        }

        // 筛选：条件 exact/increased/decreased/changed/unchanged
        public static List<IntPtr> FilterScan(IntPtr h, List<IntPtr> addresses, Dictionary<long, byte[]> lastValues, string type, string condition, string value, Action<string> log)
        {
            int size = TypeSize(type);
            byte[] target = null;
            if (condition == "exact")
            {
                try { target = ToBytes(type, value); }
                catch { return addresses; }
            }

            var kept = new List<IntPtr>();
            var newLast = new Dictionary<long, byte[]>();
            int checkedCount = 0;
            foreach (IntPtr a in addresses)
            {
                checkedCount++;
                if (checkedCount % 20000 == 0) log("筛选进度 " + checkedCount + "/" + addresses.Count);

                byte[] cur = new byte[size];
                int read;
                if (!ReadProcessMemory(h, a, cur, size, out read) || read != size) continue;

                byte[] last;
                bool hasLast = lastValues.TryGetValue((long)a, out last);

                bool keep = false;
                switch (condition)
                {
                    case "exact":
                        keep = cur.SequenceEqual(target);
                        break;
                    case "changed":
                        keep = hasLast && !cur.SequenceEqual(last);
                        break;
                    case "unchanged":
                        keep = hasLast && cur.SequenceEqual(last);
                        break;
                    case "increased":
                    case "decreased":
                        if (hasLast)
                        {
                            object a0 = BytesToValue(cur, type);
                            object b0 = BytesToValue(last, type);
                            if (a0 != null && b0 != null)
                            {
                                int c = ((IComparable)a0).CompareTo(b0);
                                keep = condition == "increased" ? c > 0 : c < 0;
                            }
                        }
                        break;
                }
                if (keep)
                {
                    kept.Add(a);
                    newLast[(long)a] = cur;
                }
            }
            return kept;
        }
    }

    // ---------- 主窗体 ----------
    public class MainForm : Form
    {
        // 全局热键 Ctrl+Alt+G
        const int WM_HOTKEY = 0x0312;
        const uint MOD_CONTROL = 0x0002;
        const uint MOD_ALT = 0x0001;
        const uint VK_G = 0x47;
        const int HOTKEY_ID = 0xC0DE;

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 存档 Tab
        TextBox txtDir, txtLog;
        CheckBox chkTop;
        DataGridView gridSave;

        // 内存 Tab
        ComboBox cmbProc, cmbType;
        TextBox txtValue, txtExact, txtStatus;
        Button btnAttach;
        DataGridView gridResults, gridMemItems;
        IntPtr memHandle = IntPtr.Zero;
        List<IntPtr> scanAddresses = new List<IntPtr>();
        Dictionary<long, byte[]> scanLast = new Dictionary<long, byte[]>();
        string scanType = "int32";
        Timer lockTimer;

        public MainForm()
        {
            Text = "鬼谷八荒修改器";
            StartPosition = FormStartPosition.Manual;
            Location = new Point(60, 60);
            Size = new Size(1120, 700);
            TopMost = true;
            BackColor = Color.FromArgb(40, 40, 46);
            ForeColor = Color.WhiteSmoke;
            MinimumSize = new Size(900, 560);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            Controls.Add(tabs);

            var tabSave = new TabPage("存档修改（改完需导入存档）");
            var tabMem = new TabPage("内存修改（实时生效，无需重启游戏）");
            tabs.TabPages.Add(tabSave);
            tabs.TabPages.Add(tabMem);

            BuildSaveTab(tabSave);
            BuildMemTab(tabMem);

            lockTimer = new Timer();
            lockTimer.Interval = 400;
            lockTimer.Tick += (s, e) => LockTick();
            lockTimer.Start();

            // 注册全局热键 Ctrl+Alt+G：隐藏时唤出，显示时置顶
            if (!RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_G))
                Log("热键 Ctrl+Alt+G 注册失败（可能被其它程序占用）");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                ToggleVisible();
                return;
            }
            base.WndProc(ref m);
        }

        void ToggleVisible()
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                Hide();
                Log("已隐藏，按 Ctrl+Alt+G 重新唤出");
            }
            else
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                TopMost = chkTop.Checked;
            }
        }

        // ================= 存档 Tab =================
        void BuildSaveTab(TabPage page)
        {
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
            page.Controls.Add(root);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = BackColor };
            var lbl = MakeLabel("存档目录:");
            txtDir = new TextBox { Width = 460, BackColor = Color.FromArgb(30, 30, 34), ForeColor = Color.White };
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

            chkTop = new CheckBox { Text = "悬浮窗置顶", Checked = true, AutoSize = true, ForeColor = ForeColor, BackColor = BackColor };
            chkTop.CheckedChanged += (s, e) => TopMost = chkTop.Checked;
            root.Controls.Add(chkTop, 0, 1);

            gridSave = NewGrid();
            gridSave.Columns.Add("colOld", "查找内容（支持正则）");
            gridSave.Columns.Add("colNew", "替换为");
            gridSave.Columns.Add("colNote", "说明");
            gridSave.Columns[0].FillWeight = 45;
            gridSave.Columns[1].FillWeight = 35;
            gridSave.Columns[2].FillWeight = 20;
            root.Controls.Add(gridSave, 0, 2);

            var quick = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = BackColor };
            var q1 = MakeButton("灵石 999999"); q1.Click += (s, e) => AddSaveRow(@"\[1,10001,\d+\]", "[1,10001,999999]", "灵石改999999");
            var q2 = MakeButton("灵石 1亿"); q2.Click += (s, e) => AddSaveRow(@"\[1,10001,\d+\]", "[1,10001,100000000]", "灵石改1亿");
            var q3 = MakeButton("宗门贡献 999999"); q3.Click += (s, e) => AddSaveRow(@"\[1,10002,\d+\]", "[1,10002,999999]", "宗门贡献");
            var q4 = MakeButton("难度改地狱"); q4.Click += (s, e) => AddSaveRow(@"""gameLevel"":\d+", "\"gameLevel\":5", "5=地狱");
            var q5 = MakeButton("寿命 9999"); q5.Click += (s, e) => AddSaveRow(@"""y"":\d+", "\"y\":9999", "玩家寿命(慎用)");
            quick.Controls.AddRange(new Control[] { q1, q2, q3, q4, q5 });
            root.Controls.Add(quick, 0, 3);

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

        void AddSaveRow(string p, string r, string note)
        {
            gridSave.Rows.Add(p, r, note);
            Log("已添加规则: " + p + "  ->  " + r);
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
                if (name.EndsWith(".json")) continue;
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
            Log(string.Format("完成: 成功 {0}, 失败 {1}。规则填好后点【写入存档】。", ok, fail));
        }

        string ApplySaveRule(string text, ReplaceRule rule)
        {
            try { return Regex.Replace(text, rule.Pattern, rule.Replace); }
            catch { return text.Replace(rule.Pattern, rule.Replace); }
        }

        void WriteSave()
        {
            string dir = GetSaveDir(); if (dir == null) return;
            Log("=== 写入存档: " + dir + " ===");

            var rules = new List<ReplaceRule>();
            foreach (DataGridViewRow row in gridSave.Rows)
            {
                if (row.IsNewRow) continue;
                string p = Convert.ToString(row.Cells[0].Value);
                string r = Convert.ToString(row.Cells[1].Value);
                string note = Convert.ToString(row.Cells[2].Value);
                if (string.IsNullOrEmpty(p)) continue;
                rules.Add(new ReplaceRule { Pattern = p, Replace = r ?? "", Note = note ?? "" });
            }
            if (rules.Count == 0) { Log("没有替换规则，未做任何修改。"); return; }

            int hit = 0;
            foreach (string jsonFile in Directory.GetFiles(dir, "*.json"))
            {
                string json = File.ReadAllText(jsonFile);
                string original = json;
                foreach (var rule in rules) json = ApplySaveRule(json, rule);
                if (json != original)
                {
                    File.WriteAllText(jsonFile, json, new UTF8Encoding(false));
                    hit++;
                    Log("已修改 " + Path.GetFileName(jsonFile));
                }
            }
            Log(string.Format("修改了 {0} 个文件。", hit));

            int encOk = 0, encFail = 0;
            foreach (string jsonFile in Directory.GetFiles(dir, "*.json"))
            {
                string name = Path.GetFileName(jsonFile);
                if (!name.EndsWith(".cache.json")) continue;
                string cacheName = name.Substring(0, name.Length - 5);
                string cachePath = Path.Combine(dir, cacheName);
                try
                {
                    byte[] enc = SaveCipher.Encrypt(SaveCipher.GzipCompress(File.ReadAllBytes(jsonFile)));
                    File.WriteAllBytes(cachePath, enc);
                    encOk++;
                }
                catch (Exception ex) { encFail++; Log("回写失败 " + cacheName + ": " + ex.Message); }
            }
            Log(string.Format("回写完成: 成功 {0}, 失败 {1}。游戏内【导入存档】生效。", encOk, encFail));
        }

        // ================= 内存 Tab =================
        void BuildMemTab(TabPage page)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(8), BackColor = BackColor };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            page.Controls.Add(root);

            // 行1：进程选择
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = BackColor };
            var lblProc = MakeLabel("游戏进程:");
            cmbProc = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 30, 34), ForeColor = Color.White };
            var btnRefresh = MakeButton("刷新");
            btnAttach = MakeButton("附加进程");
            txtStatus = new TextBox { Width = 380, ReadOnly = true, BackColor = Color.FromArgb(24, 24, 28), ForeColor = Color.LightYellow };
            btnRefresh.Click += (s, e) => RefreshProc();
            btnAttach.Click += (s, e) => AttachProc();
            bar.Controls.AddRange(new Control[] { lblProc, cmbProc, btnRefresh, btnAttach, txtStatus });
            root.Controls.Add(bar, 0, 0);

            // 行2：首次扫描
            var scan = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = BackColor };
            var lblType = MakeLabel("数值类型:");
            cmbType = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 30, 34), ForeColor = Color.White };
            cmbType.Items.AddRange(new object[] { "int32", "float", "int64", "double" });
            cmbType.SelectedIndex = 0;
            cmbType.SelectedIndexChanged += (s, e) => { scanType = Convert.ToString(cmbType.SelectedItem); };
            var lblValue = MakeLabel("当前数值:");
            txtValue = new TextBox { Width = 160, BackColor = Color.FromArgb(30, 30, 34), ForeColor = Color.White };
            var btnFirst = MakeButton("首次扫描");
            var lblHint = MakeLabel("  （游戏里先记下要改的数值，填进来）");
            lblHint.ForeColor = Color.Gray;
            btnFirst.Click += (s, e) => DoFirstScan();
            scan.Controls.AddRange(new Control[] { lblType, cmbType, lblValue, txtValue, btnFirst, lblHint });
            root.Controls.Add(scan, 0, 1);

            // 行3：筛选
            var filter = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = BackColor };
            var lblFilter = MakeLabel("筛选:");
            txtExact = new TextBox { Width = 160, BackColor = Color.FromArgb(30, 30, 34), ForeColor = Color.White };
            var btnExact = MakeButton("精确值");
            var btnInc = MakeButton("增大了");
            var btnDec = MakeButton("减小了");
            var btnChanged = MakeButton("改变了");
            var btnUnchanged = MakeButton("未改变");
            var lblFilterHint = MakeLabel("   （在游戏里让数值变化一次，再点对应条件）");
            lblFilterHint.ForeColor = Color.Gray;
            btnExact.Click += (s, e) => DoFilter("exact");
            btnInc.Click += (s, e) => DoFilter("increased");
            btnDec.Click += (s, e) => DoFilter("decreased");
            btnChanged.Click += (s, e) => DoFilter("changed");
            btnUnchanged.Click += (s, e) => DoFilter("unchanged");
            filter.Controls.AddRange(new Control[] { lblFilter, txtExact, btnExact, btnInc, btnDec, btnChanged, btnUnchanged, lblFilterHint });
            root.Controls.Add(filter, 0, 2);

            // 行4：结果 + 修改项
            var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor };
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.Controls.Add(mid, 0, 3);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = BackColor };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var lblRes = MakeLabel("搜索结果（地址 · 当前值）");
            gridResults = NewGrid();
            gridResults.MultiSelect = true;
            gridResults.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridResults.Columns.Add("colAddr", "地址");
            gridResults.Columns.Add("colVal", "当前值");
            gridResults.Columns[0].FillWeight = 60;
            gridResults.Columns[1].FillWeight = 40;
            left.Controls.Add(lblRes, 0, 0);
            left.Controls.Add(gridResults, 0, 1);
            mid.Controls.Add(left, 0, 0);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = BackColor };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            var lblItems = MakeLabel("修改项（锁定 = 持续写入）");
            gridMemItems = NewGrid();
            gridMemItems.Columns.Add("colAddr2", "地址");
            gridMemItems.Columns.Add("colType2", "类型");
            gridMemItems.Columns.Add("colVal2", "写入值");
            var lockCol = new DataGridViewCheckBoxColumn { HeaderText = "锁定", Name = "colLock", Width = 60, FillWeight = 15 };
            gridMemItems.Columns.Add(lockCol);
            gridMemItems.Columns[0].FillWeight = 50;
            gridMemItems.Columns[1].FillWeight = 18;
            gridMemItems.Columns[2].FillWeight = 30;
            var btnBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = BackColor };
            var btnAdd = MakeButton("↓ 加入修改项");
            var btnWrite = MakeButton("立即写入");
            var btnRefreshRes = MakeButton("刷新结果值");
            var btnDel = MakeButton("删除选中");
            var btnClear = MakeButton("清空");
            btnAdd.Click += (s, e) => AddSelectedToItems();
            btnWrite.Click += (s, e) => WriteItemsOnce();
            btnRefreshRes.Click += (s, e) => RefreshResultValues();
            btnDel.Click += (s, e) => DeleteSelectedItems();
            btnClear.Click += (s, e) => gridMemItems.Rows.Clear();
            btnBar.Controls.AddRange(new Control[] { btnAdd, btnWrite, btnRefreshRes, btnDel, btnClear });
            right.Controls.Add(lblItems, 0, 0);
            right.Controls.Add(gridMemItems, 0, 1);
            right.Controls.Add(btnBar, 0, 2);
            mid.Controls.Add(right, 1, 0);

            // 行6：日志
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
            root.Controls.Add(txtLog, 0, 5);
        }

        void RefreshProc()
        {
            cmbProc.Items.Clear();
            foreach (Process p in Process.GetProcessesByName("guigubahuang"))
            {
                cmbProc.Items.Add(p.Id + "  |  " + p.ProcessName + ".exe");
            }
            Log("刷新进程: 找到 " + cmbProc.Items.Count + " 个 guigubahuang 进程");
            if (cmbProc.Items.Count > 0) cmbProc.SelectedIndex = 0;
        }

        void AttachProc()
        {
            if (cmbProc.SelectedItem == null) { Log("未选择进程，先点【刷新】"); return; }
            string sel = Convert.ToString(cmbProc.SelectedItem);
            int pid = int.Parse(sel.Split('|')[0].Trim());
            DetachProc();
            IntPtr h = MemEditor.Open(pid);
            if (h == IntPtr.Zero)
            {
                Log("附加失败 pid=" + pid + "，请确认游戏正在运行");
                txtStatus.Text = "未附加";
                return;
            }
            memHandle = h;
            txtStatus.Text = "已附加 pid=" + pid;
            Log("已附加进程 pid=" + pid);
        }

        void DetachProc()
        {
            if (memHandle != IntPtr.Zero)
            {
                MemEditor.Close(memHandle);
                memHandle = IntPtr.Zero;
                txtStatus.Text = "未附加";
            }
        }

        void EnsureAttached()
        {
            if (memHandle == IntPtr.Zero) AttachProc();
            if (memHandle == IntPtr.Zero) throw new InvalidOperationException("未附加游戏进程");
        }

        void DoFirstScan()
        {
            try
            {
                EnsureAttached();
                string v = txtValue.Text.Trim();
                if (v.Length == 0) { Log("先填写当前数值"); return; }
                scanType = Convert.ToString(cmbType.SelectedItem);
                Log("=== 首次扫描 " + scanType + " = " + v + " ===");
                btnAttach.Enabled = false;
                var bw = new BackgroundWorker();
                bw.DoWork += (s, e) =>
                {
                    e.Result = MemEditor.FirstScan(memHandle, scanType, v, Log);
                };
                bw.RunWorkerCompleted += (s, e) =>
                {
                    btnAttach.Enabled = true;
                    scanAddresses = (List<IntPtr>)e.Result ?? new List<IntPtr>();
                    scanLast = new Dictionary<long, byte[]>();
                    int size = MemEditor.TypeSize(scanType);
                    byte[] bytes;
                    try { bytes = MemEditor.ToBytes(scanType, v); } catch { bytes = new byte[size]; }
                    foreach (IntPtr a in scanAddresses) scanLast[(long)a] = bytes;
                    FillResults(scanAddresses);
                    Log("首次扫描完成: " + scanAddresses.Count + " 个候选。去游戏里改变数值，再点筛选。");
                };
                bw.RunWorkerAsync();
            }
            catch (Exception ex) { Log("首次扫描失败: " + ex.Message); }
        }

        void DoFilter(string condition)
        {
            try
            {
                EnsureAttached();
                if (scanAddresses.Count == 0) { Log("没有候选地址，先做首次扫描"); return; }
                string v = condition == "exact" ? txtExact.Text.Trim() : "";
                Log("=== 筛选: " + condition + (condition == "exact" ? " " + v : "") + " ===");
                scanAddresses = MemEditor.FilterScan(memHandle, scanAddresses, scanLast, scanType, condition, v, Log);
                FillResults(scanAddresses);
                Log("筛选后剩余: " + scanAddresses.Count + " 个地址。剩 1~5 个即可加入修改项。");
            }
            catch (Exception ex) { Log("筛选失败: " + ex.Message); }
        }

        void FillResults(List<IntPtr> addresses)
        {
            gridResults.Rows.Clear();
            int n = 0;
            foreach (IntPtr a in addresses)
            {
                if (n++ >= 2000) break;
                string cur = memHandle != IntPtr.Zero ? MemEditor.ReadValue(memHandle, a, scanType) : "?";
                gridResults.Rows.Add("0x" + ((long)a).ToString("X"), cur);
            }
        }

        void RefreshResultValues()
        {
            if (memHandle == IntPtr.Zero) return;
            foreach (DataGridViewRow row in gridResults.Rows)
            {
                if (row.IsNewRow) continue;
                string hex = Convert.ToString(row.Cells[0].Value);
                long addr;
                if (long.TryParse(hex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out addr))
                {
                    row.Cells[1].Value = MemEditor.ReadValue(memHandle, (IntPtr)addr, scanType);
                }
            }
        }

        void AddSelectedToItems()
        {
            foreach (DataGridViewRow row in gridResults.SelectedRows)
            {
                if (row.IsNewRow) continue;
                string hex = Convert.ToString(row.Cells[0].Value);
                long addr;
                if (!long.TryParse(hex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out addr)) continue;
                string cur = Convert.ToString(row.Cells[1].Value);
                gridMemItems.Rows.Add("0x" + addr.ToString("X"), scanType, cur, true);
            }
            Log("已加入 " + gridResults.SelectedRows.Count + " 个修改项（锁定已勾选，会自动持续写入）");
        }

        void WriteItemsOnce()
        {
            if (memHandle == IntPtr.Zero) { Log("未附加进程"); return; }
            int ok = 0;
            foreach (DataGridViewRow row in gridMemItems.Rows)
            {
                if (row.IsNewRow) continue;
                string hex = Convert.ToString(row.Cells[0].Value);
                string type = Convert.ToString(row.Cells[1].Value);
                string val = Convert.ToString(row.Cells[2].Value);
                long addr;
                if (!long.TryParse(hex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out addr)) continue;
                if (MemEditor.WriteValue(memHandle, (IntPtr)addr, type, val)) ok++;
                else Log("写入失败 " + hex);
            }
            Log("已写入 " + ok + " 项。");
            RefreshResultValues();
        }

        void LockTick()
        {
            if (memHandle == IntPtr.Zero) return;
            foreach (DataGridViewRow row in gridMemItems.Rows)
            {
                if (row.IsNewRow) continue;
                bool locked = row.Cells[3].Value != null && Convert.ToBoolean(row.Cells[3].Value);
                if (!locked) continue;
                string hex = Convert.ToString(row.Cells[0].Value);
                string type = Convert.ToString(row.Cells[1].Value);
                string val = Convert.ToString(row.Cells[2].Value);
                long addr;
                if (!long.TryParse(hex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out addr)) continue;
                MemEditor.WriteValue(memHandle, (IntPtr)addr, type, val);
            }
        }

        void DeleteSelectedItems()
        {
            foreach (DataGridViewRow row in gridMemItems.SelectedRows)
            {
                if (!row.IsNewRow) gridMemItems.Rows.Remove(row);
            }
        }

        // ================= 通用 =================
        Label MakeLabel(string s)
        {
            return new Label { Text = s, AutoSize = true, ForeColor = ForeColor, BackColor = BackColor, Padding = new Padding(0, 6, 0, 0) };
        }

        Button MakeButton(string s)
        {
            return new Button { Text = s, AutoSize = true, BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        }

        DataGridView NewGrid()
        {
            var g = new DataGridView
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
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(50, 50, 58);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            g.DefaultCellStyle.BackColor = Color.FromArgb(30, 30, 34);
            g.DefaultCellStyle.ForeColor = Color.White;
            return g;
        }

        void Log(string s)
        {
            if (txtLog == null || txtLog.IsDisposed) return;
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnregisterHotKey(Handle, HOTKEY_ID);
            DetachProc();
            base.OnFormClosing(e);
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
