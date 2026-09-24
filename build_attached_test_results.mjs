import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const inputPath = "D:/Down/tickets_r1_r5_with_comments.xlsx";
const resultJsonPath = "./tmp/attached_workbook_result.json";
const outputDir = "./outputs/01a0c92d-a1f3-7d63-914f-a7816c5219f2";
const outputPath = `${outputDir}/tickets_r1_r5_test_results.xlsx`;
const fontFamily = "Arial";
const headerFill = "#1F4E78";
const lightBlue = "#D9EAF7";
const lightGray = "#F2F2F2";
const borderColor = "#D9E2F3";

const inputWorkbook = await SpreadsheetFile.importXlsx(await FileBlob.load(inputPath));
const inputSheet = inputWorkbook.worksheets.getItemAt(0);
const inputValues = inputSheet.getRange("A1:E501").values;
const execution = JSON.parse(await fs.readFile(resultJsonPath, "utf8"));

const rowNumber = (externalId) => Number(String(externalId).split(":").at(-1));
const ticketByRow = new Map(execution.Tickets.map((ticket) => [rowNumber(ticket.ExternalId), ticket]));
const rowByTicketId = new Map(execution.Tickets.map((ticket) => [ticket.Id, rowNumber(ticket.ExternalId)]));
const reviewBySolutionId = new Map(execution.Reviews.map((review) => [review.SolutionId, review]));

const workbook = Workbook.create();
const summary = workbook.worksheets.add("Summary");
const ticketSheet = workbook.worksheets.add("Ticket Results");
const solutionSheet = workbook.worksheets.add("Draft Solutions");
const clusterSheet = workbook.worksheets.add("Clusters");

for (const sheet of workbook.worksheets.items) {
  sheet.showGridLines = false;
  sheet.getRange("A1:Z600").format.font = { name: fontFamily, size: 10, color: "#1F2937" };
}

function titleSheet(sheet, title, context, lastColumn) {
  sheet.getRange("A2").values = [[title]];
  sheet.getRange("A2").format.font = { name: fontFamily, size: 15, bold: true, color: "#17365D" };
  sheet.getRange("A3").values = [[context]];
  sheet.getRange("A3").format.font = { name: fontFamily, size: 10, italic: true, color: "#5B6573" };
  sheet.getRange(`A3:${lastColumn}3`).format.borders = {
    bottom: { style: "thin", color: "#8EA9C1" },
  };
}

function styleHeader(range) {
  range.format = {
    fill: headerFill,
    font: { name: fontFamily, size: 10, bold: true, color: "#FFFFFF" },
    horizontalAlignment: "center",
    verticalAlignment: "center",
    wrapText: true,
    borders: { preset: "inside", style: "thin", color: "#FFFFFF" },
  };
  range.format.rowHeight = 30;
}

function styleTableBody(range) {
  range.format.verticalAlignment = "top";
  range.format.borders = {
    insideHorizontal: { style: "thin", color: borderColor },
    bottom: { style: "thin", color: borderColor },
  };
}

titleSheet(
  summary,
  "Kết quả chạy bộ testcase tickets_r1_r5_with_comments.xlsx",
  "Phạm vi: chỉ 500 ticket trong workbook đính kèm; không nhập dữ liệu Jira/mock hoặc bộ testcase khác.",
  "H",
);
summary.tabColor = "#1F4E78";

summary.getRange("A5:B5").values = [["Chỉ số chạy", "Kết quả"]];
summary.getRange("A6:B17").values = [
  ["Received", execution.Run.Received],
  ["Imported", execution.Run.Imported],
  ["Idempotent skipped", execution.Run.IdempotentSkipped],
  ["Blocked", execution.Run.Blocked],
  ["Search only", execution.Run.SearchOnly],
  ["Duplicates", execution.Run.Duplicates],
  ["Auto linked", execution.Run.AutoLinked],
  ["Candidate eligible", execution.Run.CandidateEligible],
  ["Drafts created", execution.Run.DraftsCreated],
  ["Domain reviews", execution.Run.DomainReviewsCreated],
  ["Manager/SME reviews", execution.Run.ManagerReviewsCreated],
  ["Clusters", execution.Clusters.length],
];
styleHeader(summary.getRange("A5:B5"));
styleTableBody(summary.getRange("A6:B17"));
summary.getRange("B6:B17").format.numberFormat = "#,##0";

const decisionCounts = Object.entries(execution.Tickets.reduce((counts, ticket) => {
  counts[ticket.Decision] = (counts[ticket.Decision] ?? 0) + 1;
  return counts;
}, {})).sort((a, b) => b[1] - a[1]);
summary.getRange("D5:E5").values = [["Quyết định", "Số ticket"]];
summary.getRange(`D6:E${5 + decisionCounts.length}`).values = decisionCounts;
styleHeader(summary.getRange("D5:E5"));
styleTableBody(summary.getRange(`D6:E${5 + decisionCounts.length}`));
summary.getRange(`E6:E${5 + decisionCounts.length}`).format.numberFormat = "#,##0";

const riskCounts = Object.entries(execution.Tickets.reduce((counts, ticket) => {
  counts[ticket.Risk] = (counts[ticket.Risk] ?? 0) + 1;
  return counts;
}, {})).sort((a, b) => b[1] - a[1]);
summary.getRange("G5:H5").values = [["Mức rủi ro", "Số ticket"]];
summary.getRange(`G6:H${5 + riskCounts.length}`).values = riskCounts;
styleHeader(summary.getRange("G5:H5"));
styleTableBody(summary.getRange(`G6:H${5 + riskCounts.length}`));
summary.getRange(`H6:H${5 + riskCounts.length}`).format.numberFormat = "#,##0";

const categoryCounts = Object.entries(execution.Tickets.reduce((counts, ticket) => {
  counts[ticket.Category] = (counts[ticket.Category] ?? 0) + 1;
  return counts;
}, {})).sort((a, b) => b[1] - a[1]);
summary.getRange("D13:E13").values = [["Category", "Số ticket"]];
summary.getRange(`D14:E${13 + categoryCounts.length}`).values = categoryCounts;
styleHeader(summary.getRange("D13:E13"));
styleTableBody(summary.getRange(`D14:E${13 + categoryCounts.length}`));
summary.getRange(`E14:E${13 + categoryCounts.length}`).format.numberFormat = "#,##0";

summary.getRange("A20:H20").values = [["Ghi chú đánh giá", null, null, null, null, null, null, null]];
summary.getRange("A20:H20").format = {
  fill: lightBlue,
  font: { name: fontFamily, size: 10, bold: true, color: "#17365D" },
  borders: { preset: "outside", style: "thin", color: "#8EA9C1" },
};
summary.getRange("A21:H23").values = [
  ["Ticket Results", "Đối chiếu dữ liệu gốc với nội dung đã làm sạch, quality score, risk, decision và lý do.", null, null, null, null, null, null],
  ["Draft Solutions", "24 bản nháp do pipeline tạo; tất cả vẫn ở trạng thái InReview/Pending.", null, null, null, null, null, null],
  ["Clusters", "100 cụm được tạo từ chính bộ dữ liệu này; cột Promoted cho biết cụm đã sinh bản nháp.", null, null, null, null, null, null],
];
summary.mergeCells("B21:H21");
summary.mergeCells("B22:H22");
summary.mergeCells("B23:H23");
summary.getRange("A21:A23").format.font = { name: fontFamily, size: 10, bold: true, color: "#17365D" };
summary.getRange("B21:H23").format.wrapText = true;
summary.getRange("A21:H23").format.verticalAlignment = "top";
summary.getRange("A5:H23").format.borders = { bottom: { style: "thin", color: borderColor } };
summary.getRange("A:A").format.columnWidth = 24;
summary.getRange("B:B").format.columnWidth = 18;
summary.getRange("C:C").format.columnWidth = 3;
summary.getRange("D:D").format.columnWidth = 24;
summary.getRange("E:E").format.columnWidth = 14;
summary.getRange("F:F").format.columnWidth = 3;
summary.getRange("G:G").format.columnWidth = 20;
summary.getRange("H:H").format.columnWidth = 14;
summary.getRange("B21:H23").format.rowHeight = 42;

titleSheet(ticketSheet, "Kết quả theo từng ticket", "Dữ liệu gốc và kết quả xử lý của 500 dòng testcase.", "T");
ticketSheet.tabColor = "#4472C4";
const ticketHeaders = [
  "Source row", "internal_ticket_id", "app", "summary", "description", "original comment",
  "clean title", "clean description", "clean resolution", "category", "subcategory", "quality score",
  "risk", "decision", "decision reason", "ticket id", "cluster id", "duplicate of source row",
  "linked solution id", "match confidence",
];
const ticketRows = [];
for (let sourceRow = 2; sourceRow <= 501; sourceRow += 1) {
  const original = inputValues[sourceRow - 1] ?? [];
  const ticket = ticketByRow.get(sourceRow);
  if (!ticket) throw new Error(`Missing pipeline result for source row ${sourceRow}`);
  ticketRows.push([
    sourceRow,
    original[0] ?? null,
    original[1] ?? "",
    original[2] ?? "",
    original[4] ?? "",
    original[3] ?? "",
    ticket.CleanTitle,
    ticket.CleanDescription,
    ticket.CleanResolution,
    ticket.Category,
    ticket.Subcategory,
    ticket.QualityScore,
    ticket.Risk,
    ticket.Decision,
    ticket.DecisionReason,
    ticket.Id,
    ticket.ClusterId ?? "",
    ticket.DuplicateOfTicketId ? rowByTicketId.get(ticket.DuplicateOfTicketId) ?? "" : "",
    ticket.LinkedSolutionId ?? "",
    ticket.MatchConfidence,
  ]);
}
ticketSheet.getRange("A4:T4").values = [ticketHeaders];
ticketSheet.getRange("A5").write(ticketRows);
styleHeader(ticketSheet.getRange("A4:T4"));
styleTableBody(ticketSheet.getRange("A5:T504"));
ticketSheet.getRange("A5:T504").format.rowHeight = 30;
ticketSheet.getRange("A5:B504").format.numberFormat = "#,##0";
ticketSheet.getRange("L5:L504").format.numberFormat = "0";
ticketSheet.getRange("T5:T504").format.numberFormat = "0.0%";
ticketSheet.getRange("A4:T504").format.verticalAlignment = "top";
ticketSheet.getRange("D5:I504").format.wrapText = false;
ticketSheet.getRange("A:A").format.columnWidth = 11;
ticketSheet.getRange("B:B").format.columnWidth = 16;
ticketSheet.getRange("C:C").format.columnWidth = 20;
ticketSheet.getRange("D:D").format.columnWidth = 38;
ticketSheet.getRange("E:E").format.columnWidth = 25;
ticketSheet.getRange("F:F").format.columnWidth = 42;
ticketSheet.getRange("G:G").format.columnWidth = 38;
ticketSheet.getRange("H:H").format.columnWidth = 25;
ticketSheet.getRange("I:I").format.columnWidth = 42;
ticketSheet.getRange("J:K").format.columnWidth = 17;
ticketSheet.getRange("L:N").format.columnWidth = 16;
ticketSheet.getRange("O:O").format.columnWidth = 38;
ticketSheet.getRange("P:Q").format.columnWidth = 36;
ticketSheet.getRange("R:R").format.columnWidth = 20;
ticketSheet.getRange("S:S").format.columnWidth = 36;
ticketSheet.getRange("T:T").format.columnWidth = 16;
ticketSheet.freezePanes.freezeRows(4);
ticketSheet.freezePanes.freezeColumns(3);
const ticketTable = ticketSheet.tables.add("A4:T504", true, "TicketResultsTable");
ticketTable.style = "TableStyleMedium2";
ticketSheet.getRange("M5:M504").conditionalFormats.add("containsText", { text: "High", format: { fill: "#FCE8E6", font: { color: "#B91C1C", bold: true } } });
ticketSheet.getRange("N5:N504").conditionalFormats.add("containsText", { text: "SearchOnly", format: { fill: "#FFF2CC", font: { color: "#7F6000" } } });
ticketSheet.getRange("N5:N504").conditionalFormats.add("containsText", { text: "Duplicate", format: { fill: "#EDE9FE", font: { color: "#5B21B6" } } });

titleSheet(solutionSheet, "Bản nháp tri thức", "Các solution draft được tạo từ bộ testcase; chưa có quyết định duyệt của con người.", "M");
solutionSheet.tabColor = "#70AD47";
const solutionHeaders = [
  "solution id", "cluster id", "title", "problem", "procedure", "applicability", "warning",
  "risk", "status", "source ticket count", "required role", "review decision", "review due at",
];
const solutionRows = execution.Solutions.map((solution) => {
  const review = reviewBySolutionId.get(solution.Id);
  return [
    solution.Id, solution.ClusterId, solution.Title, solution.Problem, solution.Procedure,
    solution.Applicability, solution.Warning, solution.Risk, solution.Status, solution.SourceTicketCount,
    review?.RequiredRole ?? "", review?.Decision ?? "", new Date(solution.ReviewDueAt),
  ];
});
solutionSheet.getRange("A4:M4").values = [solutionHeaders];
solutionSheet.getRange("A5").write(solutionRows);
styleHeader(solutionSheet.getRange("A4:M4"));
styleTableBody(solutionSheet.getRange(`A5:M${4 + solutionRows.length}`));
solutionSheet.getRange(`J5:J${4 + solutionRows.length}`).format.numberFormat = "#,##0";
solutionSheet.getRange(`M5:M${4 + solutionRows.length}`).format.numberFormat = "yyyy-mm-dd";
solutionSheet.getRange(`C5:G${4 + solutionRows.length}`).format.wrapText = true;
solutionSheet.getRange(`A5:M${4 + solutionRows.length}`).format.verticalAlignment = "top";
solutionSheet.getRange("A:B").format.columnWidth = 36;
solutionSheet.getRange("C:C").format.columnWidth = 24;
solutionSheet.getRange("D:D").format.columnWidth = 34;
solutionSheet.getRange("E:E").format.columnWidth = 52;
solutionSheet.getRange("F:G").format.columnWidth = 34;
solutionSheet.getRange("H:I").format.columnWidth = 14;
solutionSheet.getRange("J:J").format.columnWidth = 18;
solutionSheet.getRange("K:L").format.columnWidth = 20;
solutionSheet.getRange("M:M").format.columnWidth = 16;
solutionSheet.freezePanes.freezeRows(4);
solutionSheet.freezePanes.freezeColumns(2);
const solutionTable = solutionSheet.tables.add(`A4:M${4 + solutionRows.length}`, true, "DraftSolutionsTable");
solutionTable.style = "TableStyleMedium4";
solutionSheet.getRange(`H5:H${4 + solutionRows.length}`).conditionalFormats.add("containsText", { text: "High", format: { fill: "#FCE8E6", font: { color: "#B91C1C", bold: true } } });

titleSheet(clusterSheet, "Cụm ticket", "Các cụm được hình thành trong lần chạy từ chính 500 ticket đính kèm.", "K");
clusterSheet.tabColor = "#A5A5A5";
const clusterHeaders = [
  "cluster id", "name", "workspace", "category", "ticket count", "average quality",
  "knowledge value", "risk", "promoted", "promotion reason", "solution id",
];
const clusterRows = execution.Clusters.map((cluster) => [
  cluster.Id, cluster.Name, cluster.Workspace, cluster.Category, cluster.TicketIds.length,
  cluster.AverageQuality, cluster.KnowledgeValue, cluster.Risk, cluster.Promoted,
  cluster.PromotionReason, cluster.SolutionId ?? "",
]);
clusterRows.sort((a, b) => Number(b[8]) - Number(a[8]) || b[6] - a[6]);
clusterSheet.getRange("A4:K4").values = [clusterHeaders];
clusterSheet.getRange("A5").write(clusterRows);
styleHeader(clusterSheet.getRange("A4:K4"));
styleTableBody(clusterSheet.getRange(`A5:K${4 + clusterRows.length}`));
clusterSheet.getRange(`E5:E${4 + clusterRows.length}`).format.numberFormat = "#,##0";
clusterSheet.getRange(`F5:G${4 + clusterRows.length}`).format.numberFormat = "0.0";
clusterSheet.getRange("A:A").format.columnWidth = 36;
clusterSheet.getRange("B:B").format.columnWidth = 24;
clusterSheet.getRange("C:D").format.columnWidth = 18;
clusterSheet.getRange("E:I").format.columnWidth = 17;
clusterSheet.getRange("J:J").format.columnWidth = 38;
clusterSheet.getRange("K:K").format.columnWidth = 36;
clusterSheet.freezePanes.freezeRows(4);
clusterSheet.freezePanes.freezeColumns(2);
const clusterTable = clusterSheet.tables.add(`A4:K${4 + clusterRows.length}`, true, "ClustersTable");
clusterTable.style = "TableStyleMedium15";

workbook.recalculate();
await fs.mkdir(outputDir, { recursive: true });

const checks = [];
for (const [sheetName, range] of [
  ["Summary", "A1:H23"],
  ["Ticket Results", "A1:T14"],
  ["Draft Solutions", "A1:M14"],
  ["Clusters", "A1:K14"],
]) {
  const inspection = await workbook.inspect({
    kind: "table",
    sheetId: sheetName,
    range,
    include: "values,formulas",
    tableMaxRows: 14,
    tableMaxCols: 20,
    maxChars: 12000,
  });
  checks.push(inspection.ndjson);
  const preview = await workbook.render({ sheetName, range, scale: 1.2, format: "png" });
  const safeName = sheetName.toLowerCase().replaceAll(" ", "_");
  await fs.writeFile(`${outputDir}/${safeName}_preview.png`, new Uint8Array(await preview.arrayBuffer()));
}

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!",
  options: { useRegex: true, maxResults: 300 },
  summary: "final formula error scan",
});

await fs.writeFile(`${outputDir}/inspection.ndjson`, `${checks.join("\n")}\n${errors.ndjson}\n`, "utf8");
const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);
console.log(JSON.stringify({ outputPath, ticketRows: ticketRows.length, solutionRows: solutionRows.length, clusterRows: clusterRows.length, errorScan: errors.ndjson }));
