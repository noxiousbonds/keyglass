# 把 KeyGlass 发布到 GitHub（零基础版）

> 目标：把这个项目变成一个别人能看懂、能下载、能自己编译的 GitHub 仓库。
> 你不需要懂命令行：**第 3 节用 GitHub Desktop 点点鼠标就能完成 90% 的工作**。

---

## 1. 上传前你要准备的东西

| 项 | 说明 | 本项目取值 |
| --- | --- | --- |
| GitHub 账号 | 没有就先注册，建议开启两步验证 | — |
| 仓库名 | 全小写、用连字符 | `keyglass` |
| 一句话简介（About） | 显示在仓库右上角 | 见第 5 节 |
| 主题标签（Topics） | 让人能搜到你 | 见第 5 节 |
| 许可证 | 公开仓库必须有，否则别人不敢用 | MIT（已放 `LICENSE`） |
| 截图 | 放在 README 里，是最有说服力的部分 | `docs/img/*.png`（已准备） |
| 发布物（exe） | **不要放进仓库**，放到 Release 里 | 见第 6 节 |

## 2. 仓库里应该有什么、不该有什么

**要提交**（本项目已按此结构整理好）：

```
keyglass/
├─ README.md              ← 门面：这是什么、怎么用、截图、怎么编译
├─ LICENSE                ← 许可证
├─ CHANGELOG.md           ← 版本变更记录
├─ .gitignore             ← 告诉 Git 哪些不提交（已配好）
├─ .gitattributes         ← 统一换行，避免“整文件重写”的假 diff
├─ docs/
│  ├─ 交付文档.md          ← 设计取舍、架构、验收约定（本项目的技术底稿）
│  ├─ 验收清单.md          ← 逐条自检 + 实测数据
│  ├─ 发布指南.md          ← 本文
│  └─ img/                ← README 用到的截图
├─ src/KeyGlass/          ← 源码（*.cs / *.xaml / *.csproj / app.ico）
└─ tools/                 ← 构建、安装、冒烟测试、性能实测脚本
```

**不要提交**（`.gitignore` 已经拦掉）：

| 目录 | 原因 |
| --- | --- |
| `bin/` `obj/` | 编译中间产物 |
| `dist/` `dist-single/` | 编译出来的程序（140MB，GitHub 单文件上限 100MB，会被拒） |
| `artifacts/` | 自检生成的截图和 JSON |
| `.dotnet/` `.nuget/` `.tmp/` | 本地工具链和缓存 |
| `logs/` | 运行日志（可能含你的窗口标题等隐私） |

> 判断标准：**能在别人机器上从源码重新生成的东西，都不提交**。想给别人直接用，就把 exe 传到 Release。

## 3. 上传：GitHub Desktop（推荐，图形界面）

1. 打开 <https://desktop.github.com> 下载安装 **GitHub Desktop**。
2. 启动后点 **Sign in to GitHub.com**，浏览器里授权登录。
3. 菜单 **File → Add local repository…**，选择本目录 `D:\ai\keyglass`（已经初始化成 Git 仓库）。
4. 左下角 **Summary** 填 `Initial commit: KeyGlass v1.0.0`，点 **Commit to main**。
5. 顶部点 **Publish repository**：
   - Name: `keyglass`
   - Description: 复制第 5 节的中文描述
   - **取消勾选** `Keep this code private`（想先私有也可以，之后再公开）
6. 点 **Publish repository**，等进度条跑完即可。

## 4. 上传：命令行（可选，仓库已初始化）

```powershell
# 本机已装好便携 Git；如果你想用自己的 Git，把路径换掉即可
$git = "D:\ai\.tools\git\cmd\git.exe"
cd D:\ai\keyglass
& $git status                 # 确认只有该提交的文件
& $git log --oneline          # 确认已有 Initial commit
```

推送前需要先在 GitHub 网页上 **新建空仓库**（不要勾选 README/LICENSE/gitignore），然后：

```powershell
& $git remote add origin https://github.com/<你的用户名>/keyglass.git
& $git push -u origin main
```

第一次推送会要求登录：推荐用 **Personal Access Token**（见下），不要用账号密码（GitHub 已不支持）。

### 创建 Token（只在需要命令行推送时）

1. GitHub → 右上头像 → **Settings** → **Developer settings** → **Personal access tokens** → **Fine-grained tokens** → **Generate new token**
2. Repository access 选 **Only select repositories** → 勾选 `keyglass`
3. Permissions → Repository permissions → **Contents: Read and write**
4. 生成后复制（只显示一次），推送时用户名填 GitHub 用户名，密码填这个 token
5. **用完可立即删除该 token**（Settings 里同一页面）

## 5. 上传后立刻做的 4 件事

1. 仓库首页右侧 **About** → 齿轮 → 填简介 + 网站（可留空）+ Topics：
   ```
   windows  wpf  dotnet  overlay  keyboard  mouse  streaming  bilibili  rawinput  input-visualizer
   ```
   **推荐简介（中文）**：
   > Windows 11 键盘鼠标输入可视化叠加层：为 B 站直播姬设计，绿色/透明双模式采集，支持「自己屏幕看不到、直播姬照采」；零第三方依赖，自包含单文件。
   **English**：
   > Real-time keyboard & mouse input visualizer for Windows 11, built for Bilibili Live Studio. Chroma-key or transparent capture, hide-from-own-screen mode, zero third-party dependencies.
2. **Settings → General → Features**：打开 Issues；不想收 PR 可以先关掉。
3. 确认 README 顶部的截图能正常显示（点仓库首页看）。
4. **Releases → Draft a new release**：Tag 填 `v1.0.0`，标题 `KeyGlass v1.0.0`，把 `dist\KeyGlass\KeyGlass.exe` 或打包好的 zip **拖进附件区**（单文件 <2GB，140MB 没问题），正文直接复制 `CHANGELOG.md` 的 v1.0.0 段。

> 为什么不把 exe 提交进仓库：GitHub 对单文件 >100MB 会直接拒绝推送；Release 附件才是分发可执行文件的正规位置。

## 6. 以后怎么更新

每次改完代码：

1. GitHub Desktop 里会列出改动 → 写 Summary（例如 `fix: 修复隐藏模式下任务栏遮挡失效`）→ **Commit to main** → **Push origin**
2. 发新版本时：改 `CHANGELOG.md` → 提交 → 网页 **Releases → Draft a new release**，Tag 填 `v1.0.1`，附件换成新的 exe。

提交信息建议用前缀，读起来清楚：

| 前缀 | 用途 |
| --- | --- |
| `feat:` | 新功能 |
| `fix:` | 修 bug |
| `docs:` | 只改文档 |
| `perf:` | 性能优化 |
| `chore:` | 杂项（依赖、脚本） |

## 7. 新手最容易踩的 6 个坑

| 现象 | 原因 | 怎么解决 |
| --- | --- | --- |
| 推送报 `File exceeds 100 MB` | 把 `dist/` 提交了 | `git rm -r --cached dist`，确认 `.gitignore` 已包含 `dist/`，重新提交 |
| 满屏红字 `LF will be replaced by CRLF` | 换行符不统一 | 已用 `.gitattributes` 统一，忽略该提示 |
| 中文文件名显示乱码 | Git 默认转义非 ASCII | `git config --global core.quotepath false` |
| 认证失败 `Authentication failed` | 用了账号密码 | 改用 Personal Access Token（第 4 节） |
| 提交里混进 `logs/` 或 `artifacts/` | `.gitignore` 不生效（之前已 add 过） | `git rm -r --cached <目录>` 后再提交 |
| 双击 exe 被杀软报毒 | 未签名的键盘监听类程序 | 在 README「已知限制」里说明；建议用户从 Release 下载并核对 SHA256 |

## 8. 这个项目专属的 3 个注意点

1. **不要在 README 里写死本机路径**（已经泛化成 `dist\...` 相对路径）。
2. **不要提交 `%APPDATA%\KeyGlass\settings.json`**：里面是个人配置，且不在仓库目录内，注意别手动复制进来。
3. **截图别放运行日志**：`logs\diag.log` 会记录前台窗口标题，可能带隐私；只放 `docs\img` 里的产品截图。

## 9. 一分钟自检清单

```powershell
cd D:\ai\keyglass
$git = "D:\ai\.tools\git\cmd\git.exe"
& $git status --short          # 应该为空
& $git ls-files | Measure-Object -Line   # 约 50 个文件
& $git count-objects -vH       # size-pack 应在几 MB 以内
```

- [ ] 仓库里没有 `dist/` `bin/` `obj/` `logs/` `artifacts/`
- [ ] `README.md` 第一屏能看懂「这是什么 + 一张截图 + 怎么用」
- [ ] `LICENSE` 存在
- [ ] About 简介 / Topics 已填
- [ ] Release 里有 v1.0.0 的 exe 附件
- [ ] 仓库公开后自己用无痕窗口打开一次，确认截图和链接都能点开
