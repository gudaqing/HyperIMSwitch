# HyperIMSwitch 输入法切换调试记录

## 背景
- 项目目标：Win11 下通过全局快捷键切换输入法。
- 实际环境：已安装并测试的目标输入法为：
  - 英语（美式键盘）`[0409] KeyboardLayout-0409`
  - 日语（微软输入法）`[0411] 微软输入法`
  - 微信输入法 `"[0804] 微信输入法"`

## 已确认问题与修复过程

### 1. 热键监听问题（已排除）
- 现象：最初怀疑无日志=监听失效。
- 结论：监听线程与热键注册均正常（`RegisterHotKey ok=True`，`WM_HOTKEY received` 可见）。
- 修复点：
  - 修正窗口 `Suspend/Resume` 生命周期，避免热键长期挂起。
  - 启动流程中提前应用绑定，避免窗口初始化异常导致未注册。

### 2. API 兼容问题（核心）
- 现象：`ITfInputProcessorProfileMgr` 在目标环境 `E_NOINTERFACE (0x80004002)`。
- 处理：
  - 放弃该接口路径。
  - 输入法切换改为 `ITfInputProcessorProfiles.ActivateLanguageProfile`。
  - 键盘布局切换走 `WM_INPUTLANGCHANGEREQUEST` + `ActivateKeyboardLayout` 回退。

### 3. 0409 切回 IME 失败问题（已定位）
- 关键现象：从 `0409` 切到 `0411/0804` 时，首次 `ActivateLanguageProfile` 常返回 `0x80070057`。
- 重试链中验证到：`ChangeCurrentLanguage` 是决定性步骤。

## 自动诊断结果（托盘 `自动诊断 / Run Diagnostics`）

测试场景矩阵（8 组）：
- 变量：
  - `change` = `RetryChangeCurrentLanguage`
  - `setDefault` = `RetrySetDefaultProfile`
  - `foreground` = `RetryForegroundLangRequest`
- 固定：`RetryEnableProfile=false`

在 `sublime_text` 前台下结果：
- `change=true` 的 Scenario 1-4：全部 `PASS`
- `change=false` 的 Scenario 5-8：全部 `FAIL`

结论：
- `RetryChangeCurrentLanguage`：**必要**
- `RetrySetDefaultProfile`：非必要（在当前测试场景不影响结果）
- `RetryForegroundLangRequest`（IME 路径）：非必要（在当前测试场景不影响结果）
- `RetryEnableProfile`：非必要（日志持续 `enabled=True`）

## 当前行为总结
- 仍会先尝试一次 `ActivateLanguageProfile`。
- 若失败（常见 `0x80070057`），进入串行重试链。
- 对当前环境，真正使切换成功的是：
  1. `ChangeCurrentLanguage`
  2. `ActivateLanguageProfile(Retry)`

## 风险与兼容性说明
- 若面向他人环境，建议保留“按需兼容回退”能力，不建议直接彻底删除所有非必要步骤。
- 推荐策略：
  - 默认最小链（快、清晰）
  - 在特定条件（如 profile disabled）再启用兼容步骤
  - 保留可配置开关

## 后续优化建议
1. 收敛默认链到最小必要步骤，减少噪声和维护成本。  
2. 将兼容步骤改为条件触发，而非每次失败都全链执行。  
3. 保留自动诊断工具，用于跨软件/跨机器回归验证。  

