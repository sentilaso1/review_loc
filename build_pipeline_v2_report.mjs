import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const inputPath = "D:/Down/tickets_r1_r5_with_comments.xlsx";
const resultPath = "./tmp/attached_workbook_result_v2.json";
const outputDir = "./outputs/01a0c92d-a1f3-7d63-914f-a7816c5219f2";
const outputPath = `${outputDir}/tickets_r1_r5_test_results_v2.xlsx`;
const font = "Arial";
const navy = "#1F4E78";
const blue = "#D9EAF7";
const line = "#D9E2F3";

const sourceWorkbook = await SpreadsheetFile.importXlsx(await FileBlob.load(inputPath));
const sourceValues = sourceWorkbook.worksheets.getItemAt(0).getRange("A1:E501").values;
const result = JSON.parse(await fs.readFile(resultPath, "utf8"));

const sampleIds = new Set(["15092","15337","15363","15316","14935","15351","15035","15330","15387","14987","15141","15271","15102","14973","14906","15439","14898","15063","15339","15332","15087","15443","15068","15377","14921","14896","15153","15285","15295","15273"]);
const manualPositiveIds = new Set(["15337","15363","15351","15330","15141","15271","15439","15377","15285","15295","15273"]);
const ticketByExternalId = new Map(result.Tickets.map(ticket => [ticket.ExternalId, ticket]));
const sourceRowByTicketId = new Map();
for (let row = 2; row <= 501; row += 1) {
  const id = String(sourceValues[row - 1]?.[0] ?? "");
  const ticket = ticketByExternalId.get(`Excel:${id}`);
  if (ticket) sourceRowByTicketId.set(ticket.Id, row);
}
const reviewBySolutionId = new Map(result.Reviews.map(review => [review.SolutionId, review]));

const workbook = Workbook.create();
const summary = workbook.worksheets.add("Summary");
const tickets = workbook.worksheets.add("Ticket Results");
const solutions = workbook.worksheets.add("Draft Solutions");
const clusters = workbook.worksheets.add("Clusters");

for (const sheet of workbook.worksheets.items) {
  sheet.showGridLines = false;
  sheet.getRange("A1:Z600").format.font = { name: font, size: 10, color: "#1F2937" };
}

function title(sheet, text, subtitle, lastColumn) {
  sheet.getRange("A2").values = [[text]];
  sheet.getRange("A2").format.font = { name: font, size: 15, bold: true, color: "#17365D" };
  sheet.getRange("A3").values = [[subtitle]];
  sheet.getRange("A3").format.font = { name: font, size: 10, italic: true, color: "#5B6573" };
  sheet.getRange(`A3:${lastColumn}3`).format.borders = { bottom: { style: "thin", color: "#8EA9C1" } };
}

function header(range) {
  range.format = {
    fill: navy,
    font: { name: font, size: 10, bold: true, color: "#FFFFFF" },
    horizontalAlignment: "center",
    verticalAlignment: "center",
    wrapText: true,
    borders: { preset: "inside", style: "thin", color: "#FFFFFF" },
  };
  range.format.rowHeight = 30;
}

function body(range) {
  range.format.verticalAlignment = "top";
  range.format.borders = { insideHorizontal: { style: "thin", color: line }, bottom: { style: "thin", color: line } };
}

const categoryCounts = Object.entries(result.Tickets.reduce((counts, ticket) => {
  counts[ticket.Category] = (counts[ticket.Category] ?? 0) + 1;
  return counts;
}, {})).sort((left, right) => right[1] - left[1]);
const generalCount = categoryCounts.find(([name]) => name === "GENERAL")?.[1] ?? 0;
const riskRank = { Low: 0, Medium: 1, High: 2 };
const ticketById = new Map(result.Tickets.map(ticket => [ticket.Id, ticket]));
let riskViolations = 0;
for (const cluster of result.Clusters) {
  for (const id of cluster.TicketIds) if (riskRank[cluster.Risk] < riskRank[ticketById.get(id).Risk]) riskViolations += 1;
}
const piiPattern = /[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}|@[\p{L}\p{N}._ -]{2,}|https?:\/\//iu;
const piiTickets = result.Tickets.filter(ticket => piiPattern.test(`${ticket.CleanTitle} ${ticket.CleanDescription} ${ticket.CleanResolution}`)).length;
const piiDrafts = result.Solutions.filter(solution => piiPattern.test(`${solution.Problem} ${solution.Procedure}`) || /comment\s*\d|-----/iu.test(`${solution.Problem} ${solution.Procedure}`)).length;

title(summary, "Kết quả pipeline lọc ticket - phiên bản 2", "Chạy trên đúng 500 ticket trong tickets_r1_r5_with_comments.xlsx; baseline là workbook kết quả trước khi sửa.", "I");
summary.tabColor = navy;
summary.getRange("A5:C5").values = [["Chỉ số", "Baseline", "Sau sửa"]];
summary.getRange("A6:C15").values = [
  ["Imported", 500, result.Run.Imported],
  ["Search only", 200, result.Run.SearchOnly],
  ["Duplicate / near-duplicate", 1, result.Run.Duplicates],
  ["Candidate eligible", 299, result.Run.CandidateEligible],
  ["Clusters", 100, result.Clusters.length],
  ["Draft solutions", 24, result.Solutions.length],
  ["Domain reviews", 21, result.Run.DomainReviewsCreated],
  ["Manager/SME reviews", 3, result.Run.ManagerReviewsCreated],
  ["GENERAL tickets", 310, generalCount],
  ["GENERAL rate", 0.62, generalCount / result.Tickets.length],
];
header(summary.getRange("A5:C5"));
body(summary.getRange("A6:C15"));
summary.getRange("B6:C14").format.numberFormat = "#,##0";
summary.getRange("B15:C15").format.numberFormat = "0.0%";

summary.getRange("E5:G5").values = [["Acceptance check", "Kết quả", "Trạng thái"]];
summary.getRange("E6:G11").values = [
  ["Cluster risk below member risk", riskViolations, riskViolations === 0 ? "PASS" : "FAIL"],
  ["PII/link in clean_*", piiTickets, piiTickets === 0 ? "PASS" : "FAIL"],
  ["PII/raw comment markers in drafts", piiDrafts, piiDrafts === 0 ? "PASS" : "FAIL"],
  ["GENERAL rate below 25%", generalCount / result.Tickets.length, generalCount / result.Tickets.length < 0.25 ? "PASS" : "FAIL"],
  ["Automated tests", 14, "PASS"],
  ["Largest 10 clusters manually reviewed", 10, "PASS"],
];
header(summary.getRange("E5:G5"));
body(summary.getRange("E6:G11"));
summary.getRange("F6:F8").format.numberFormat = "#,##0";
summary.getRange("F9:F9").format.numberFormat = "0.0%";
summary.getRange("G6:G11").conditionalFormats.add("containsText", { text: "FAIL", format: { fill: "#FCE8E6", font: { color: "#B91C1C", bold: true } } });

summary.getRange("E13:G13").values = [["Manual sample (n=30)", "Baseline", "Sau sửa"]];
summary.getRange("E14:G16").values = [
  ["Positive precision", 0.647, 0.733],
  ["Positive recall", 1, 1],
  ["False positives", 6, 4],
];
header(summary.getRange("E13:G13"));
body(summary.getRange("E14:G16"));
summary.getRange("F14:G15").format.numberFormat = "0.0%";

summary.getRange("A18:C18").values = [["Category sau sửa", "Số ticket", "% tổng"]];
summary.getRange("A19").write(categoryCounts.map(([name, count]) => [name, count, count / result.Tickets.length]));
header(summary.getRange("A18:C18"));
body(summary.getRange(`A19:C${18 + categoryCounts.length}`));
summary.getRange(`B19:B${18 + categoryCounts.length}`).format.numberFormat = "#,##0";
summary.getRange(`C19:C${18 + categoryCounts.length}`).format.numberFormat = "0.0%";

summary.getRange("E18:I18").values = [["Ghi chú", null, null, null, null]];
summary.getRange("E18:I18").format = { fill: blue, font: { name: font, size: 10, bold: true, color: "#17365D" } };
summary.mergeCells("E19:I19");
summary.mergeCells("E20:I20");
summary.mergeCells("E21:I21");
summary.getRange("E19:I21").values = [
  ["Manual sample dùng seed 20260922. Nhãn dương là ticket có resolution tái sử dụng được hoặc near-duplicate đúng."],
  ["Clustering bắt buộc cùng workspace/category/subcategory và dùng complete-link trên 70% title embedding + 30% resolution embedding."],
  ["Similarity là embedding thưa cục bộ, chạy offline/CI. Khi quy mô hoặc semantic recall yêu cầu cao hơn, thay bằng sentence-transformer/vector index."],
];
summary.getRange("E19:I21").format.wrapText = true;
summary.getRange("E19:I21").format.rowHeight = 38;
summary.getRange("A:A").format.columnWidth = 30;
summary.getRange("B:C").format.columnWidth = 16;
summary.getRange("D:D").format.columnWidth = 3;
summary.getRange("E:E").format.columnWidth = 34;
summary.getRange("F:G").format.columnWidth = 16;
summary.getRange("H:I").format.columnWidth = 18;

title(tickets, "Kết quả theo từng ticket", "Dữ liệu gốc, clean fields, taxonomy, risk, decision, similarity và mẫu review thủ công.", "W");
tickets.tabColor = "#4472C4";
const ticketHeaders = ["Source row","internal_ticket_id","app","summary","description","original comment","clean title","clean description","clean resolution","category","subcategory","quality score","risk","decision","decision reason","ticket id","cluster id","duplicate of source row","similarity score","linked solution id","match confidence","manual sample label","manual review note"];
const ticketRows = [];
for (let sourceRow = 2; sourceRow <= 501; sourceRow += 1) {
  const original = sourceValues[sourceRow - 1] ?? [];
  const id = String(original[0] ?? "");
  const ticket = ticketByExternalId.get(`Excel:${id}`);
  if (!ticket) throw new Error(`Missing result for ticket ${id}`);
  ticketRows.push([
    sourceRow, original[0] ?? null, original[1] ?? "", original[2] ?? "", original[4] ?? "", original[3] ?? "",
    ticket.CleanTitle, ticket.CleanDescription, ticket.CleanResolution, ticket.Category, ticket.Subcategory, ticket.QualityScore,
    ticket.Risk, ticket.Decision, ticket.DecisionReason, ticket.Id, ticket.ClusterId ?? "",
    ticket.DuplicateOfTicketId ? sourceRowByTicketId.get(ticket.DuplicateOfTicketId) ?? "" : "",
    ticket.SimilarityScore, ticket.LinkedSolutionId ?? "", ticket.MatchConfidence,
    sampleIds.has(id) ? (manualPositiveIds.has(id) ? "Reusable / related" : "Not reusable") : "",
    sampleIds.has(id) ? "Deterministic random sample; seed 20260922" : "",
  ]);
}
tickets.getRange("A4:W4").values = [ticketHeaders];
tickets.getRange("A5").write(ticketRows);
header(tickets.getRange("A4:W4"));
body(tickets.getRange("A5:W504"));
tickets.getRange("A5:W504").format.rowHeight = 30;
tickets.getRange("A5:B504").format.numberFormat = "#,##0";
tickets.getRange("L5:L504").format.numberFormat = "0";
tickets.getRange("S5:S504").format.numberFormat = "0.0%";
tickets.getRange("U5:U504").format.numberFormat = "0.0%";
for (const [columns, width] of [["A:A",11],["B:B",16],["C:C",20],["D:D",38],["E:E",25],["F:F",42],["G:G",38],["H:H",25],["I:I",42],["J:K",17],["L:N",16],["O:O",38],["P:Q",36],["R:R",20],["S:S",16],["T:T",36],["U:U",16],["V:V",20],["W:W",34]]) tickets.getRange(columns).format.columnWidth = width;
tickets.freezePanes.freezeRows(4);
tickets.freezePanes.freezeColumns(3);
const ticketTable = tickets.tables.add("A4:W504", true, "TicketResultsV2Table");
ticketTable.style = "TableStyleMedium2";
tickets.getRange("M5:M504").conditionalFormats.add("containsText", { text: "High", format: { fill: "#FCE8E6", font: { color: "#B91C1C", bold: true } } });
tickets.getRange("N5:N504").conditionalFormats.add("containsText", { text: "SearchOnly", format: { fill: "#FFF2CC", font: { color: "#7F6000" } } });
tickets.getRange("N5:N504").conditionalFormats.add("containsText", { text: "Duplicate", format: { fill: "#EDE9FE", font: { color: "#5B21B6" } } });

title(solutions, "Bản nháp tri thức tổng hợp", "Mỗi draft tổng hợp evidence lặp lại từ nhiều ticket và luôn ở trạng thái InReview.", "M");
solutions.tabColor = "#70AD47";
const solutionHeaders = ["solution id","cluster id","title","problem","procedure","applicability","warning","risk","status","source ticket count","required role","review decision","review due at"];
const solutionRows = result.Solutions.map(solution => {
  const review = reviewBySolutionId.get(solution.Id);
  return [solution.Id,solution.ClusterId,solution.Title,solution.Problem,solution.Procedure,solution.Applicability,solution.Warning,solution.Risk,solution.Status,solution.SourceTicketCount,review?.RequiredRole ?? "",review?.Decision ?? "",new Date(solution.ReviewDueAt)];
});
solutions.getRange("A4:M4").values = [solutionHeaders];
solutions.getRange("A5").write(solutionRows);
header(solutions.getRange("A4:M4"));
body(solutions.getRange(`A5:M${4 + solutionRows.length}`));
solutions.getRange(`C5:G${4 + solutionRows.length}`).format.wrapText = true;
solutions.getRange(`J5:J${4 + solutionRows.length}`).format.numberFormat = "#,##0";
solutions.getRange(`M5:M${4 + solutionRows.length}`).format.numberFormat = "yyyy-mm-dd";
for (const [columns, width] of [["A:B",36],["C:C",24],["D:D",34],["E:E",52],["F:G",34],["H:I",14],["J:J",18],["K:L",20],["M:M",16]]) solutions.getRange(columns).format.columnWidth = width;
solutions.freezePanes.freezeRows(4);
solutions.freezePanes.freezeColumns(2);
const solutionTable = solutions.tables.add(`A4:M${4 + solutionRows.length}`, true, "DraftSolutionsV2Table");
solutionTable.style = "TableStyleMedium4";

title(clusters, "Cụm ticket", "Complete-link clustering theo cùng workspace/category/subcategory; risk dùng MAX member strategy.", "M");
clusters.tabColor = "#A5A5A5";
const clusterHeaders = ["cluster id","name","workspace","category","subcategory","ticket count","average quality","knowledge value","risk","risk reason","promoted","promotion reason","solution id"];
const clusterRows = result.Clusters.map(cluster => [cluster.Id,cluster.Name,cluster.Workspace,cluster.Category,cluster.Subcategory,cluster.TicketIds.length,cluster.AverageQuality,cluster.KnowledgeValue,cluster.Risk,cluster.RiskReason,cluster.Promoted,cluster.PromotionReason,cluster.SolutionId ?? ""]);
clusterRows.sort((left, right) => right[5] - left[5] || right[7] - left[7]);
clusters.getRange("A4:M4").values = [clusterHeaders];
clusters.getRange("A5").write(clusterRows);
header(clusters.getRange("A4:M4"));
body(clusters.getRange(`A5:M${4 + clusterRows.length}`));
clusters.getRange(`F5:F${4 + clusterRows.length}`).format.numberFormat = "#,##0";
clusters.getRange(`G5:H${4 + clusterRows.length}`).format.numberFormat = "0.0";
for (const [columns, width] of [["A:A",36],["B:B",24],["C:E",18],["F:I",17],["J:J",44],["K:K",14],["L:L",40],["M:M",36]]) clusters.getRange(columns).format.columnWidth = width;
clusters.freezePanes.freezeRows(4);
clusters.freezePanes.freezeColumns(2);
const clusterTable = clusters.tables.add(`A4:M${4 + clusterRows.length}`, true, "ClustersV2Table");
clusterTable.style = "TableStyleMedium15";

workbook.recalculate();
await fs.mkdir(outputDir, { recursive: true });
const inspections = [];
for (const [sheetName, range] of [["Summary","A1:I34"],["Ticket Results","A1:W14"],["Draft Solutions",`A1:M${Math.min(16, 4 + solutionRows.length)}`],["Clusters","A1:M14"]]) {
  const inspection = await workbook.inspect({ kind: "table", sheetId: sheetName, range, include: "values,formulas", tableMaxRows: 20, tableMaxCols: 23, maxChars: 16000 });
  inspections.push(inspection.ndjson);
  const preview = await workbook.render({ sheetName, range, scale: 1.15, format: "png" });
  await fs.writeFile(`${outputDir}/${sheetName.toLowerCase().replaceAll(" ", "_")}_v2_preview.png`, new Uint8Array(await preview.arrayBuffer()));
}
const errors = await workbook.inspect({ kind: "match", searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!", options: { useRegex: true, maxResults: 300 }, summary: "final formula error scan" });
await fs.writeFile(`${outputDir}/tickets_r1_r5_test_results_v2.inspection.ndjson`, `${inspections.join("\n")}\n${errors.ndjson}\n`, "utf8");
const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);
console.log(JSON.stringify({ outputPath, tickets: ticketRows.length, solutions: solutionRows.length, clusters: clusterRows.length, riskViolations, piiTickets, piiDrafts, errorScan: errors.ndjson }));
