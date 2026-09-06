# ModTheSpire 2

一个面向《杀戮尖塔 2》的启动前 Mod 管理器，参考了 [ModTheSpire](https://github.com/kiooeht/ModTheSpire) 的选择器体验：启动游戏前列出已安装模组，支持勾选启用状态和拖拽调整加载顺序，并额外提供 StS2 原生 `DLL + JSON + PCK` Bootstrap 包。

## 设计说明

《杀戮尖塔 1》的 ModTheSpire 是 Java/JAR 注入器，会建立自己的 class loader、补丁后的游戏 JAR 和 Mod 初始化流程。杀戮尖塔 2 使用 Godot + C#，原生 Mod 包是 manifest + DLL/PCK，因此不能直接复用 Java 注入链。

本项目采用“外部启动器 + 原生 Bootstrap Mod”方案。原因是 DLL 只有在游戏进程开始加载 Mod 后才会执行，不能在游戏进程启动前创建 StS1 那种独立选择窗口；启动前窗口必须由外部启动器提供。

外部启动器负责：

1. 扫描游戏目录下的 `mods/<ModName>/*.json` manifest。
2. 读取当前账号的 `settings.save`。
3. 在 `mod_settings.mod_list` 中写入模组顺序、`is_enabled` 和 `source`。
4. 先原子保存设置并生成 `settings.save.bak`，同时在同一账号目录写入 `modtheSpire2.launch.json`，再启动 `SlayTheSpire2.exe`。

原生 Bootstrap Mod 负责：

1. 通过 `ModuleInitializer` 尽早读取 `modtheSpire2.launch.json`。
2. 在 `settings.save` 可访问时再次校准 `mod_settings.mod_list` 的顺序、启用状态和来源。
3. 只修改 `mod_settings`，并在发生写入时保留 `settings.save.bootstrap.bak`。

启动器直接写入游戏下次启动使用的配置，因此不需要进入游戏后再改一次并重启。Bootstrap 是兜底校准层；DLL/PCK 的实际加载仍由杀戮尖塔 2 自己完成。

## 构建

要求 .NET 9 SDK。Bootstrap DLL 目标为 `net8.0`，以匹配 StS2 的 C# Mod 运行时；PCK 打包器按 Godot 4 PCK v4 格式生成，不要求开发机安装 Godot。

开发构建：

```powershell
dotnet restore .\Sts2ModManager.sln
dotnet build .\Sts2ModManager.sln
dotnet run --project .\tests\Sts2ModManager.Core.Tests\Sts2ModManager.Core.Tests.csproj
```

发布普通框架依赖版本：

```powershell
.\build.ps1
```

发布包含 .NET 运行时的单文件版本：

```powershell
.\build.ps1 -SelfContained
```

输出默认在 `dist\`。

构建成功后：

```text
dist\ModTheSpire2\ModTheSpire2.dll
dist\ModTheSpire2\ModTheSpire2.json
dist\ModTheSpire2\ModTheSpire2.pck
```

## 使用

1. 将整个 `dist\ModTheSpire2` 文件夹复制到游戏的 `mods\` 目录，使其结构为 `mods\ModTheSpire2\ModTheSpire2.*`。
2. 将 `dist\Sts2ModManager.exe` 放在游戏目录附近，或直接运行它。
3. 如果自动检测不到路径，在底部选择包含 `SlayTheSpire2.exe` 的游戏目录。
4. 选择当前 Steam 账号的 `settings.save`。通常在：
   `%APPDATA%\SlayTheSpire2\steam\<SteamId>\settings.save`
5. 左侧勾选/取消勾选 Mod；拖拽行即可调整顺序，也可以使用上移、下移、置顶、置底。
6. 点击“保存并启动游戏”。

如果游戏还没有生成 `settings.save`，先运行一次游戏并退出，再回到启动器选择该文件。启动器不会在无法确认账号时自动创建或覆盖未知存档路径。

## 支持的 manifest

每个 Mod 目录根部应有 `mod_manifest.json` 或与目录同名的 `.json` 文件，例如：

```text
Slay the Spire 2/
└── mods/
    └── MyMod/
        ├── MyMod.json
        ├── MyMod.dll
        └── MyMod.pck
```

至少需要 `id`；`name`、`author`、`version`、`description`、`dependencies` 会显示或读取。扫描到坏 JSON 或缺少 `id` 的文件时，启动器会报告问题并跳过该条目。

## 注意事项

- 这是 StS2 原生 Mod loader 的配置前端，不加载 StS1 的 Java Mod/JAR。
- 多人游戏需要所有玩家的启用列表、Mod 版本和顺序一致，否则可能出现版本或模型哈希不同步。
- 每次覆盖已有 `settings.save` 前会留下同目录的 `.bak` 备份。
- 游戏更新可能改变存档 JSON 结构；启动器只修改 `mod_settings`，并尽量保留其他未知字段。

## 参考

- [kiooeht/ModTheSpire](https://github.com/kiooeht/ModTheSpire)
- [STS2-LoadOrderManager](https://github.com/Jianbao233/STS2-LoadOrderManager)：验证 StS2 `settings.save` 中 `mod_settings.mod_list` 的字段和下次启动生效行为。
