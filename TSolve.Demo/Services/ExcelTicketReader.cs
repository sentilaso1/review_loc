using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using TSolve.Demo.Models;

namespace TSolve.Demo.Services;

public sealed class ExcelTicketReader
{
    private const int MaxFileBytes = 10_000_000;
    private const int MaxXmlEntryBytes = 20_000_000;
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly string[] RequiredHeaders = ["summary", "description", "comment"];

    public IReadOnlyList<SourceTicket> Read(Stream input, string fileName)
    {
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (copy.Length + read > MaxFileBytes) throw new InvalidDataException("The Excel file must be 10 MB or smaller.");
            copy.Write(buffer, 0, read);
        }
        var bytes = copy.ToArray();
        if (bytes.Length == 0) throw new InvalidDataException("The Excel file is empty.");

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var sharedStrings = ReadSharedStrings(archive);
        var sheet = LoadXml(GetFirstWorksheet(archive));
        var rows = sheet.Descendants(Spreadsheet + "row").ToList();
        if (rows.Count == 0) throw new InvalidDataException("The first worksheet has no rows.");

        var headerRow = rows.FirstOrDefault(row => ReadRow(row, sharedStrings).Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            ?? throw new InvalidDataException("The first worksheet has no header row.");
        var headers = ReadRow(headerRow, sharedStrings)
            .ToDictionary(cell => cell.Key, cell => cell.Value.Trim().ToLowerInvariant());
        var columns = RequiredHeaders.ToDictionary(name => name, name => headers.FirstOrDefault(header => header.Value == name).Key);
        var missing = columns.Where(column => column.Value == 0).Select(column => column.Key).ToArray();
        if (missing.Length > 0) throw new InvalidDataException($"Missing required column(s): {string.Join(", ", missing)}.");
        var appColumn = headers.FirstOrDefault(header => header.Value == "app").Key;
        var internalIdColumn = headers.FirstOrDefault(header => header.Value == "internal_ticket_id").Key;

        var fileId = Convert.ToHexString(SHA256.HashData(bytes))[..16];
        var safeName = Path.GetFileName(fileName);
        var importedAt = DateTimeOffset.UtcNow;
        var tickets = new List<SourceTicket>();

        foreach (var row in rows.SkipWhile(row => row != headerRow).Skip(1))
        {
            var cells = ReadRow(row, sharedStrings);
            var summary = Get(cells, columns["summary"]);
            var description = Get(cells, columns["description"]);
            var comment = Get(cells, columns["comment"]);
            if (string.IsNullOrWhiteSpace(summary) && string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(comment)) continue;

            var rowNumber = (int?)row.Attribute("r") ?? tickets.Count + 2;
            if (string.IsNullOrWhiteSpace(summary)) throw new InvalidDataException($"Row {rowNumber} has no summary.");
            var application = Get(cells, appColumn);
            var internalId = Get(cells, internalIdColumn);

            tickets.Add(new SourceTicket
            {
                Source = "Excel",
                ExternalId = string.IsNullOrWhiteSpace(internalId) ? $"{fileId}:{rowNumber}" : $"Excel:{internalId}",
                SourceUrl = $"excel://{Uri.EscapeDataString(safeName)}#row={rowNumber}",
                Title = summary,
                Application = application,
                Description = description,
                Resolution = "",
                Comments = string.IsNullOrWhiteSpace(comment) ? [] : [comment],
                Status = "Resolved",
                IssueType = "Imported ticket",
                Workspace = "GENERAL",
                CreatedAt = importedAt,
                ResolvedAt = importedAt,
                UpdatedAt = importedAt,
                RawJson = JsonSerializer.Serialize(new { internal_ticket_id = internalId, app = application, summary, description, comment })
            });
        }

        if (tickets.Count == 0) throw new InvalidDataException("The Excel file has no ticket rows.");
        return tickets;
    }

    private static ZipArchiveEntry GetFirstWorksheet(ZipArchive archive)
    {
        var workbook = LoadXml(RequiredEntry(archive, "xl/workbook.xml"));
        var relationshipId = workbook.Descendants(Spreadsheet + "sheet").FirstOrDefault()?.Attribute(Relationships + "id")?.Value
            ?? throw new InvalidDataException("The workbook has no worksheet.");
        var relationships = LoadXml(RequiredEntry(archive, "xl/_rels/workbook.xml.rels"));
        var target = relationships.Descendants(PackageRelationships + "Relationship")
            .FirstOrDefault(item => (string?)item.Attribute("Id") == relationshipId)?.Attribute("Target")?.Value
            ?? throw new InvalidDataException("The first worksheet relationship is missing.");
        var normalized = target.Replace('\\', '/').TrimStart('/');
        var path = normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? normalized : $"xl/{normalized}";
        return RequiredEntry(archive, path);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        return LoadXml(entry).Descendants(Spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(Spreadsheet + "t").Select(text => text.Value)))
            .ToList();
    }

    private static Dictionary<int, string> ReadRow(XElement row, IReadOnlyList<string> sharedStrings) =>
        row.Elements(Spreadsheet + "c").ToDictionary(
            cell => ColumnNumber((string?)cell.Attribute("r")),
            cell => ReadCell(cell, sharedStrings));

    private static string ReadCell(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr") return string.Concat(cell.Descendants(Spreadsheet + "t").Select(text => text.Value));
        var value = cell.Element(Spreadsheet + "v")?.Value ?? "";
        return type == "s" && int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Count ? sharedStrings[index] : value;
    }

    private static int ColumnNumber(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return 0;
        var column = 0;
        foreach (var character in reference.TakeWhile(char.IsLetter)) column = column * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        return column;
    }

    private static string Get(IReadOnlyDictionary<int, string> cells, int column) =>
        column > 0 && cells.TryGetValue(column, out var value) ? value.Trim() : "";

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, string path) =>
        archive.GetEntry(path) ?? throw new InvalidDataException($"Invalid Excel file: {path} is missing.");

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxXmlEntryBytes) throw new InvalidDataException("The Excel file contains an oversized worksheet.");
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.None);
    }
}
