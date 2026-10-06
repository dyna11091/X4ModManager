# X4 mod管理器

一款面向 Windows 的轻量级 X4: Foundations 本地 MOD 管理器。

## 当前功能

- 自动定位 Steam/GOG 版 X4，并读取 `X4.exe` 的游戏版本。
- 扫描游戏 `extensions`、玩家 `extensions` 和 Steam Workshop `392160` 目录。
- 读取每个扩展的 `content.xml`：名称、ID、作者、版本和简介。
- 优先显示 `language="86"` 的简体中文名称与简介。
- 区分官方 DLC、Steam Workshop 与本地/Nexus MOD；`ws_数字` ID 始终按创意工坊识别。
- 兼容部分旧 MOD 使用的 XML 1.1 声明，不再因版本声明中断扫描。
- 从玩家配置 `content.xml` 读取并批量修改普通 MOD 与官方 DLC 的启用状态。
- 支持本地搜索和来源筛选；拖动列表项可保存自定义显示顺序。
- 可创建功能分类文件夹，并把一个或多个 MOD 拖入分类；分类自身支持拖动排序和右键删除。
- 分类可展开/收起；将 MOD 拖到分类内任意 MOD 行也会完成归类，空的“未分类”不会显示。
- 支持 Ctrl/Shift 多选及拖动边缘自动滚动；分类只保存在管理器中，不移动实际 MOD 文件。
- 可为 MOD 设置仅在管理器中显示的别名，不修改原始 MOD 文件和扩展 ID。
- 可从管理器直接启动 X4。
- 支持白天/夜间模式；主题、别名、分类和排序均保存在本机，每次启动默认显示全部来源。
- 写入前自动备份，并采用临时文件替换和写后校验。
- 打开 Workshop/Nexus 来源页；无法识别来源时可直接打开 MOD 文件夹。
- 把简介一键送到浏览器翻译为简体中文，不需要 API 密钥。

本项目不提供 MOD 在线搜索，也不删除、覆盖或自动更新 MOD 文件。

拖动排序只影响管理器中的显示顺序；X4 的扩展依赖与实际加载规则仍由游戏处理。

## 运行

需要 Windows 10/11 和 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。

普通用户可直接从 [GitHub Releases](https://github.com/dyna11091/X4ModManager/releases/latest) 下载发布包。

```powershell
dotnet run --project .\src\X4ModManager\X4ModManager.csproj
```

## 构建与测试

```powershell
dotnet build .\X4ModManager.sln -c Release
dotnet run --project .\tests\X4ModManager.Core.Tests\X4ModManager.Core.Tests.csproj -c Release
```

后续版本规划见 [docs/PLAN.md](docs/PLAN.md)。

