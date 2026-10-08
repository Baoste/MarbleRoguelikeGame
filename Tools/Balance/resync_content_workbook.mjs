import fs from "node:fs/promises";
import { pathToFileURL } from "node:url";

const artifactToolPath = process.env.ARTIFACT_TOOL_PATH;
if (!artifactToolPath) throw new Error("ARTIFACT_TOOL_PATH must point to artifact_tool.mjs");
const { FileBlob, SpreadsheetFile } = await import(pathToFileURL(artifactToolPath).href);

const [workbookPath, contentPath, previewDir] = process.argv.slice(2);
const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(workbookPath));
const content = JSON.parse(await fs.readFile(contentPath, "utf8"));
await fs.mkdir(previewDir, { recursive: true });

const design = workbook.worksheets.getItem("设计总表");
// Use an absolute A1 range so matrix indexes map directly to Excel row numbers;
// getUsedRange().values can trim the leading blank table row on import.
const values = design.getRange("A1:S50").values;
const devices = new Map(content.Devices.map((row) => [row.Name, row]));
const drugs = new Map(content.Drugs.map((row) => [row.Name, row]));
const falseStackRule = new Map([
  ["肾上腺素", "同类仅刷新窗口；不叠层"],
  ["烧烧果实", "同类刷新，不叠层"],
  ["延长药剂", "同类刷新，不叠层"],
  ["润流剂", "同类刷新，不叠层"],
  ["凝血剂", "同类刷新，不叠层"],
  ["无限增值", "同类刷新、不叠层；复制链最多2代"],
  ["定向试剂（补全名）", "同类刷新，不叠层"],
]);

for (let index = 0; index < values.length; index += 1) {
  const excelRow = index + 1;
  const category = values[index]?.[2];
  const name = values[index]?.[1];
  const definition = category === "装置" ? devices.get(name) : category === "药物" ? drugs.get(name) : null;
  if (!definition) continue;
  design.getRange(`H${excelRow}`).values = [[definition.Description]];
  if (category === "药物") {
    design.getRange(`K${excelRow}`).values = [[`${definition.DurationSeconds}秒发射窗口`]];
    design.getRange(`M${excelRow}`).values = [[definition.Stackable ? "不限次数" : (falseStackRule.get(name) ?? "同类刷新，不叠层")]];
  }
}

// Remove cells accidentally populated on the separator row by the first edit pass.
design.getRange("H19:S19").clear({ applyTo: "contents" });

const rowByName = new Map();
for (let index = 0; index < values.length; index += 1) rowByName.set(values[index]?.[1], index + 1);
const notes = new Map([
  ["黑色四叶草", "Recommended：75%→65%；与肾上腺素合计88%，避免100%确定点火。"],
  ["增值透镜", "Recommended：×2.00→×1.85，降低回流路线复利速度。"],
  ["资本主义", "Recommended：×3.00→×2.70；与透镜串联由×6降至×4.995。"],
  ["肾上腺素", "Recommended：+25pp→+23pp；价格24→21。"],
  ["烧烧果实", "Recommended：RUSH倍率×1.75→×1.68；价格24→22。"],
  ["延长药剂", "Recommended：持续倍率×1.50→×1.45；价格15→12。"],
  ["无限增值", "安全修复：10秒→6秒，允许叠加→否；2^n递归改为最多2代。"],
]);
for (const [name, note] of notes) {
  const row = rowByName.get(name);
  design.getRange(`P${row}`).values = [[note]];
  design.getRange(`S${row}`).values = [["已模拟"]];
}
// Restore cells on rows that received a shifted note in the first pass.
design.getRange(`P${rowByName.get("传送门")}`).values = [["全体传送共享0.5秒球级锁，阻止多门环路"]];
design.getRange(`S${rowByName.get("传送门")}`).values = [["待测试"]];
for (const name of ["弹板", "稀血剂"]) {
  design.getRange(`P${rowByName.get(name)}`).clear({ applyTo: "contents" });
  design.getRange(`S${rowByName.get(name)}`).clear({ applyTo: "contents" });
}

const params = workbook.worksheets.getItem("效果参数");
const paramValues = params.getRange("A1:N60").values;
const paramRows = new Map();
for (let index = 0; index < paramValues.length; index += 1) paramRows.set(paramValues[index]?.[0], index + 1);
const parameterUpdates = new Map([
  ["D004-01", [1, 0, "新增建议"]],
  ["D005-01", [0.65, null, "Recommended模拟校准"]],
  ["D007-03", [0.5, null, "原表"]],
  ["M001-01", [0.23, 6, "Recommended模拟校准"]],
  ["M001-02", [1.15, 6, "Recommended模拟校准"]],
  ["M002-01", [1.68, 6, "Recommended模拟校准"]],
  ["M002-02", [0.90, 6, "Recommended模拟校准"]],
]);
for (const [id, [value, duration, basis]] of parameterUpdates) {
  const row = paramRows.get(id);
  params.getRange(`E${row}`).values = [[value]];
  if (duration === null) params.getRange(`I${row}`).clear({ applyTo: "contents" });
  else params.getRange(`I${row}`).values = [[duration]];
  params.getRange(`M${row}`).values = [[basis]];
}

workbook.recalculate();
const preview = await workbook.render({ sheetName: "设计总表", autoCrop: "all", scale: 1, format: "png" });
await fs.writeFile(`${previewDir}/设计总表_复核.png`, new Uint8Array(await preview.arrayBuffer()));
const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(workbookPath);
