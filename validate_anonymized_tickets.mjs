import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const path = "outputs/01a0c416-14cb-7de0-a6f3-7fd359d22329/tickets_anonymized_with_500_samples.xlsx";
const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(path));
const sheet = workbook.worksheets.getItem("Tickets");
const rows = sheet.getRange("A2:H801").values;
const ids = rows.map((r) => String(r[0] ?? ""));
const descriptions = rows.map((r) => String(r[3] ?? "").trim());
const commentMismatches = rows.filter((r) => Number(r[4]) !== ((String(r[5] ?? "").match(/\[Comment /g) ?? []).length));
const sensitivePattern = /cmcglobal|@cmc|https?:\/\/|data-email=|\b(?:10\.|192\.168\.|172\.(?:1[6-9]|2\d|3[01])\.)|\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/i;
const sensitiveMatches = [];
for (let r = 0; r < rows.length; r += 1) {
  for (let c = 0; c < rows[r].length; c += 1) {
    if (typeof rows[r][c] === "string" && sensitivePattern.test(rows[r][c])) {
      sensitiveMatches.push({ row: r + 2, col: c + 1, value: rows[r][c].slice(0, 120) });
    }
  }
}
const apps = new Set(rows.map((r) => String(r[1] ?? "")));
const result = {
  records: rows.length,
  anonymizedRecords: ids.filter((id) => id.startsWith("TKT-ANON-")).length,
  newSyntheticRecords: ids.filter((id) => id.startsWith("TKT-SYN-")).length,
  uniqueIds: new Set(ids).size,
  appCategories: apps.size,
  blankDescriptions: descriptions.filter((v) => !v).length,
  commentCountMismatches: commentMismatches.length,
  sensitiveMatches: sensitiveMatches.length,
};
console.log(JSON.stringify(result, null, 2));
if (
  result.records !== 800 ||
  result.anonymizedRecords !== 300 ||
  result.newSyntheticRecords !== 500 ||
  result.uniqueIds !== 800 ||
  result.blankDescriptions !== 0 ||
  result.commentCountMismatches !== 0 ||
  result.sensitiveMatches !== 0
) {
  console.error(JSON.stringify({ sensitiveMatches: sensitiveMatches.slice(0, 10) }, null, 2));
  process.exit(1);
}
