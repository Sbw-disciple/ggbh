# 鬼谷八荒存档修改器 (GhbgEditor)

一个可置顶的 Windows 悬浮窗存档修改器。读取鬼谷八荒（Tale of Immortal）的 PC 本地存档目录，把加密的 `.cache` 存档解成 JSON，按你填写的「查找 / 替换」规则批量修改，再加密写回。改完在游戏内【导入存档】即可生效。

**仅支持单机模式**。当前适配游戏版本：**v1.2.106（PC / Steam 版）**。

## 界面

- **悬浮窗置顶**：默认置顶，可取消勾选，右上角可关闭。
- **目录选择**：浏览选择存档目录，例如
  `C:\Users\<你>\AppData\LocalLow\guigugame\guigubahuang\Steam\CacheData\2_XXXXXX`
- **规则表**：三列 —— 查找内容（支持正则）/ 替换为 / 说明。可增删行。
- **常用快捷按钮**：灵石、宗门贡献、游戏难度、寿命等一键填充。
- **操作流程**：`① 读取存档`（生成 `*.cache.json`）→ 填规则 → `② 写入存档`（回写 `.cache`）。
- **备份存档**：一键把当前存档目录复制为 `*_backup_时间戳`。

## 修改示例

| 查找内容（正则） | 替换为 | 说明 |
|---|---|---|
| `\[1,10001,\d+\]` | `[1,10001,999999]` | 灵石改 999999 |
| `\[1,10002,\d+\]` | `[1,10002,999999]` | 宗门贡献改 999999 |
| `"gameLevel":\d+` | `"gameLevel":5` | 游戏难度改地狱 |
| `"y":\d+` | `"y":9999` | 玩家寿命（会命中所有 `y` 字段，慎用） |

## 存档格式说明（社区整理）

- 文件头：校验串每个字符 XOR `0x03`，共 30 字节。
- 文件体：逐字节滚动异或 + gzip 压缩。
- 文件名：`@` 替换为 `/` 后 Base64 解码，再用 DES 解密得到内部名。

## 存档文件对应内容

| 文件 | 内容 |
|---|---|
| `O33a@9fkh++YwffVNeV1RA==.cache` | DataUnit —— 人物数据（玩家/NPC 属性、背包、功法） |
| `eMZk7avfjaaSiFBwLwtZHg==.cache` | DataWorld —— 综合信息（难度、天气、玩家编号） |
| `1V5i793M2Ol7H@CBpcLu+A==.cache` | DataGrid —— 地图数据 |
| `soegnXJ65qU3ZS4O9xLDgg==.cache` | DataBuildSchool —— 宗门 |
| `soegnXJ65qUCT4iKXaSMyA==.cache` | DataBuildTown —— 城镇 |
| `W5hmZoonQsc=.cache` | DataMap —— 地图 |
| `CacheData` | 存档元信息（角色名、存档名） |

## 构建

无需安装 .NET SDK，直接用 Windows 自带的 .NET Framework 编译器：

```
build.bat
```

输出：`bin\GhbgEditor.exe`。双击运行。

## 免责声明

本项目仅供个人学习与技术研究使用，仅针对单机模式下的本地存档文件。修改前请自行备份存档。本项目不包含任何游戏内容，对游戏内容不主张任何权利；如游戏权利方提出要求，将立即移除相关内容。

## License

MIT
