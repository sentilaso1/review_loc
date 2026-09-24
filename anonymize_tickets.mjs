import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const inputPath = "D:/Down/tickets_r1_r5_with_comments.xlsx";
const outputDir = "D:/Down/12334444-test2-f21b6117f65f/12334444-test2-f21b6117f65f/outputs/01a0c483-1689-7ab1-9200-955b17241afa";
const outputPath = `${outputDir}/tickets_r1_r5_anonymized.xlsx`;
const previewPath = `${outputDir}/tickets_r1_r5_anonymized_preview.png`;

const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(inputPath));
const sheet = workbook.worksheets.getItem("Tickets");
const used = sheet.getUsedRange();
const values = used.values;

const userByKey = new Map();
const usernameToUser = new Map();
const displayToUser = new Map();
const referenceMap = new Map();
let nextUser = 1;
let nextReference = 1;

function createUser(key) {
  const normalized = String(key).trim().toLowerCase();
  if (!userByKey.has(normalized)) {
    userByKey.set(normalized, `User_${String(nextUser++).padStart(3, "0")}`);
  }
  return userByKey.get(normalized);
}

function userForEmail(email) {
  const local = email.split("@")[0].toLowerCase();
  const user = createUser(`account:${local}`);
  usernameToUser.set(local, user);
  return user;
}

function userForDisplay(display, email = "") {
  if (email && email.toLowerCase() !== "undefined") {
    const user = userForEmail(email);
    displayToUser.set(display.toLowerCase(), user);
    return user;
  }
  const clean = display
    .replace(/^@/, "")
    .replace(/\s+-\s+.*$/i, "")
    .trim();
  const user = createUser(`display:${clean.toLowerCase()}`);
  displayToUser.set(display.toLowerCase(), user);
  displayToUser.set(clean.toLowerCase(), user);
  return user;
}

function isLikelyUserToken(token) {
  if (!/^[A-Za-z][A-Za-z0-9._-]{2,24}\d{1,3}$/i.test(token)) return false;
  if (/^(?:DU|DX|SBU|BU|G|TSAP|THABU|SK|SC|FY|Q|S)\d/i.test(token)) return false;
  if (/^[a-f0-9]{8,}$/i.test(token)) return false;
  return true;
}

// Build deterministic identity maps in source order before replacing any text.
for (const row of values.slice(1)) {
  for (const cell of row.slice(1)) {
    if (typeof cell !== "string") continue;

    for (const match of cell.matchAll(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi)) {
      userForEmail(match[0]);
    }

    for (const match of cell.matchAll(/data-mention="@([^"]+)"(?:\s+data-email="([^"]*)")?/gi)) {
      userForDisplay(match[1], match[2] ?? "");
    }

    for (const match of cell.matchAll(/\b([A-Za-z][A-Za-z0-9._-]{2,24}\d{1,3})\s+-\s+([A-ZĐ][A-Za-zÀ-ỹĐđ]*(?:\s+[A-ZĐ][A-Za-zÀ-ỹĐđ]*){1,5})/g)) {
      const username = match[1];
      const user = usernameToUser.get(username.toLowerCase()) ?? createUser(`account:${username.toLowerCase()}`);
      usernameToUser.set(username.toLowerCase(), user);
      displayToUser.set(match[2].toLowerCase(), user);
    }

    for (const match of cell.matchAll(/\b[A-Za-z][A-Za-z0-9._-]{2,24}\d{1,3}\b/g)) {
      if (!isLikelyUserToken(match[0])) continue;
      const key = match[0].toLowerCase();
      if (!usernameToUser.has(key)) usernameToUser.set(key, createUser(`account:${key}`));
    }
  }
}

const systems = [
  { label: "System_01", aliases: ["HRMS/TMS System", "HRMS/TMS", "HRMS", "TMS", "Timesheet"] },
  { label: "System_02", aliases: ["Dashboard"] },
  { label: "System_03", aliases: ["CRM System", "CRM"] },
  { label: "System_04", aliases: ["Jira/Wiki", "Jira", "Wiki"] },
  { label: "System_05", aliases: ["E-Sign"] },
  { label: "System_06", aliases: ["CNOW System", "CNOW"] },
  { label: "System_07", aliases: ["RTS"] },
  { label: "System_08", aliases: ["GAMS System", "GAMS"] },
  { label: "System_09", aliases: ["C-Ticket"] },
  { label: "System_10", aliases: ["POA System", "POA"] },
  { label: "System_11", aliases: ["Websites"] },
  { label: "System_12", aliases: ["EC"] },
  { label: "System_13", aliases: ["C-CodeX"] },
  { label: "System_14", aliases: ["LMS"] },
  { label: "System_15", aliases: ["C-HUB", "CHUB"] },
  { label: "System_16", aliases: ["OMS System", "OMS"] },
  { label: "System_17", aliases: ["sf4c"] },
];

const escapedAliases = systems
  .flatMap(({ label, aliases }) => aliases.map((alias) => ({ label, alias })))
  .sort((a, b) => b.alias.length - a.alias.length)
  .map(({ label, alias }) => ({
    label,
    pattern: new RegExp(`(?<![A-Za-z0-9])${alias.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}(?![A-Za-z0-9])`, "gi"),
  }));

function redactReference(code) {
  const key = code.toUpperCase();
  if (!referenceMap.has(key)) {
    referenceMap.set(key, `REF-${String(nextReference++).padStart(4, "0")}`);
  }
  return referenceMap.get(key);
}

function sanitizeText(source) {
  let text = source;

  text = text.replace(/https?:\/\/[^\s"'<>]+/gi, "https://example.invalid/redacted-resource");
  text = text.replace(/\b(?:\d{1,3}\.){3}\d{1,3}\b/g, "10.0.0.1");

  text = text.replace(/<span\b([^>]*\bclass="[^"]*\bmention\b[^"]*"[^>]*)>([\s\S]*?)<\/span>/gi, (whole, attrs) => {
    const email = attrs.match(/data-email="([^"]*)"/i)?.[1] ?? "";
    const display = attrs.match(/data-mention="@([^"]+)"/i)?.[1] ?? whole.replace(/<[^>]+>/g, "");
    const user = userForDisplay(display, email);
    return `<span class="mention" data-mention="@${user}" data-email="${user.toLowerCase()}@example.invalid">@${user}</span>`;
  });

  text = text.replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi, (email) => {
    const user = userForEmail(email);
    return `${user.toLowerCase()}@example.invalid`;
  });

  for (const [display, user] of [...displayToUser.entries()].sort((a, b) => b[0].length - a[0].length)) {
    if (display.length < 4) continue;
    text = text.replace(new RegExp(display.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "gi"), user);
  }

  for (const [username, user] of [...usernameToUser.entries()].sort((a, b) => b[0].length - a[0].length)) {
    text = text.replace(new RegExp(`(?<![A-Za-z0-9])${username.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}(?![A-Za-z0-9])`, "gi"), user);
  }

  text = text.replace(/\b[A-Za-z][A-Za-z0-9._-]{2,24}\d{1,3}\b/g, (token) => {
    if (!isLikelyUserToken(token)) return token;
    const key = token.toLowerCase();
    const user = usernameToUser.get(key) ?? createUser(`account:${key}`);
    usernameToUser.set(key, user);
    return user;
  });

  for (const { label, pattern } of escapedAliases) text = text.replace(pattern, label);

  text = text
    .replace(/\bCMC\s*-?\s*(?:Global|APAC|Japan|G)?\b/gi, "Organization")
    .replace(/\bCMCGlobal\b/gi, "Organization")
    .replace(/\bcmcglobal\.(?:vn|com\.vn)\b/gi, "example.invalid")
    .replace(/\bAMS\b/gi, "Support Team")
    .replace(/\b(?:KRMO|VNMO|APMO|JPMO|THABU\d*|SBU\d*|DU\d+(?:\.\d+)?|DX\d+|TSAP\d*|BKR\d*|ESC\d*|AIX|HNO|JPO|G\d+O)\b/gi, "Unit")
    .replace(/\b[A-Z]{1,4}-\d{3,}\b/g, redactReference);

  return text;
}

const sanitized = values.map((row, rowIndex) => row.map((value, colIndex) => {
  if (rowIndex === 0) return value;
  if (colIndex === 0 && typeof value === "number") {
    return `TKT-${String(rowIndex).padStart(4, "0")}`;
  }
  return typeof value === "string" ? sanitizeText(value) : value;
}));

used.values = sanitized;
workbook.recalculate();

const keyCheck = await workbook.inspect({
  kind: "table",
  sheetId: "Tickets",
  range: "A1:E12",
  include: "values,formulas",
  tableMaxRows: 12,
  tableMaxCols: 5,
  maxChars: 12000,
});
console.log("=== SANITIZED_SAMPLE ===");
console.log(keyCheck.ndjson);

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!",
  options: { useRegex: true, maxResults: 300 },
  summary: "final formula error scan",
});
console.log("=== FORMULA_ERRORS ===");
console.log(errors.ndjson);

await fs.mkdir(outputDir, { recursive: true });
const preview = await workbook.render({ sheetName: "Tickets", range: "A1:E20", scale: 1.2, format: "png" });
await fs.writeFile(previewPath, new Uint8Array(await preview.arrayBuffer()));

const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);

const verification = await SpreadsheetFile.importXlsx(await FileBlob.load(outputPath));
const verifySheet = verification.worksheets.getItem("Tickets");
console.log("=== OUTPUT ===");
console.log(JSON.stringify({
  outputPath,
  usedRange: verifySheet.getUsedRange().address,
  userCount: userByKey.size,
  referenceCount: referenceMap.size,
  sheetCount: verification.worksheets.items.length,
  tableCount: verifySheet.tables.items.length,
}));
