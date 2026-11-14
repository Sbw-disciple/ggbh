using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnhollowerBaseLib;

[assembly: MelonInfo(typeof(GhbgMod.GhbgModMain), "GhbgMod", "1.0.0", "Sbw66")]
[assembly: MelonGame(null, null)]

namespace GhbgMod
{
    public static class S
    {
        public static Font GuiFont;

        public static bool Btn(string s, params GUILayoutOption[] o)
            => GUILayout.Button(s, new Il2CppReferenceArray<GUILayoutOption>(o));

        public static void Lbl(string s, params GUILayoutOption[] o)
            => GUILayout.Label(s, new Il2CppReferenceArray<GUILayoutOption>(o));

        public static string TF(string s, params GUILayoutOption[] o)
            => GUILayout.TextField(s, new Il2CppReferenceArray<GUILayoutOption>(o));

        public static void Space(float f) => GUILayout.Space(f);

        public static void BeginH(params GUILayoutOption[] o)
            => GUILayout.BeginHorizontal(new Il2CppReferenceArray<GUILayoutOption>(o));

        public static void EndH() => GUILayout.EndHorizontal();

        public static void Flex() => GUILayout.FlexibleSpace();

        public static Vector2 BeginScroll(Vector2 v, params GUILayoutOption[] o)
            => GUILayout.BeginScrollView(v, new Il2CppReferenceArray<GUILayoutOption>(o));

        public static void EndScroll() => GUILayout.EndScrollView();
    }

    public class GhbgModMain : MelonMod
    {
        bool showMain;
        Rect winMain = new Rect(50, 50, 1160, 700);
        int tab;

        string outString = "就绪";
        string propsID = "10001";
        string propsCount = "999999";
        string filterTask = "";
        string moneyFilter = "";

        List<string> allTaskIDs = new List<string>();
        List<string> playerTasks = new List<string>();
        Vector2 scrollTask, scrollProps, scrollMain;

        public override void OnApplicationStart()
        {
            MelonLogger.Msg("GhbgMod 已加载 — 按 F4 打开/关闭修改器");
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F4)) showMain = !showMain;
        }

        public override void OnGUI()
        {
            if (!showMain) return;

            if (S.GuiFont == null)
                S.GuiFont = Font.CreateDynamicFontFromOSFont(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" }, 24);

            GUI.skin.font = S.GuiFont;
            GUI.skin.label.fontSize = 24;
            GUI.skin.button.fontSize = 24;
            GUI.skin.textField.fontSize = 22;
            GUI.skin.toggle.fontSize = 20;
            GUI.skin.window.fontSize = 24;
            GUI.skin.button.fixedHeight = 42;
            GUI.skin.textField.fixedHeight = 40;
            GUI.skin.label.fixedHeight = 34;

            GUI.backgroundColor = new Color(0.96f, 0.97f, 0.99f, 0.99f);
            GUI.contentColor = new Color(0.13f, 0.15f, 0.2f, 1f);
            GUI.color = new Color(0.94f, 0.95f, 0.98f, 0.99f);

            GUILayout.BeginArea(winMain);
            GUI.Box(new Rect(0, 0, winMain.width, winMain.height), GUIContent.none);

            // 标题栏（可拖动）
            GUI.Label(new Rect(20, 8, 620, 36), "鬼谷八荒内置修改器 v1.0");
            GUI.backgroundColor = new Color(0.98f, 0.73f, 0.32f, 1f);
            if (GUI.Button(new Rect(winMain.width - 170, 6, 160, 40), "关闭 (F4)")) showMain = false;
            GUI.backgroundColor = new Color(0.96f, 0.97f, 0.99f, 0.99f);
            HandleDrag(new Rect(0, 0, winMain.width - 180, 46));

            DrawMain(0);
            GUILayout.EndArea();
        }

        bool dragging;
        Vector2 dragOff;
        void HandleDrag(Rect r)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                dragging = true;
                dragOff = e.mousePosition - new Vector2(winMain.x, winMain.y);
            }
            if (dragging)
            {
                if (e.type == EventType.MouseDrag)
                    winMain.position = e.mousePosition - dragOff;
                if (e.type == EventType.MouseUp)
                    dragging = false;
            }
        }

        void DrawMain(int id)
        {
            GUILayout.BeginArea(new Rect(10, 54, winMain.width - 20, winMain.height - 64));

            S.BeginH();
            string[] tabs = { "① 属性修改", "② 道具/资源", "③ 任务总览", "④ 使用说明" };
            for (int i = 0; i < tabs.Length; i++)
            {
                GUI.backgroundColor = tab == i ? new Color(0.98f, 0.73f, 0.32f, 1f) : new Color(0.82f, 0.86f, 0.93f, 1f);
                if (S.Btn(tabs[i], GUILayout.MinWidth(240)))
                {
                    tab = i;
                    if (tab == 2) RefreshTasks();
                }
            }
            GUI.backgroundColor = new Color(0.96f, 0.97f, 0.99f, 0.99f);
            S.EndH();

            S.Space(6);

            scrollMain = S.BeginScroll(scrollMain, GUILayout.Width(winMain.width - 24), GUILayout.Height(winMain.height - 104));

            if (tab == 0) DrawTabProps();
            else if (tab == 1) DrawTabItems();
            else if (tab == 2) DrawTabTasks();
            else DrawTabHelp();

            S.EndScroll();

            S.BeginH();
            S.Lbl(outString, GUILayout.Height(34));
            S.Flex();
            S.EndH();

            GUILayout.EndArea();
        }

        // ---------- 数据 ----------
        DataUnit.UnitInfoData GetPlayer()
        {
            try
            {
                if (g.game == null || g.game.data == null) return null;
                if (g.game.data.world == null) return null;
                string pid = g.game.data.world.playerUnitID;
                if (string.IsNullOrEmpty(pid)) return null;
                if (g.game.data.unit == null) return null;
                return g.game.data.unit.GetUnit(pid);
            }
            catch (Exception e)
            {
                outString = "获取玩家失败: " + e.Message;
                return null;
            }
        }

        DataUnit.PropertyData GetProperty(DataUnit.UnitInfoData u)
        {
            try { return u.propertyData; }
            catch (Exception e) { outString = "获取属性失败: " + e.Message; return null; }
        }

        // ---------- 属性 ----------
        void DrawTabProps()
        {
            var u = GetPlayer();
            if (u == null) { S.Lbl("尚未进入游戏世界（读取不到玩家数据）"); return; }
            var p = GetProperty(u);
            if (p == null) { S.Lbl("读取属性失败"); return; }

            S.Lbl("◆ 基础属性");
            StatRow("等级 gradeID", p.gradeID, v => p.gradeID = v);
            StatRow("经验 exp", p.exp, v => p.exp = v);
            StatRow("年龄 age", p.age, v => p.age = v);
            StatRow("寿命 life", p.life, v => p.life = v);
            StatRow("魅力 beauty", p.beauty, v => p.beauty = v);
            StatRow("声望 reputation", p.reputation, v => p.reputation = v);

            S.Space(8);
            S.Lbl("◆ 战斗属性");
            StatRow("生命 hp", p.hp, v => p.hp = v);
            StatRow("生命上限 hpMax", p.hpMax, v => p.hpMax = v);
            StatRow("灵力 mp", p.mp, v => p.mp = v);
            StatRow("灵力上限 mpMax", p.mpMax, v => p.mpMax = v);
            StatRow("念力 sp", p.sp, v => p.sp = v);
            StatRow("念力上限 spMax", p.spMax, v => p.spMax = v);
            StatRow("攻击 attack", p.attack, v => p.attack = v);
            StatRow("防御 defense", p.defense, v => p.defense = v);
            StatRow("脚力 footSpeed", p.footSpeed, v => p.footSpeed = v);
            StatRow("气运 luck", p.luck, v => p.luck = v);
            StatRow("暴击 crit", p.crit, v => p.crit = v);
            StatRow("护体 guard", p.guard, v => p.guard = v);
            StatRow("精力 energy", p.energy, v => p.energy = v);
            StatRow("精力上限 energyMax", p.energyMax, v => p.energyMax = v);

            S.Space(10);
            if (S.Btn("一键满状态（血/灵/念/精力拉满）", GUILayout.Height(48)))
            {
                p.hp = p.hpMax; p.mp = p.mpMax; p.sp = p.spMax; p.energy = p.energyMax;
                outString = "已回满状态";
            }
        }

        void StatRow(string label, int cur, Action<int> set)
        {
            S.BeginH();
            S.Lbl(label + " = " + cur, GUILayout.Width(380));
            string newVal = S.TF("", GUILayout.Width(180));
            if (S.Btn("写入", GUILayout.Width(110)))
            {
                int v;
                if (int.TryParse(newVal, out v)) { set(v); outString = label + " 已改为 " + v; }
                else outString = "必须是整数";
            }
            S.EndH();
        }

        // ---------- 道具 ----------
        void DrawTabItems()
        {
            S.Lbl("◆ 添加道具（输入道具ID和数量）");
            S.BeginH();
            S.Lbl("道具ID", GUILayout.Width(130));
            propsID = S.TF(propsID, GUILayout.Width(150));
            S.Lbl("数量", GUILayout.Width(100));
            propsCount = S.TF(propsCount, GUILayout.Width(150));
            S.EndH();

            S.BeginH();
            if (S.Btn("添加道具", GUILayout.Width(190))) AddProps(propsID, propsCount);
            if (S.Btn("灵石=10001", GUILayout.Width(210))) AddProps("10001", propsCount);
            if (S.Btn("宗门贡献=10002", GUILayout.Width(240))) AddProps("10002", propsCount);
            if (S.Btn("补元丹=1011011", GUILayout.Width(250))) AddProps("1011011", propsCount);
            S.EndH();

            S.Space(10);
            S.Lbl("◆ 背包（改数量即时生效，筛选留空显示全部）");
            S.BeginH();
            moneyFilter = S.TF(moneyFilter, GUILayout.Width(220));
            S.Lbl("（按ID/名称筛选）", GUILayout.Width(320));
            S.EndH();

            var u = GetPlayer();
            if (u == null) { S.Lbl("尚未进入游戏世界"); return; }
            var ud = u.propData;
            if (ud == null || ud.allProps == null) { S.Lbl("背包数据为空"); return; }

            scrollProps = S.BeginScroll(scrollProps, GUILayout.Height(340));
            int shown = 0;
            foreach (var pd in ud.allProps)
            {
                if (pd == null) continue;
                string name = GetPropName(pd.propsID);
                string line = "[" + pd.propsID + "] " + name + " x" + pd.propsCount;
                if (!string.IsNullOrEmpty(moneyFilter) && !line.Contains(moneyFilter)) continue;
                S.BeginH();
                S.Lbl(line, GUILayout.Width(540));
                string newCount = S.TF(pd.propsCount.ToString(), GUILayout.Width(130));
                if (S.Btn("改", GUILayout.Width(80)))
                {
                    int v;
                    if (int.TryParse(newCount, out v)) { pd.propsCount = v; outString = "已修改 " + name + " → " + v; }
                }
                if (S.Btn("删", GUILayout.Width(80)))
                {
                    ud.DelPropsItem(pd);
                    outString = "已删除 " + name;
                }
                S.EndH();
                if (++shown > 250) break;
            }
            S.EndScroll();
            S.Lbl("显示 " + shown + " 项");
        }

        string GetPropName(int id)
        {
            try
            {
                string n = ConfLocalText.GetText("itemName_" + id);
                return string.IsNullOrEmpty(n) ? "?" : n;
            }
            catch { return "?"; }
        }

        void AddProps(string idStr, string countStr)
        {
            int id, count;
            if (!int.TryParse(idStr, out id) || !int.TryParse(countStr, out count)) { outString = "ID/数量必须是数字"; return; }
            var u = GetPlayer();
            if (u == null) { outString = "尚未进入游戏世界"; return; }
            try
            {
                var np = DataProps.PropsData.NewProps(id, count);
                u.propData.AddProps(np);
                outString = "已添加道具 " + id + " x" + count;
            }
            catch (Exception e) { outString = "添加失败: " + e.Message; }
        }

        // ---------- 任务 ----------
        void RefreshTasks()
        {
            try
            {
                if (allTaskIDs.Count == 0)
                {
                    string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    string dir = Path.GetDirectoryName(dllPath);
                    string f = Path.Combine(dir, "task_ids.txt");
                    allTaskIDs = File.ReadAllLines(f).Where(l => l.Trim().Length >= 7 && l.Trim().All(char.IsDigit)).Select(l => l.Trim()).ToList();
                }
            }
            catch (Exception e) { outString = "读取 task_ids.txt 失败: " + e.Message; }

            playerTasks.Clear();
            var u = GetPlayer();
            if (u != null && u.allTask != null)
            {
                foreach (var t in u.allTask)
                {
                    if (t == null) continue;
                    playerTasks.Add(t.id + "|" + (int)t.state);
                }
            }
        }

        void DrawTabTasks()
        {
            S.Lbl("◆ 任务总览（共 " + allTaskIDs.Count + " 个任务，按类别分组）");
            S.BeginH();
            S.Lbl("筛选:", GUILayout.Width(120));
            filterTask = S.TF(filterTask, GUILayout.Width(240));
            if (S.Btn("刷新任务", GUILayout.Width(160))) RefreshTasks();
            S.EndH();

            int done = playerTasks.Count(t => { var p = t.Split('|'); return p.Length > 1 && (p[1] == "1" || p[1] == "2"); });
            int doing = playerTasks.Count(t => { var p = t.Split('|'); return p.Length > 1 && p[1] == "0"; });
            S.Lbl("当前存档：进行中 " + doing + " 个 | 已完成/可领奖 " + done + " 个 | 未接取 " + Math.Max(0, allTaskIDs.Count - doing - done) + " 个", GUILayout.Height(36));

            scrollTask = S.BeginScroll(scrollTask, GUILayout.Height(430));

            // 类别分组：前缀决定
            var groups = new List<Tuple<string, string, List<string>>>
            {
                Tuple.Create("11", "【主线剧情】", new List<string>()),
                Tuple.Create("21", "【城镇/悬赏任务】", new List<string>()),
                Tuple.Create("30", "【奇遇/特殊任务】", new List<string>()),
                Tuple.Create("80", "【DLC/扩展任务】", new List<string>()),
                Tuple.Create("",  "【其他任务】", new List<string>())
            };
            foreach (string id in allTaskIDs)
            {
                bool matched = false;
                foreach (var gp in groups)
                {
                    if (!string.IsNullOrEmpty(gp.Item1) && id.StartsWith(gp.Item1)) { gp.Item3.Add(id); matched = true; break; }
                }
                if (!matched) groups[4].Item3.Add(id);
            }

            int shown = 0;
            foreach (var gp in groups)
            {
                if (gp.Item3.Count == 0) continue;
                S.Lbl(gp.Item2 + "（" + gp.Item3.Count + "）");
                foreach (string id in gp.Item3)
                {
                    string name = GetTaskName(id);
                    if (!string.IsNullOrEmpty(filterTask) && !id.Contains(filterTask) && !name.Contains(filterTask)) continue;

                    string state; Color color;
                    var match = playerTasks.FirstOrDefault(t => t.StartsWith(id + "|"));
                    if (match == null) { state = "未接取"; color = new Color(0.55f, 0.55f, 0.6f, 1f); }
                    else
                    {
                        int st = int.Parse(match.Split('|')[1]);
                        if (st == 0) { state = "进行中"; color = new Color(0.15f, 0.45f, 0.85f, 1f); }
                        else if (st == 1) { state = "已完成"; color = new Color(0.12f, 0.6f, 0.25f, 1f); }
                        else { state = "可领奖"; color = new Color(0.9f, 0.6f, 0.1f, 1f); }
                    }

                    S.BeginH();
                    var oldC = GUI.contentColor;
                    GUI.contentColor = color;
                    S.Lbl("    " + id, GUILayout.Width(180));
                    S.Lbl(name, GUILayout.Width(580));
                    S.Lbl(state, GUILayout.Width(160));
                    GUI.contentColor = oldC;
                    S.EndH();
                    if (++shown > 600) { S.EndScroll(); return; }
                }
            }
            S.EndScroll();
            S.Lbl("显示 " + shown + " 个（灰=未接取 蓝=进行中 绿=已完成 橙=可领奖）");
        }

        string GetTaskName(string id)
        {
            try
            {
                string n = ConfLocalText.GetText("taskName_" + id);
                return string.IsNullOrEmpty(n) ? "?" : n;
            }
            catch { return "?"; }
        }

        // ---------- 帮助 ----------
        void DrawTabHelp()
        {
            S.Lbl("◆ 使用说明");
            S.Lbl("· F4 打开/关闭本窗口（游戏内直接按）");
            S.Lbl("· 属性页：填新值 → 写入，即时生效");
            S.Lbl("· 道具页：按 ID 添加；改数量/删除即时生效");
            S.Lbl("· 任务页：自动对比当前存档，做完/没做完一目了然");
            S.Space(12);
            S.Lbl("◆ 存档位置");
            S.Lbl(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Low", "guigugame", "guigubahuang", "Steam", "CacheData"));
            S.Space(12);
            S.Lbl("◆ 本插件");
            S.Lbl("文件：GhbgMod.dll + task_ids.txt → 放入游戏 Mods 文件夹");
        }
    }
}
