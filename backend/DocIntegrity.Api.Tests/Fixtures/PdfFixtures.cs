using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DocIntegrity.Api.Tests.Fixtures;

// Página de un PDF ficticio: líneas de texto y, opcionalmente, una imagen que
// cubre toda la página (simula un escaneo).
public sealed record PageSpec(string[] Lines, bool FullPageImage = false)
{
    public static PageSpec Of(params string[] lines) => new(lines);
    public static PageSpec Scanned(params string[] lines) => new(lines, FullPageImage: true);
}

// Documentos PDF ficticios generados en memoria para las pruebas.
public static class PdfFixtures
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;

    public static byte[] Build(IEnumerable<PageSpec> pages, Action<PdfDocumentBuilder.DocumentInformationBuilder>? info = null)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        info?.Invoke(builder.DocumentInformation);

        foreach (var spec in pages)
        {
            var page = builder.AddPage(PageSize.A4);

            if (spec.FullPageImage)
            {
                page.AddPng(Png(60, 80), new PdfRectangle(0, 0, PageWidth, PageHeight));
            }

            var y = 800.0;

            foreach (var line in spec.Lines)
            {
                page.AddText(line, 11, new PdfPoint(50, y), font);
                y -= 16;
            }
        }

        return builder.Build();
    }

    public static byte[] Build(params PageSpec[] pages) => Build((IEnumerable<PageSpec>)pages);

    public static byte[] Lines(params string[] lines) => Build(PageSpec.Of(lines));

    // Muchas páginas iguales con una línea numerada.
    public static byte[] ManyPages(int count) =>
        Build(Enumerable.Range(1, count).Select(i => PageSpec.Of($"Pagina {i} del documento de prueba.")));

    // Texto pseudoaleatorio determinista: páginas × líneas × palabras.
    public static byte[] RandomText(int seed, int pages, int linesPerPage, int wordsPerLine)
    {
        var random = new Random(seed);
        string Word() => new(Enumerable.Range(0, random.Next(3, 9)).Select(_ => (char)('a' + random.Next(26))).ToArray());

        return Build(Enumerable.Range(0, pages).Select(_ =>
            new PageSpec(Enumerable.Range(0, linesPerPage)
                .Select(_ => string.Join(' ', Enumerable.Range(0, wordsPerLine).Select(_ => Word())))
                .ToArray())));
    }

    // PDF cifrado con contraseña de apertura (Standard, RC4 40 bits). La entrada /U
    // no corresponde a la contraseña vacía, así que no se puede abrir sin contraseña.
    public static byte[] EncryptedWithUserPassword()
    {
        const string content = "BT /F1 12 Tf 72 720 Td (Documento confidencial) Tj ET";

        return RawPdf(
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>",
                $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
                $"<< /Filter /Standard /V 1 /R 2 /O <{Hex(0x11, 32)}> /U <{Hex(0x22, 32)}> /P -3904 >>"
            ],
            $"/Encrypt 5 0 R /ID [<{Hex(0x33, 16)}> <{Hex(0x33, 16)}>]");
    }

    // Firma %PDF- correcta seguida de bytes sin estructura PDF.
    public static byte[] Garbage(int length = 4096)
    {
        var random = new Random(7);
        var bytes = new byte[length];
        random.NextBytes(bytes);
        var header = "%PDF-1.7\n"u8;
        header.CopyTo(bytes);
        return bytes;
    }

    private static byte[] RawPdf(string[] objects, string trailerExtra)
    {
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = Encoding.Latin1.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R {trailerExtra} >>\nstartxref\n{xref}\n%%EOF\n");

        return Encoding.Latin1.GetBytes(pdf.ToString());
    }

    private static string Hex(byte value, int count) => Convert.ToHexString(Enumerable.Repeat(value, count).ToArray());

    // PNG RGB mínimo (gris) generado sin bibliotecas.
    public static byte[] Png(int width, int height)
    {
        var raw = new byte[height * (1 + width * 3)];

        for (var row = 0; row < height; row++)
        {
            var start = row * (1 + width * 3);
            raw[start] = 0; // filtro "None"
            Array.Fill(raw, (byte)(120 + row % 60), start + 1, width * 3);
        }

        byte[] compressed;

        using (var output = new MemoryStream())
        {
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            compressed = output.ToArray();
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;  // bits por canal
        header[9] = 2;  // RGB
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed);
        Chunk(png, "IEND", []);

        return png.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);

        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(typeAndData);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32(typeAndData));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in data)
        {
            crc ^= b;

            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
