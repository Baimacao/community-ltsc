#!/usr/bin/env node
/**
 * ICC-CE LTSC 代码级验证
 *
 * 教训（2026-11-10）：
 *   ① 不能只 grep 字符串——注释里的旧代码会命中，产生假阳性/假阴性
 *   ② 不能用贪心正则跨方法边界——[\\s\\S]{0,300} 会吃掉后面的方法
 *   ③ 必须剥注释 + 括号配对提取方法体
 *
 * 用法：node tools/verify.js [--verbose]
 */
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..', 'src', 'Ink Canvas');
const VERBOSE = process.argv.includes('--verbose');

/** 剥掉 // 行注释、/* *\/ 块注释、字符串内容保留但标记 */
function strip(src) {
  let out = '', i = 0, n = src.length;
  let inBlock = false, inLine = false, inStr = false, inChar = false, inVerb = false;
  while (i < n) {
    const c = src[i], c2 = src.slice(i, i + 2);
    if (inLine) { if (c === '\n') { inLine = false; out += c; } i++; continue; }
    if (inBlock) { if (c2 === '*/') { inBlock = false; i += 2; } else { if (c === '\n') out += c; i++; } continue; }
    if (inStr) { out += c; if (c === '\\') { out += src[i+1] ?? ''; i += 2; continue; } if (c === '"') inStr = false; i++; continue; }
    if (inChar) { out += c; if (c === '\\') { out += src[i+1] ?? ''; i += 2; continue; } if (c === "'") inChar = false; i++; continue; }
    // C# 逐字字符串 @"  或 $@" 或 @$"
    if (src.slice(i, i+2) === '@"' || src.slice(i, i+3) === '$@"' || src.slice(i, i+3) === '@$"') {
      const st = src[i] === '$' ? i + 3 : i + 2;
      out += src.slice(i, st); i = st; inVerb = true; continue;
    }
    if (inVerb) { out += c; if (c === '"') { if (src[i+1] === '"') { out += '"'; i += 2; continue; } inVerb = false; } i++; continue; }
    if (c2 === '//') { inLine = true; i += 2; continue; }
    if (c2 === '/*') { inBlock = true; i += 2; continue; }
    if (c === '"') { out += c; inStr = true; i++; continue; }
    if (c === "'") { out += c; inChar = true; i++; continue; }
    out += c; i++;
  }
  return out;
}

/** 按签名提取方法体（括号配对） */
function methodBody(src, sigRe) {
  const m = src.match(sigRe);
  if (!m || m.index === undefined) return null;
  const braceAt = src.indexOf('{', m.index + m[0].length - 1);
  if (braceAt < 0) return null;
  let d = 0;
  for (let i = braceAt; i < src.length; i++) {
    if (src[i] === '{') d++;
    else if (src[i] === '}') { d--; if (d === 0) return src.slice(braceAt, i + 1); }
  }
  return null;
}

const cache = new Map();
function read(rel, stripComments = true) {
  const key = rel + (stripComments ? ':S' : ':R');
  if (cache.has(key)) return cache.get(key);
  const p = path.join(ROOT, rel);
  let t = '';
  try { t = fs.readFileSync(p, 'utf8'); } catch { t = ''; }
  const v = stripComments ? strip(t) : t;
  cache.set(key, v);
  return v;
}

const checks = [];
function check(id, desc, fn) { checks.push({ id, desc, fn }); }

// ═══════════ P1：PPT 重建后应满足的 ═══════════
check('P1-OLD-1', 'MW_PPT 里不再有 CreatePowerPointApplication 定义',
  () => !/void\s+CreatePowerPointApplication\s*\(/.test(read('MainWindow_cs/MW_PPT.cs')));
check('P1-OLD-2', 'MW_PPT 里不再有 ClosePowerPointApplication 定义',
  () => !/void\s+ClosePowerPointApplication\s*\(/.test(read('MainWindow_cs/MW_PPT.cs')));
check('P1-OLD-3', 'MW_PPT 里不再有 StartPowerPointProcessMonitoring 定义',
  () => !/void\s+StartPowerPointProcessMonitoring\s*\(/.test(read('MainWindow_cs/MW_PPT.cs')));
check('P1-OLD-4', 'MW_PPT 里不再碰 Application.Visible',
  () => !/\.Visible\s*=\s*MsoTriState/.test(read('MainWindow_cs/MW_PPT.cs')));
check('P1-OLD-5', 'MW_PPT 里不再 .Quit()',
  () => !/pptApplication\.Quit\(\)/.test(read('MainWindow_cs/MW_PPT.cs')));
check('P1-LINK-1', 'PPTLinkMode 的 != Com 死判断已清零',
  () => (read('MainWindow_cs/MW_PPT.cs').match(/PPTLinkMode\s*!=\s*PPTLinkMode\.Com/g) || []).length === 0);
check('P1-FIELD-1', 'MW_Save&OpenStrokes 里不再引用 pptApplication',
  () => !/pptApplication/.test(read('MainWindow_cs/MW_Save&OpenStrokes.cs')));
check('P1-PAGE-1', '设置页 PPT 架构下拉已删',
  () => !/ComboBoxPPTArchitecture/.test(read('Windows/SettingsViews/Pages/PowerPointPage.xaml.cs')));

// ═══════════ 已完成的历史修复（回归保护）═══════════
check('H-1', 'HardwareAcceleratedInkProcessor 无 Task.Run',
  () => !/Task\.Run/.test(read('Helpers/HardwareAcceleratedInkProcessor.cs')));
check('H-1b', 'InkSmoothingManager 无 .Wait(',
  () => !/\.Wait\s*\(/.test(read('Helpers/InkSmoothingManager.cs')));
check('C-3', 'InkFadeManager _segmentedTimers ≥4 处',
  () => (read('Helpers/InkFadeManager.cs').match(/_segmentedTimers/g) || []).length >= 4);
check('C-4', 'MW_AutoFold finally 复位标志',
  () => /finally\s*\{[^}]*isFloatingBarChangingHideMode\s*=\s*false/s.test(read('MainWindow_cs/MW_AutoFold.cs')));
check('C-5', 'App 心跳 Process 走 using',
  () => /using\s*\([^)]*GetCurrentProcess\(\)/s.test(read('App.xaml.cs')));
check('UI-1b', 'FileAssociationManager 无 Thread.Sleep(1000)',
  () => !/Thread\.Sleep\(\s*1000\s*\)/.test(read('Helpers/FileAssociationManager.cs')));
check('UI-1c', 'IpcIACoreClient 无 Thread.Sleep(100)',
  () => !/Thread\.Sleep\(\s*100\s*\)/.test(read('Helpers/IpcIACoreClient.cs')));
check('L-1', 'PPTQuickPanel TrimPptImages 调用 ≥4',
  () => (read('Windows/PPTQuickPanel.xaml.cs').match(/TrimPptImages\(\)/g) || []).length >= 4);
check('L-3', 'PptBridge RaisePresentationEvent 有 ReferenceEquals + SafeRelease',
  () => /ReferenceEquals\(owned,\s*pres\)/.test(read('Helpers/PptBridge.cs')) && /SafeRelease\(owned\)/.test(read('Helpers/PptBridge.cs')));
check('L-4', 'MW_Save&OpenStrokes FileStream 走 using',
  () => /using \(var fs = new FileStream/.test(read('MainWindow_cs/MW_Save&OpenStrokes.cs')));
check('L-5', 'App 里 IWindowOverviewService 只注册 1 次',
  () => (read('App.xaml.cs').match(/typeof\(Plugins\.IWindowOverviewService\)/g) || []).length === 1);
check('L-8', 'PptBridge 轮询无 dynamic app',
  () => { const b = methodBody(read('Helpers/PptBridge.cs'), /void RefreshSlideShowState\(\)/); return !!b && !/dynamic app = _comApplication/.test(b) && /SafeRelease\(view\)/.test(b); });
check('L-10', 'InkFadeManager 先建 visual 再写字典',
  () => { const t = read('Helpers/InkFadeManager.cs'); const a = t.indexOf('var strokeVisual = CreateStrokeVisual(stroke);'); const b = t.indexOf('_strokeStartPoints[stroke] = startPoint;'); return a >= 0 && b >= 0 && a < b; });
check('L-12', 'MainWindow UserPreferenceChanged 配对退订',
  () => /SystemEvents\.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged/.test(read('MainWindow.xaml.cs')));
check('L-16', 'MW_FloatingBarIcons 有 ReleasePopupManager',
  () => /internal void ReleasePopupManager\(\)/.test(read('MainWindow_cs/MW_FloatingBarIcons.cs')));
check('A-1', 'MW_Timer 有重入闩 + 缓存',
  () => { const t = read('MainWindow_cs/MW_Timer.cs'); return /_autoFoldTickBusy/.test(t) && /IsSlideShowWindowExistedCached/.test(t); });
check('A-4', 'MW_PPT 长按定时器解绑',
  () => /_longPressTimer\.Tick -= OnLongPressTimerTick/.test(read('MainWindow_cs/MW_PPT.cs')));
check('A-6', 'MW_UriHandler 无 Invoke(async lambda)',
  () => !/Dispatcher\.Invoke\(async/.test(read('MainWindow_cs/MW_UriHandler.cs')));
check('UI-2', 'App 无 updateProcess 轮询',
  () => !/waitCount < maxWaitCount/.test(read('App.xaml.cs')));
check('D-01', 'App 无更新下载/安装调用',
  () => !/DownloadUpdate|InstallUpdate|CheckForUpdatesAsync/.test(read('App.xaml.cs')));
check('D-04', 'ProcessProtectionManager 是直通空壳',
  () => /public static bool Enabled => false;/.test(read('Helpers/ProcessProtectionManager.cs')));

// ═══════════ P4：依赖清理后应满足的 ═══════════
const cj = read('../../Ink Canvas/InkCanvasForClass.csproj', false);
check('P4-1', 'csproj 已无 MdXaml',
  () => !/Include="MdXaml"/.test(read('../Ink Canvas/InkCanvasForClass.csproj', false)));
check('P4-2', 'csproj 已无 Uwp.Notifications',
  () => !/Uwp\.Notifications/.test(read('../Ink Canvas/InkCanvasForClass.csproj', false)));

// ═══════════ 干净性检查（永远该满足）═══════════
check('CLEAN-1', '源码里没有 LTSC 临时探针残留',
  () => !/LTSC 临时探针|DisableComEventsForProbe|ProbeForceFullRelease/.test(read('App.xaml.cs') + read('Helpers/PptBridge.cs')));

// ═══════════ 执行 ═══════════
let pass = 0, fail = 0, skip = 0;
const failed = [];
console.log('ICC-CE LTSC 代码级验证');
console.log('='.repeat(70));
for (const c of checks) {
  let ok = false, err = '';
  try { ok = c.fn(); } catch (e) { ok = false; err = e.message; }
  if (ok) { pass++; console.log('  PASS  ' + c.id.padEnd(12) + c.desc); }
  else { fail++; failed.push(c.id); console.log('  FAIL  ' + c.id.padEnd(12) + c.desc + (err ? '  [' + err + ']' : '')); }
}
console.log('='.repeat(70));
console.log('总计 ' + checks.length + '  通过 ' + pass + '  失败 ' + fail);
if (failed.length) {
  console.log('\n失败项：' + failed.join(', '));
  process.exit(1);
}
