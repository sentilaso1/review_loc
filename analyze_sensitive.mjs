import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const workbook = await SpreadsheetFile.importXlsx(
  await FileBlob.load("D:/Down/tickets_r1_r5_with_comments.xlsx"),
);
const sheet = workbook.worksheets.getItem("Tickets");
const values = sheet.getRange("A1:E501").values;

const apps = new Map();
const emails = new Set();
const mentions = new Set();
const urls = new Set();
const likelyIds = new Set();
const orgTerms = new Set();

for (const row of values.slice(1)) {
  const app = String(row[1] ?? "").trim();
  if (app) apps.set(app, (apps.get(app) ?? 0) + 1);
  for (const cell of row.slice(1)) {
    if (typeof cell !== "string") continue;
    for (const m of cell.matchAll(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi)) emails.add(m[0]);
    for (const m of cell.matchAll(/data-mention="@([^"]+)"/gi)) mentions.add(m[1]);
    for (const m of cell.matchAll(/https?:\/\/[^\s"'<>]+/gi)) urls.add(m[0]);
    for (const m of cell.matchAll(/\b(?:[a-z]{1,5}[a-z0-9]{1,15}\d{1,3})\b/gi)) likelyIds.add(m[0]);
    for (const m of cell.matchAll(/\b(?:CMC(?:\s+Global|G|\s+APAC)?|AMS)\b/gi)) orgTerms.add(m[0]);
  }
}

console.log("APPS", JSON.stringify([...apps.entries()].sort((a,b)=>b[1]-a[1]), null, 2));
console.log("EMAIL_COUNT", emails.size, JSON.stringify([...emails].slice(0, 80), null, 2));
console.log("MENTION_COUNT", mentions.size, JSON.stringify([...mentions].slice(0, 80), null, 2));
console.log("URL_COUNT", urls.size, JSON.stringify([...urls].slice(0, 20), null, 2));
console.log("LIKELY_ID_COUNT", likelyIds.size, JSON.stringify([...likelyIds].slice(0, 120), null, 2));
console.log("ORG_TERMS", JSON.stringify([...orgTerms], null, 2));
