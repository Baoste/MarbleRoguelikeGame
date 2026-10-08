import fs from "node:fs/promises";
import { pathToFileURL } from "node:url";

const artifactToolPath = process.env.ARTIFACT_TOOL_PATH;
if (!artifactToolPath) throw new Error("ARTIFACT_TOOL_PATH must point to artifact_tool.mjs");
const { FileBlob, SpreadsheetFile } = await import(pathToFileURL(artifactToolPath).href);

const [workbookPath, contentPath] = process.argv.slice(2);
const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(workbookPath));
const content = JSON.parse(await fs.readFile(contentPath, "utf8"));
const rows = workbook.worksheets.getItem("设计总表").getUsedRange().values;
const devices = new Map(content.Devices.map((row) => [row.Name, row]));
const drugs = new Map(content.Drugs.map((row) => [row.Name, row]));
const issues = [];
let checked = 0;
for (const row of rows) {
  const category = row?.[2];
  const name = row?.[1];
  if (category !== "装置" && category !== "药物") continue;
  const definition = (category === "装置" ? devices : drugs).get(name);
  if (!definition) continue;
  checked += 1;
  if (definition.Description !== row[7]) issues.push(`${name}: description mismatch workbook=${JSON.stringify(row[7])} json=${JSON.stringify(definition.Description)}`);
  if (category === "药物") {
    const match = String(row[10] ?? "").match(/(\d+(?:\.\d+)?)秒/);
    if (!match || Number(match[1]) !== Number(definition.DurationSeconds)) issues.push(`${name}: duration mismatch workbook=${JSON.stringify(row[10])} json=${definition.DurationSeconds}`);
    const workbookStackable = row[12] === "不限次数";
    if (workbookStackable !== Boolean(definition.Stackable)) issues.push(`${name}: stackable mismatch workbook=${JSON.stringify(row[12])} json=${definition.Stackable}`);
  }
}
if (issues.length) throw new Error(`Workbook validation failed:\n${issues.join("\n")}`);
const formulaCheck = await workbook.inspect({ kind: "formula", maxChars: 30000, options: { maxResults: 500 } });
const serialized = formulaCheck.ndjson ?? String(formulaCheck);
if (/#REF!|#DIV\/0!|#VALUE!|#NAME\?|#N\/A/.test(serialized)) throw new Error("Workbook still contains a formula error");
console.log(`Workbook/content validation passed: ${checked} authored runtime rows, no formula errors.`);
