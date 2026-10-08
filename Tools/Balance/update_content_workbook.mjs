import fs from "node:fs/promises";
import { pathToFileURL } from "node:url";

const artifactToolPath = process.env.ARTIFACT_TOOL_PATH;
if (!artifactToolPath) throw new Error("ARTIFACT_TOOL_PATH must point to artifact_tool.mjs");
const { FileBlob, SpreadsheetFile } = await import(pathToFileURL(artifactToolPath).href);

const [workbookPath, contentPath, previewDir] = process.argv.slice(2);
if (!workbookPath || !contentPath || !previewDir) {
  throw new Error("Usage: node update_content_workbook.mjs <workbook.xlsx> <GameContent.json> <preview-dir>");
}

await fs.mkdir(previewDir, { recursive: true });
const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(workbookPath));
const content = JSON.parse(await fs.readFile(contentPath, "utf8"));

const design = workbook.worksheets.getItem("设计总表");
const designValues = design.getUsedRange().values;
const designUpdates = new Map([
  ["D005|黑色四叶草", {
    effect: "过叶后，该球下一次有效得分结算的RUSH检定概率设为65%，结算时最多一次检定",
    note: "Recommended：75%→65%；与肾上腺素合计88%，避免100%确定点火。",
  }],
  ["D008|增值透镜", {
    effect: "穿过后当前价值×1.85；同一装置遵循每球触发上限",
    note: "Recommended：×2.00→×1.85，降低回流路线复利速度。",
  }],
  ["D009|资本主义", {
    effect: "球价值<6时销毁；价值≥6时当前价值×2.70",
    note: "Recommended：×3.00→×2.70；与透镜串联由×6降至×4.995。",
  }],
  ["M001|肾上腺素", {
    effect: "6秒内发射球的RUSH检定概率+23%；每次发射血耗×1.15。",
    duration: "6秒发射窗口",
    cost: "期间血液消耗×1.15",
    note: "Recommended：+25pp→+23pp；价格24→21。",
  }],
  ["M002|烧烧果实", {
    effect: "6秒内发射球基础价值×0.90；若在RUSH期间结算，得分额外×1.68。",
    duration: "6秒发射窗口",
    note: "Recommended：RUSH倍率×1.75→×1.68；价格24→22。",
  }],
  ["M003|延长药剂", {
    effect: "6秒内发射球触发RUSH时，持续时间×1.45；每次发射血耗×1.15。",
    duration: "6秒发射窗口",
    cost: "期间血液消耗×1.15",
    note: "Recommended：持续倍率×1.50→×1.45；价格15→12。",
  }],
  ["M009|无限增值", {
    effect: "6秒窗口内发射球体积×1.5、血耗×2.0；接触装置或圆钉时复制，整条复制链最多2代。",
    duration: "6秒发射窗口",
    stacking: "同类刷新、不叠层；复制链最多2代",
    cost: "血耗×2.0、体积×1.5",
    note: "安全修复：10秒→6秒，允许叠加→否；2^n递归改为最多2代。",
  }],
]);

for (let row = 0; row < designValues.length; row += 1) {
  const sourceId = designValues[row]?.[0];
  const name = designValues[row]?.[1];
  const update = designUpdates.get(`${sourceId}|${name}`);
  if (!update) continue;
  if (update.effect) design.getCell(row, 7).values = [[update.effect]];
  if (update.duration) design.getCell(row, 10).values = [[update.duration]];
  if (update.stacking) design.getCell(row, 12).values = [[update.stacking]];
  if (update.cost) design.getCell(row, 14).values = [[update.cost]];
  if (update.note) design.getCell(row, 15).values = [[update.note]];
  design.getCell(row, 18).values = [["已模拟"]];
}

const params = workbook.worksheets.getItem("效果参数");
const paramValues = params.getUsedRange().values;
const parameterUpdates = new Map([
  ["D005-01", { value: 0.65 }],
  ["M001-01", { value: 0.23, duration: 6 }],
  ["M001-02", { value: 1.15, duration: 6 }],
  ["M002-01", { value: 1.68, duration: 6 }],
  ["M002-02", { value: 0.90, duration: 6 }],
]);
for (let row = 0; row < paramValues.length; row += 1) {
  const update = parameterUpdates.get(paramValues[row]?.[0]);
  if (!update) continue;
  params.getCell(row, 4).values = [[update.value]];
  if (update.duration !== undefined) params.getCell(row, 8).values = [[update.duration]];
  params.getCell(row, 12).values = [["Recommended模拟校准"]];
}

const effectText = (category, definition) => {
  if (category === "装置") {
    const kind = Number(definition.Kind ?? 0);
    if (definition.Id === 113) return `得分倍率×${definition.ScoreMultiplier}`;
    if (definition.Id === 114) return `RUSH概率+${Math.round(definition.RushChanceAdd * 100)}pp`;
    const byKind = {
      1: `银行换金系数×${definition.Value}`,
      2: `价值×${definition.Value}（需两塔连线）`,
      3: `出口速度+${definition.Value}`,
      4: "成对双向传送",
      5: `RUSH概率SET ${Math.round(definition.Value * 100)}%`,
      6: "价值≥2时二分且总价值守恒",
      7: `弹射强度${definition.Value}`,
      8: `价值×${definition.Value}`,
      9: `价值<6销毁，否则×${definition.Value}`,
      10: `储值阈值${definition.Value}`,
      11: `速度×${definition.Value}`,
      12: "返还原始血耗（家族去重）",
      13: "磁场半径20cm",
    };
    return byKind[kind] ?? definition.Description;
  }
  const effects = (definition.Effects ?? []).map((effect) => `${effect.Attribute}:${effect.Operation}:${effect.Value}`).join("；");
  const special = [];
  if (definition.RushChanceAdd) special.push(`RUSH+${Math.round(definition.RushChanceAdd * 100)}pp`);
  if (definition.RushScoreMultiplier && definition.RushScoreMultiplier !== 1) special.push(`RUSH得分×${definition.RushScoreMultiplier}`);
  if (definition.VolumeMultiplier && definition.VolumeMultiplier !== 1) special.push(`体积×${definition.VolumeMultiplier}`);
  return [...special, effects ? `属性[${effects}]` : ""].filter(Boolean).join("；") || definition.Description;
};

const changes = new Map([
  ["装置:104", ["RUSH SET 75%", "RUSH SET 65%", "-13.3%", "保留高概率点火，但与肾上腺素组合不再100%。"]],
  ["装置:107", ["价值×2.00", "价值×1.85", "-7.5%", "降低回流复利；仍保留Epic爆发定位。"]],
  ["装置:108", ["价值×3.00", "价值×2.70", "-10.0%", "与透镜串联倍率由×6降为×4.995。"]],
  ["装置:113", ["得分×2.00", "得分×1.85", "-7.5%", "降低多实例乘法触顶速度。"]],
  ["装置:114", ["RUSH+12pp", "RUSH+11pp", "-8.3%", "小幅压低无条件概率叠加。"]],
  ["药物:100", ["价格24；RUSH+25pp", "价格21；RUSH+23pp", "价格-12.5%；效果-8.0%", "降低爆发并降价，维持与其他Rush药的选择竞争。"]],
  ["药物:101", ["价格24；RUSH×1.75", "价格22；RUSH×1.68", "价格-8.3%；增量-9.3%", "压低峰值但保留高时机收益。"]],
  ["药物:102", ["价格15；时长×1.50", "价格12；时长×1.45", "价格-20.0%；增量-10.0%", "削弱单次覆盖、降价维持辅助路线可选。"]],
  ["药物:107", ["10秒；可叠加；递归不限代", "6秒；不叠加；最多2代", "窗口-40%；上限新增", "修复2^n球数爆炸，单颗原球最多扩展至4球。"]],
]);

const modeledDevices = new Set([100, 101, 104, 107, 108, 111, 113, 114]);
const modeledDrugs = new Set([100, 101, 102]);
const runtimeRows = [];
for (const [category, definitions] of [["装置", content.Devices], ["药物", content.Drugs]]) {
  for (const definition of definitions) {
    const key = `${category}:${definition.Id}`;
    const change = changes.get(key) ?? ["—", "—", "0%", "本轮保持；等待对应物理变体样本后再调效果。"];
    const modeled = category === "装置" ? modeledDevices.has(definition.Id) : modeledDrugs.has(definition.Id);
    const confidence = definition.Id === 107 && category === "药物" ? "规则安全修复" : modeled ? "Hybrid已建模" : "物理变体待采样";
    runtimeRows.push([
      category, definition.Id, definition.SourceId ?? "", definition.Name, definition.Price,
      effectText(category, definition), category === "药物" ? definition.DurationSeconds : "—",
      category === "药物" ? (definition.Stackable ? "是" : "否") : "—", confidence,
      change[0], change[1], change[2], change[3],
    ]);
  }
}

const runtime = workbook.worksheets.add("运行时平衡表");
runtime.showGridLines = false;
runtime.mergeCells("A1:M1");
runtime.getRange("A1").values = [["《血色柏青哥》运行时道具 / 药物平衡表（Recommended）"]];
runtime.mergeCells("A2:M2");
runtime.getRange("A2").values = [["Source of Truth：Assets/Resources/GameContent.json。路线/物理型条目标记为“待采样”时，不根据启发式低EV擅自增强。"]];
const headers = [["类别", "Runtime ID", "Source ID", "名称", "价格", "当前核心效果", "持续秒", "可叠加", "证据等级", "旧值", "新值", "变化", "调整原因"]];
runtime.getRange("A4:M4").values = headers;
runtime.getRange(`A5:M${4 + runtimeRows.length}`).values = runtimeRows;
runtime.tables.add(`A4:M${4 + runtimeRows.length}`, true, "RuntimeBalanceTable");
runtime.freezePanes.freezeRows(4);
runtime.getRange("A1:M1").format = {
  fill: "#7A1F2B", font: { name: "Microsoft YaHei", bold: true, color: "#FFFFFF", size: 16 },
  horizontalAlignment: "center", verticalAlignment: "center",
};
runtime.getRange("A2:M2").format = {
  fill: "#F4E6E8", font: { name: "Microsoft YaHei", color: "#5F1B25", size: 10 }, wrapText: true,
};
runtime.getRange("A4:M4").format = {
  fill: "#2F3E46", font: { name: "Microsoft YaHei", bold: true, color: "#FFFFFF" },
  horizontalAlignment: "center", verticalAlignment: "center", wrapText: true,
};
runtime.getRange(`A5:M${4 + runtimeRows.length}`).format = {
  font: { name: "Microsoft YaHei", size: 10 }, verticalAlignment: "center", wrapText: true,
  borders: { preset: "all", style: "thin", color: "#D9D9D9" },
};
runtime.getRange(`B5:B${4 + runtimeRows.length}`).format.numberFormat = "0";
runtime.getRange(`E5:E${4 + runtimeRows.length}`).format.numberFormat = "0";
runtime.getRange("A1:M1").format.rowHeight = 28;
runtime.getRange("A2:M2").format.rowHeight = 34;
runtime.getRange("A4:M4").format.rowHeight = 30;
runtime.getRange(`A5:M${4 + runtimeRows.length}`).format.autofitRows();
for (const [range, width] of [
  ["A1:A31", 9], ["B1:B31", 10], ["C1:C31", 16], ["D1:D31", 20], ["E1:E31", 8], ["F1:F31", 38],
  ["G1:G31", 9], ["H1:H31", 9], ["I1:I31", 16], ["J1:K31", 24], ["L1:L31", 18], ["M1:M31", 42],
]) runtime.getRange(range).format.columnWidth = width;

workbook.recalculate();
const check = await workbook.inspect({
  kind: "region",
  sheetId: "运行时平衡表",
  range: `A1:M${4 + runtimeRows.length}`,
  maxChars: 16000,
  tableMaxRows: 35,
  tableMaxCols: 13,
  tableMaxCellChars: 140,
});
await fs.writeFile(`${previewDir}/runtime_balance_inspect.ndjson`, check.ndjson ?? String(check), "utf8");
for (const sheetName of ["运行时平衡表", "设计总表"]) {
  const preview = await workbook.render({ sheetName, autoCrop: "all", scale: 1, format: "png" });
  await fs.writeFile(`${previewDir}/${sheetName}.png`, new Uint8Array(await preview.arrayBuffer()));
}
const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(workbookPath);
