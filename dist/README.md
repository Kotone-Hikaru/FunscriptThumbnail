# Funscript 缩略图插件（eroscripts 风格）

在 Windows 资源管理器里给 `.funscript` 文件显示 eroscripts 风格的热力图缩略图。
显示位置：**详细信息窗格顶部的图标处**、大图标 / 超大图标视图、文件提示。

---

## 一、拷到另一台电脑安装（最常用）

1. 把**整个文件夹**（不是单个 install.cmd）拷到那台电脑上，例如 U 盘、共享目录或解压 zip。
   放到哪里都无所谓，装完就可以删掉。
2. **双击 `install.cmd`** —— 它会自己向 Windows 请求管理员权限（UAC 弹一下，点"是"）。
   （右键"以管理员身份运行"当然也可以。）
3. 完成。打开一个含 `.funscript` 的文件夹就能看到缩略图。

> 只需管理员权限这一步。安装内容：
> - 把 `FunscriptThumbnail.dll` 复制到 `C:\ProgramData\FunscriptThumbnail\bin-<时间戳>\`
> - 在 `HKLM\SOFTWARE\Classes` 下注册缩略图处理程序（对本机所有用户生效）
> - 在「设置 → 应用 → 安装的应用」里登记一个卸载项（带图标），所以**以后卸载不用再找 cmd**
>
> 因为装在 `C:\ProgramData`，**装一次，这台电脑上所有账户都能用**，
> 也不受"用哪个账号执行安装"影响。

---

## 二、环境要求

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 10（1903 及以上）/ Windows 11，64 位 |
| 运行时 | **.NET Framework 4.8** —— Win10 1903+ 与 Win11 自带，无需额外安装 |
| 权限 | 安装时需要管理员；日常使用不需要 |

`install.cmd` 会先检查 .NET 版本，缺了会明确提示并给出下载地址，
不会出现"点了没反应"的情况。其他电脑上如果提示缺运行时：
<https://dotnet.microsoft.com/download/dotnet-framework/net48>

---

## 三、装完看不到缩略图？

**这几乎总是缩略图缓存的问题，不是插件没装好。**

资源管理器按**文件路径**缓存缩略图。插件装好之前它已经看过的文件，
会一直保留当初那份"没有缩略图"的结果。两种解决办法：

- **最快**：把 `.funscript` 复制一份改成新文件名 —— 新路径没有缓存条目，立刻出图。
- **彻底**：双击 `clear-cache.cmd`（会自己请求管理员权限）。它会重启资源管理器并清掉
  `thumbcache_*.db` / `iconcache_*.db`（只删这两个缓存，**不动任何文档**，Windows 会自动重建）。

另外确认一下：`C:\ProgramData\FunscriptThumbnail` 里应该有 `bin-` 开头的文件夹（含 DLL）、
一个 `FunscriptThumbTool.exe`（卸载按钮用的副本）和 `funscript.ico`。
用 `FunscriptThumbTool.exe status` 可以一次性看清这些。

---

## 四、卸载

**推荐：设置 → 应用 → 安装的应用（Win10 叫"应用和功能"）**，找到
「Funscript 缩略图 (Windows 资源管理器)」，点「卸载」。这是标准的卸载项，
带图标、版本和发布者，按钮会自动弹 UAC —— 不需要去找任何脚本。

也可以用本文件夹里的 `uninstall.cmd`（双击即可，会自己请求管理员权限）。

两种方式做的是同一件事：删掉注册项、卸载项、`C:\ProgramData\FunscriptThumbnail`
里的 DLL 和副本。**都不会碰你的 `.funscript` 文件**。
已缓存的缩略图会留到缓存被清掉为止（可跑 `clear-cache.cmd`）。

> 装完之后本文件夹就可以删了 —— 卸载需要的程序副本已经放在
> `C:\ProgramData\FunscriptThumbnail\` 里，卸载按钮指向的是那一份。

---

## 五、命令行工具（可选，排查用）

```
FunscriptThumbTool.exe status                             查看注册状态 / DLL 路径 / 驻留方式 / 卸载项
FunscriptThumbTool.exe install [--in-process] [--quiet]   安装（需管理员）
FunscriptThumbTool.exe uninstall [--quiet]                卸载（需管理员）
FunscriptThumbTool.exe render <in.funscript> <out.png> [最大边长=1380] [--dark|--light|--bg=RRGGBB]
                                                          用与资源管理器完全相同的代码渲染成 PNG
FunscriptThumbTool.exe shelltest <file> <out.png> [尺寸]   走系统外壳取缩略图（验证用）
```

`status` 会检查这些常见故障：注册项是否齐全、CodeBase 指向的 DLL 是否真的存在、
是否有 HKCU 影子项遮蔽了机器级注册、卸载项指向的程序是否还在。

---

## 六、关于显示效果

缩略图按 eroscripts 原版布局生成：宽度固定、高度随轴数增加、**保持原始比例**。
Windows 把缩略图放进方框时是**等比缩放、不裁切**，所以：

- 多轴脚本（如 6 轴的 L0–R2）接近方形比例，在方框里填得比较满；
- 单轴脚本本身是 690:52 的扁条，在方框里就是一条细长条。

这是原版比例的必然结果，不是 bug。

**底色**：采用 eroscripts.com 深色模式的页面底色 `#1a1a1a`。这不是随手挑的颜色 ——
funlib 的 SVG 本身是透明的，靠页面底色透出来，所以：

| 区域 | 计算结果 | 与 ES 站点实测 |
|---|---|---|
| 统计条（`#ccc` @ 30%） | `0.3×204 + 0.7×26 = 79` | 79,79,79 ✓ |
| 图形区（渐变 @ 20%，速度 0 处透明） | `#1a1a1a` | 26,26,26 ✓ |

如果哪天想要回白底，用 `render … --light` 就能看到对照效果。

---

## 七、文件说明

| 文件 | 用途 |
|---|---|
| `install.cmd` | 安装（双击即可，自动请求管理员权限） |
| `uninstall.cmd` | 卸载（同上；但通常用「设置 → 应用」就行） |
| `clear-cache.cmd` | 清缩略图缓存并重启资源管理器（同上） |
| `FunscriptThumbnail.dll` | 缩略图处理程序本体 |
| `FunscriptThumbTool.exe` | 安装 / 卸载 / 诊断 / 渲染工具 |
| `THIRD_PARTY_NOTICES.txt` | 第三方许可声明 |

---

## 八、许可与致谢

渲染算法（速度配色、背景渐变、统计数字）移植自
[Eroscripts/funlib](https://github.com/Eroscripts/funlib)（MIT）及其依赖
[colorizr](https://github.com/gilbarbara/colorizr)（MIT）。
完整许可文本见 `THIRD_PARTY_NOTICES.txt`。
