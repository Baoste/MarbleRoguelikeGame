import fs from "node:fs/promises";
import { pathToFileURL } from "node:url";

const artifactToolPath = process.env.ARTIFACT_TOOL_PATH;
if (!artifactToolPath) {
  throw new Error("ARTIFACT_TOOL_PATH must point to artifact_tool.mjs");
}
const { FileBlob, SpreadsheetFile } = await import(pathToFileURL(artifactToolPath).href);

const [inputPath, outputDir] = process.argv.slice(2);
if (!inputPath || !outputDir) {
  throw new Error("Usage: node inspect_content_workbook.mjs <input.xlsx> <output-dir>");
}

await fs.mkdir(outputDir, { recursive: true });
const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(inputPath));
const summary = await workbook.inspect({
  kind: "workbook,sheet,table",
  maxChars: 16000,
  tableMaxRows: 8,
  tableMaxCols: 16,
  tableMaxCellChars: 120,
});
await fs.writeFile(`${outputDir}/summary.ndjson`, summary.ndjson ?? String(summary), "utf8");

for (const sheetName of ["00_ATTRIBUTE_DICTIONARY (2)", "设计总表", "效果参数"]) {
  try {
    const region = await workbook.inspect({
      kind: "region",
      sheetId: sheetName,
      range: "A1:Z100",
      maxChars: 30000,
      tableMaxRows: 100,
      tableMaxCols: 26,
      tableMaxCellChars: 180,
    });
    await fs.writeFile(`${outputDir}/${sheetName.replaceAll(/[\\/:*?\"<>|]/g, "_")}.ndjson`, region.ndjson ?? String(region), "utf8");
    const preview = await workbook.render({ sheetName, autoCrop: "all", scale: 1, format: "png" });
    await fs.writeFile(`${outputDir}/${sheetName.replaceAll(/[\\/:*?\"<>|]/g, "_")}.png`, new Uint8Array(await preview.arrayBuffer()));
  } catch (error) {
    await fs.writeFile(`${outputDir}/${sheetName.replaceAll(/[\\/:*?\"<>|]/g, "_")}.error.txt`, String(error), "utf8");
  }
}
