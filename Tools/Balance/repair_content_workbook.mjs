import fs from "node:fs/promises";
import { pathToFileURL } from "node:url";

const artifactToolPath = process.env.ARTIFACT_TOOL_PATH;
if (!artifactToolPath) throw new Error("ARTIFACT_TOOL_PATH must point to artifact_tool.mjs");
const { FileBlob, SpreadsheetFile } = await import(pathToFileURL(artifactToolPath).href);

const [workbookPath, previewDir] = process.argv.slice(2);
if (!workbookPath || !previewDir) throw new Error("Usage: node repair_content_workbook.mjs <workbook.xlsx> <preview-dir>");
await fs.mkdir(previewDir, { recursive: true });

const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(workbookPath));
const sandbox = workbook.worksheets.getItem("平衡沙盘");

// Sync the visible comparison rows with the adopted Recommended values.
sandbox.getRange("A9:G11").values = [
  ["Recommended：肾上腺素与四叶草", 0.65, 0.23, 1.0, null, "SET值、ADD值、概率封顶", "65%+23%=88%，不再确定点火"],
  ["旧版组合（对照）", 0.75, 0.25, 1.0, null, "旧SET值、旧ADD值、概率封顶", "旧版合计100%，作为改动前对照"],
  ["RUSH触发的族数效应", 0.23, 5, 0, null, "单次概率、独立球家族数、预留", "至少触发一次的概率；仅首次启动前独立检定"],
];
sandbox.getRange("E9").formulas = [["=MIN(B9+C9,D9)"]];
sandbox.getRange("E10").formulas = [["=MIN(B10+C10,D10)"]];
sandbox.getRange("E11").formulas = [["=1-(1-B11)^C11"]];

// Repair pre-existing broken references: the workbook has no authored GAMBLE_EV
// row and the quadruple chance row had been deleted.  Keep the input explicit,
// derive EV locally, and compare against the adopted design target 1.40.
sandbox.getRange("D14").values = [[0.15]];
sandbox.getRange("E14").formulas = [["=B14+C14*2+D14*4"]];
sandbox.getRange("A15").values = [["相对目标EV 1.40差额"]];
sandbox.getRange("E15").formulas = [["=E14-1.4"]];
sandbox.getRange("F15").values = [["推导期望减正式目标1.40"]];
sandbox.getRange("G15").values = [["0表示四档概率与正式风险预算一致"]];
sandbox.getRange("E16").formulas = [["=E14+B16*C16"]];
sandbox.getRange("E17").formulas = [["=B16+B14+C14+D14"]];

workbook.recalculate();
const check = await workbook.inspect({
  kind: "region,formula",
  sheetId: "平衡沙盘",
  range: "A4:G17",
  maxChars: 12000,
  tableMaxRows: 20,
  tableMaxCols: 7,
});
await fs.writeFile(`${previewDir}/balance_sandbox_inspect.ndjson`, check.ndjson ?? String(check), "utf8");
const preview = await workbook.render({ sheetName: "平衡沙盘", autoCrop: "all", scale: 1, format: "png" });
await fs.writeFile(`${previewDir}/平衡沙盘.png`, new Uint8Array(await preview.arrayBuffer()));
const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(workbookPath);
