# 鬼谷八荒内置修改器 (GhbgMod)

游戏内嵌插件式修改器：放入游戏 Mods 文件夹，随游戏启动加载、随游戏退出关闭。进入游戏世界后按 **F4** 打开/关闭悬浮窗口，所有修改**实时生效**，无需重启、无需导入存档。

**仅支持单机模式**。当前适配：**鬼谷八荒 v1.2.106（PC / Steam 版，MelonLoader 0.5.4）**。

## 界面（浅色大字体）

四个标签页：

1. **① 属性修改**：等级、经验、年龄、寿命、魅力、声望；生命/灵力/念力/攻击/防御/脚力/气运/暴击/护体/精力。填新值点「写入」即时生效，另有一键满状态。
2. **② 道具/资源**：按道具 ID 添加（灵石=10001、宗门贡献=10002 等），背包列表直接改数量/删除，实时生效。
3. **③ 任务总览**：全部 507 个任务按类别分组（主线/城镇悬赏/奇遇/DLC），自动检测当前存档状态并着色——**灰=未接取、蓝=进行中、绿=已完成、橙=可领奖**。
4. **④ 使用说明**：存档位置、快捷键说明。

## 安装

1. 确认游戏目录已有 MelonLoader（游戏根目录存在 `MelonLoader\` 和 `version.dll`）。
2. 把 `GhbgMod.dll` 和 `task_ids.txt` 两个文件复制到：
   ```
   E:\Steam\steamapps\common\鬼谷八荒\Mods\
   ```
3. 启动游戏 → 进入存档 → 按 **F4** 呼出修改器。关闭窗口再按一次 F4；退出游戏插件随之关闭。

## 构建

需要 .NET 8 SDK：

```
cd plugin
dotnet build -c Release
```

输出：`plugin\bin\Release\net48\GhbgMod.dll`（需与 `task_ids.txt` 一起部署）。

## 数据结构说明（社区整理）

- 玩家数据：`g.game.data.world.playerUnitID` → `DataUnit.Unit.GetUnit()`，属性在 `UnitInfoData.propertyData`。
- 任务状态：`UnitInfoData.allTask`（`DataUnit/TaskData`），`state` 枚举 `Unfinished=0 / Achieve=1 / GetReward=2`。
- 道具：`UnitInfoData.propData`（`UnitDataProps`），`PropsData.propsID / propsCount`。
- 任务名/道具名：`ConfLocalText.GetText("taskName_" + id)` / `GetText("itemName_" + id)`。

## 免责声明

本项目仅供个人学习与技术研究使用，仅针对单机模式下的本地游戏数据。不包含任何游戏内容，对游戏内容不主张任何权利；如游戏权利方提出要求，将立即移除相关内容。

## License

MIT
