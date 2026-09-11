# STS2-ModManager

面向《杀戮尖塔 2》的启动前 Mod 管理器，参考了 [ModTheSpire](https://github.com/kiooeht/ModTheSpire) 的选择器体验。

[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3799576017)

本项目使用 StS2 原生 Mod 包格式：`DLL + JSON + PCK`。启动游戏时，Bootstrap DLL 会在原生 Mod 加载循环继续之前创建启动选择器，显示已安装的 Mod，并支持：

- 勾选启用或禁用 Mod
- 使用“上移/下移”调整加载顺序
- 查看选中 Mod 的名称、作者、版本、描述和来源
- 右侧完整文字详情自动换行，支持滚轮浏览与选中复制
- 鼠标停在左侧模组列表或右侧详情上，即可用滚轮上下浏览
- 保存到 StS2 的 `settings.save`
- 按选择后的顺序重新加载本次启动的 Mod

不需要额外的 launcher.exe；游戏直接从 Steam 启动即可。

## 构建

要求 .NET 9 SDK。Bootstrap DLL 目标为 `net8.0`，以匹配 StS2 的 C# Mod 运行时；PCK 打包器生成 StS2 兼容的 Godot PCK v3 文件。

运行测试并生成发布包：

```powershell
.\build.ps1 -Configuration Release -OutputDirectory dist
```

成功后，发布包位于：

```text
dist\ModTheSpire2\ModTheSpire2.dll
dist\ModTheSpire2\ModTheSpire2.json
dist\ModTheSpire2\ModTheSpire2.pck
```

上传 Steam Workshop 时，只需要上传上述三个文件，不要上传 `dist` 根目录中的其他构建产物。

## 安装和使用

将 `dist\ModTheSpire2` 文件夹中的三个文件放到游戏的 Mod 目录，例如：

```text
Slay the Spire 2/
└── mods/
    └── ModTheSpire2/
        ├── ModTheSpire2.dll
        ├── ModTheSpire2.json
        └── ModTheSpire2.pck
```

然后从 Steam 启动游戏。游戏加载 Mod 前会显示管理窗口。点击“应用并继续”后，配置会保存并继续加载游戏。

- “应用并继续”：保存当前配置并进入游戏。
- “保存并退出”：保存当前配置并结束本次游戏进程，不进入游戏。
- 右上角 ×（或 Alt+F4）：退出游戏，不保存本次窗口中的改动。

如果配置保存失败，会显示错误并重新打开选择器，保留刚才的选择供重试。

右侧详情使用完整面板显示，不再请求或显示创意工坊图片。长描述可通过鼠标滚轮或滚动条查看；切换模组后从描述顶部开始显示。

首次安装时，必须将 `ModTheSpire2` 放在游戏原生 Mod 列表第一位。只有 Bootstrap 先执行，才能在同一次启动中拦截后续 Mod 的加载；应用一次配置后，管理器会把自己保存到第一位。已有手动安装版的用户，请勿同时启用创意工坊版，避免重复加载同一个模组。

每次启动都会优先保留上一次保存的已启用 Mod 顺序；未启用 Mod 会放在后面，并按名称、ID、来源进行稳定排序。因此游戏原生加载器改变 `Mods` 集合顺序时，不会覆盖用户已经设置好的启用顺序。

## 窗口回归测试

在 Windows 上可运行原生窗口测试（短暂显示包含测试模组的窗口，不启动游戏）：

```powershell
dotnet run --project .\tests\Sts2ModManager.Core.Tests --configuration Release -- --native-ui
```

测试覆盖长文本完整性、左右栏滚轮方向和隔离、切换模组后回到顶部，以及继续、保存退出、关闭窗口三种操作。另有独立子进程测试验证退出前保存已经完成，退出后不再执行加载流程。

## 日志

运行诊断日志位于：

```text
%APPDATA%\SlayTheSpire2\logs\modtheSpire2.log
```

游戏原生日志位于同一个 `logs` 目录。

## 设计说明

《杀戮尖塔 1》的 ModTheSpire 是 Java/JAR 注入器，而杀戮尖塔 2 使用 Godot + C#，原生 Mod 包是 JSON、DLL 和 PCK。因此本项目没有复用 StS1 的 Java 注入链，而是使用一个原生 StS2 Bootstrap Mod：

1. 通过 `ModuleInitializer` 尽早执行。
2. 通过反射读取 StS2 的 Mod 列表和当前设置。
3. 使用 Win32 原生窗口显示启动选择器，避免额外的 UI 运行时依赖。
4. 保存启用状态和顺序。
5. 使用 Harmony 拦截 `ModManager.TryLoadMod`，按选择后的顺序重放 Mod 加载。

## 参考

- [kiooeht/ModTheSpire](https://github.com/kiooeht/ModTheSpire)
- [STS2-LoadOrderManager](https://github.com/Jianbao233/STS2-LoadOrderManager)
