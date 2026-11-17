#!/usr/bin/env node
// 统计设置页真正绑定到 Settings 类的项数。
// 用法： node count-settings-bindings.js "Ink Canvas/Resources/Settings.cs" "Ink Canvas/Windows/SettingsViews/Pages"
// 输出：每页绑定项数 + 唯一项总数 + 声明总数（分母）
const fs = require("fs"), path = require("path");

const settingsFile = process.argv[2];
const pageDir = process.argv[3];
if (!settingsFile || !pageDir) {
  console.error("用法: node count-settings-bindings.js <Settings.cs> <Pages 目录>");
  process.exit(1);
}

const src = fs.readFileSync(settingsFile, "utf8");

// 按花括号配对切出每个 class 的方法体（不能用「切到下一个 class」——嵌套会错位）
function extractClasses(text) {
  const out = [];
  const re = /public\s+(?:partial\s+)?class\s+(\w+)[^{]*\{/g;
  let m;
  while ((m = re.exec(text))) {
    let i = re.lastIndex - 1, depth = 0;
    const start = i;
    for (; i < text.length; i++) {
      if (text[i] === "{") depth++;
      else if (text[i] === "}") { depth--; if (depth === 0) break; }
    }
    out.push({ name: m[1], body: text.slice(start, i + 1) });
  }
  return out;
}

// 自动属性 { get; set; } 与公共字段都算「设置项」
const propRe = /public\s+([\w<>\[\],?\s\.]+?)\s+(\w+)\s*\{\s*get;\s*set;\s*\}/g;
const fieldRe = /public\s+([\w<>\[\],?\s\.]+?)\s+(\w+)\s*(?:=[^;]*)?;/g;

const classProps = new Map();   // className -> Set(propName)
const classTypes = new Map();   // className -> Map(propName -> declaredType)
for (const c of extractClasses(src)) {
  const names = new Set(), types = new Map();
  let pm;
  propRe.lastIndex = 0;
  while ((pm = propRe.exec(c.body))) { names.add(pm[2]); types.set(pm[2], pm[1].trim()); }
  fieldRe.lastIndex = 0;
  while ((pm = fieldRe.exec(c.body))) { names.add(pm[2]); types.set(pm[2], pm[1].trim()); }
  if (names.size) { classProps.set(c.name, names); classTypes.set(c.name, types); }
}

const topNames = classProps.get("Settings") || new Set();
const topTypes = classTypes.get("Settings") || new Map();
// 顶层项的类型若本身是我们解析到的类，它就是「类别」，不是叶子项
const isCategory = (n) => topNames.has(n) && classProps.has(topTypes.get(n));

const files = fs.readdirSync(pageDir).filter(f => f.endsWith(".xaml") || f.endsWith(".xaml.cs"));
const perPage = new Map();
const re = /\b(?:SettingsManager\.)?[Ss]ettings((?:\.[A-Za-z_][A-Za-z0-9_]*)+)/g;

for (const f of files) {
  const page = f.replace(/\.xaml\.cs$/, "").replace(/\.(xaml|cs)$/, "");
  const t = fs.readFileSync(path.join(pageDir, f), "utf8");
  let g;
  while ((g = re.exec(t))) {
    const parts = g[1].split(".").filter(Boolean);
    const cat = parts[0], prop = parts[1];
    let item = null;
    if (parts.length === 1) {
      if (topNames.has(cat) && !isCategory(cat)) item = "Settings." + cat;
    } else if (isCategory(cat)) {
      const props = classProps.get(topTypes.get(cat));
      if (props && props.has(prop)) item = topTypes.get(cat) + "." + prop;
    }
    if (item) { if (!perPage.has(page)) perPage.set(page, new Set()); perPage.get(page).add(item); }
  }
}

const allPages = [...new Set(files.map(f => f.replace(/\.xaml\.cs$/, "").replace(/\.(xaml|cs)$/, "")))].sort();
const uniq = new Set();
for (const s of perPage.values()) for (const x of s) uniq.add(x);

const rows = [...perPage].map(([p, s]) => ({ p, n: s.size }))
  .sort((a, b) => b.n - a.n || a.p.localeCompare(b.p));

let declared = 0;
for (const [cls, props] of classProps) if (cls !== "Settings") declared += props.size;

console.log("| 页 | 绑定项数 |");
console.log("|---|---|");
for (const r of rows) console.log("| " + r.p + " | " + r.n + " |");
console.log("| **合计（按页相加）** | **" + rows.reduce((a, b) => a + b.n, 0) + "** |");
console.log("");
console.log("唯一项总数（去重）:", uniq.size);
console.log("按页相加:", rows.reduce((a, b) => a + b.n, 0));
console.log("零绑定页:", allPages.filter(p => !perPage.has(p)).join(", ") || "(无)");
console.log("Settings 子类声明总数（分母）:", declared);
