import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const inputPath = "tickets_r1_r5_with_comments.xlsx";
const outputDir = "outputs/workbook_inspection";
await fs.mkdir(outputDir, { recursive: true });

const input = await FileBlob.load(inputPath);
const workbook = await SpreadsheetFile.importXlsx(input);

const summary = await workbook.inspect({
  kind: "workbook,sheet,table,thread,definedName,drawing",
  maxChars: 16000,
  tableMaxRows: 12,
  tableMaxCols: 20,
  tableMaxCellChars: 160,
  options: { maxResults: 200 },
});
console.log("SUMMARY");
console.log(summary.ndjson);

const sheets = await workbook.inspect({ kind: "sheet", include: "id,name", maxChars: 5000 });
console.log("SHEETS");
console.log(sheets.ndjson);

for (let i = 0; i < workbook.worksheets.items.length; i += 1) {
  const sheet = workbook.worksheets.getItemAt(i);
  const used = sheet.getUsedRange();
  console.log(`SHEET ${i}: ${sheet.name} USED ${used?.address ?? "none"}`);
  if (used) {
    const range = await workbook.inspect({
      kind: "table",
      sheetId: sheet.name,
      range: used.address,
      include: "values,formulas",
      maxChars: 18000,
      tableMaxRows: 18,
      tableMaxCols: 24,
      tableMaxCellChars: 200,
    });
    console.log(range.ndjson);
    const previewRange = `${sheet.getRange("A1:G20").address}`;
    const preview = await workbook.render({
      sheetName: sheet.name,
      range: previewRange,
      scale: 1,
      format: "png",
    });
    await fs.writeFile(`${outputDir}/sheet_${i + 1}.png`, new Uint8Array(await preview.arrayBuffer()));
  }
}
