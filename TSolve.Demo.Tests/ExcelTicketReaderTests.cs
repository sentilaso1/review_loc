using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TSolve.Demo.Options;
using TSolve.Demo.Services;
using TSolve.Demo.Services.Jira;

namespace TSolve.Demo.Tests;

public sealed class ExcelTicketReaderTests
{
    [Fact]
    public void Read_MapsRequiredColumns_AndCreatesStableIds()
    {
        var bytes = Workbook(
            ["summary", "description", "comment"],
            ["Printer queue stuck", "Documents remain queued.", "1. Clear queue. 2. Restart spooler. 3. Verify printing."]);
        var reader = new ExcelTicketReader();

        var first = reader.Read(new MemoryStream(bytes), "tickets.xlsx").Single();
        var second = reader.Read(new MemoryStream(bytes), "renamed.xlsx").Single();

        Assert.Equal("Printer queue stuck", first.Title);
        Assert.Equal("Documents remain queued.", first.Description);
        Assert.Equal("1. Clear queue. 2. Restart spooler. 3. Verify printing.", Assert.Single(first.Comments));
        Assert.Equal(first.ExternalId, second.ExternalId);
        Assert.Equal("Excel", first.Source);
    }

    [Fact]
    public void Read_RejectsMissingRequiredColumn()
    {
        var bytes = Workbook(["summary", "description"], ["Title", "Details"]);
        var exception = Assert.Throws<InvalidDataException>(() =>
            new ExcelTicketReader().Read(new MemoryStream(bytes), "tickets.xlsx"));

        Assert.Contains("comment", exception.Message);
    }

    [Fact]
    public async Task ImportExcel_IsIdempotentForTheSameWorkbook()
    {
        var bytes = Workbook(
            ["summary", "description", "comment"],
            ["Printer queue stuck", "Documents remain queued for several users.", "1. Clear queue. 2. Restart spooler. 3. Verify printing."]);
        var reader = new ExcelTicketReader();
        var tickets = reader.Read(new MemoryStream(bytes), "tickets.xlsx");
        var options = Microsoft.Extensions.Options.Options.Create(new JiraOptions { Mode = "Mock" });
        var pipeline = new KnowledgePipelineService(
            new DemoStore(),
            new TextProcessingService(),
            new JiraClientSelector(new MockJiraClient(), new RealJiraClient(new HttpClient(), options), options),
            options,
            NullLogger<KnowledgePipelineService>.Instance);

        var first = await pipeline.ImportExcelAsync(tickets, "tickets.xlsx", CancellationToken.None);
        var second = await pipeline.ImportExcelAsync(tickets, "tickets.xlsx", CancellationToken.None);

        Assert.Equal(1, first.Imported);
        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.IdempotentSkipped);
    }

    private static byte[] Workbook(string[] headers, string[] values)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Tickets" sheetId="1" r:id="rId1" /></sheets>
                </workbook>
                """);
            Write(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml" />
                </Relationships>
                """);
            Write(archive, "xl/worksheets/sheet1.xml", $"""
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
                  <row r="1">{Cells(headers, 1)}</row>
                  <row r="2">{Cells(values, 2)}</row>
                </sheetData></worksheet>
                """);
        }
        return output.ToArray();
    }

    private static string Cells(IEnumerable<string> values, int row) => string.Concat(values.Select((value, index) =>
        $"<c r=\"{(char)('A' + index)}{row}\" t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(value)}</t></is></c>"));

    private static void Write(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
