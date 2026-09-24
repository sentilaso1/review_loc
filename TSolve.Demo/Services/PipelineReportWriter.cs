using System.Globalization;
using System.IO.Compression;
using System.Xml;
using TSolve.Demo.Models;

namespace TSolve.Demo.Services;

public sealed class PipelineReportWriter
{
    public void Write(Stream output, DemoState state, PipelineRun run)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        WriteText(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet3.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/worksheets/sheet4.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);
        WriteText(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        WriteText(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Summary" sheetId="1" r:id="rId1"/><sheet name="Ticket Results" sheetId="2" r:id="rId2"/><sheet name="Draft Solutions" sheetId="3" r:id="rId3"/><sheet name="Clusters" sheetId="4" r:id="rId4"/></sheets>
            </workbook>
            """);
        WriteText(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
              <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml"/>
              <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet4.xml"/>
            </Relationships>
            """);

        var general = state.Tickets.Count(ticket => ticket.Category == "GENERAL");
        WriteSheet(archive, 1, new object?[][]
        {
            new object?[] { "Metric", "Value" }, new object?[] { "Received", run.Received }, new object?[] { "Imported", run.Imported }, new object?[] { "SearchOnly", run.SearchOnly },
            new object?[] { "Duplicate", run.Duplicates }, new object?[] { "AutoLinked", run.AutoLinked }, new object?[] { "CandidateEligible", run.CandidateEligible },
            new object?[] { "DraftsCreated", run.DraftsCreated }, new object?[] { "Clusters", state.Clusters.Count }, new object?[] { "AIRuns", state.AIRuns.Count },
            new object?[] { "GENERAL tickets", general }, new object?[] { "GENERAL rate", state.Tickets.Count == 0 ? 0 : Math.Round((double)general / state.Tickets.Count, 4) },
            new object?[] { "Queue", run.QueueName }, new object?[] { "CompletedAt", run.CompletedAt }
        });

        var ticketRows = new List<object?[]> { new object?[] { "external_ticket_id", "app", "title", "clean title", "clean description", "clean resolution", "category", "subcategory", "quality score", "risk", "decision", "decision reason", "ticket id", "cluster id", "duplicate of", "linked solution", "match confidence" } };
        ticketRows.AddRange(state.Tickets.OrderBy(ticket => ticket.ExternalId, StringComparer.Ordinal).Select(ticket => new object?[]
        {
            ticket.ExternalId, ticket.Application, ticket.Title, ticket.CleanTitle, ticket.CleanDescription, ticket.CleanResolution,
            ticket.Category, ticket.Subcategory, ticket.QualityScore, ticket.Risk, ticket.Decision, ticket.DecisionReason,
            ticket.Id, ticket.ClusterId, ticket.DuplicateOfTicketId, ticket.LinkedSolutionId, ticket.MatchConfidence
        }));
        WriteSheet(archive, 2, ticketRows);

        var solutionRows = new List<object?[]> { new object?[] { "solution id", "cluster id", "title", "problem", "procedure", "applicability", "warning", "risk", "status", "source count", "AI model", "prompt hash", "AI input masked" } };
        solutionRows.AddRange(state.Solutions.Select(solution =>
        {
            var ai = state.AIRuns.LastOrDefault(item => item.ClusterId == solution.ClusterId);
            return new object?[] { solution.Id, solution.ClusterId, solution.Title, solution.Problem, solution.Procedure, solution.Applicability, solution.Warning, solution.Risk, solution.Status, solution.SourceTicketCount, ai?.Model, ai?.PromptHash, ai?.InputWasMasked };
        }));
        WriteSheet(archive, 3, solutionRows);

        var clusterRows = new List<object?[]> { new object?[] { "cluster id", "name", "workspace", "category", "subcategory", "ticket count", "average quality", "knowledge value", "risk", "risk reason", "promoted", "promotion reason", "member external ids" } };
        clusterRows.AddRange(state.Clusters.OrderByDescending(cluster => cluster.TicketIds.Count).Select(cluster => new object?[]
        {
            cluster.Id, cluster.Name, cluster.Workspace, cluster.Category, cluster.Subcategory, cluster.TicketIds.Count,
            cluster.AverageQuality, cluster.KnowledgeValue, cluster.Risk, cluster.RiskReason, cluster.Promoted, cluster.PromotionReason,
            string.Join(",", state.Tickets.Where(ticket => cluster.TicketIds.Contains(ticket.Id)).Select(ticket => ticket.ExternalId).OrderBy(value => value))
        }));
        WriteSheet(archive, 4, clusterRows);
    }

    private static void WriteSheet(ZipArchive archive, int number, IEnumerable<object?[]> rows)
    {
        var entry = archive.CreateEntry($"xl/worksheets/sheet{number}.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), Indent = false });
        writer.WriteStartDocument(true);
        writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteStartElement("sheetData");
        var rowNumber = 0;
        foreach (var row in rows)
        {
            rowNumber++;
            writer.WriteStartElement("row"); writer.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
            for (var column = 0; column < row.Length; column++) WriteCell(writer, CellReference(column + 1, rowNumber), row[column]);
            writer.WriteEndElement();
        }
        writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
    }

    private static void WriteCell(XmlWriter writer, string reference, object? value)
    {
        writer.WriteStartElement("c"); writer.WriteAttributeString("r", reference);
        if (value is int or long or double or float or decimal)
        {
            writer.WriteElementString("v", Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteAttributeString("t", "inlineStr"); writer.WriteStartElement("is");
            writer.WriteStartElement("t"); writer.WriteAttributeString("xml", "space", null, "preserve");
            writer.WriteString(value switch { null => "", DateTimeOffset date => date.ToString("O"), _ => value.ToString() ?? "" });
            writer.WriteEndElement(); writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static string CellReference(int column, int row)
    {
        var letters = "";
        while (column > 0) { column--; letters = (char)('A' + column % 26) + letters; column /= 26; }
        return letters + row.ToString(CultureInfo.InvariantCulture);
    }

    private static void WriteText(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path, CompressionLevel.Optimal).Open(), new System.Text.UTF8Encoding(false));
        writer.Write(content);
    }
}
