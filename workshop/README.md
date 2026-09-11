# 创意工坊发布工作区

作品：[STS2 Mod Manager | 启动前模组管理](https://steamcommunity.com/sharedfiles/filedetails/?id=3799576017)，ID `3799576017`。

`workshop.json` 是公开页面信息；`image.png` 是 AI 生成并缩小到上传限制内的展示图；`content/` 只存放本次构建的 DLL、JSON、PCK，不提交二进制到 Git。

构建后将 `dist/ModTheSpire2/` 的三个文件复制到 `content/`，使用用户提供的上传工具：

```powershell
& 'E:\GAME\Game mod\杀戮尖塔2\ModUploader-win-x64\ModUploader.exe' upload --workspace 'E:\GAME\Game mod\杀戮尖塔2\ModTheSpire\workshop'
```

需先运行并登录拥有该作品的 Steam 账号。第一次成功创建后，保留上传器生成的 `mod_id.txt`，以后使用同一工作区更新原作品。不要删除 ID 后重复创建。

作品展示图使用内置 image_gen 工具生成，提示词见 `image-prompt.md`。展示图不是实际界面截图。
