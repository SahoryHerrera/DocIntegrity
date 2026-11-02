namespace DocIntegrity.Api.Services;

// Validación de archivos subidos, compartida por los controladores.
// Solo comprueba que el archivo exista, tenga extensión .pdf y la firma %PDF-:
// un archivo que pasa esta validación es un PDF "válido" aunque después su
// contenido no se pueda analizar (cifrado, dañado, escaneado).
public static class PdfFileValidator
{
    public sealed record ValidationError(string Code, string Message);

    // Un PDF válido contiene la firma %PDF- en su primer kilobyte.
    public const int SignatureWindow = 1024;

    public static bool HasPdfSignature(ReadOnlySpan<byte> header) =>
        header[..Math.Min(header.Length, SignatureWindow)].IndexOf("%PDF-"u8) >= 0;

    public static ValidationError? ValidateFile(IFormFile? file, string fieldName = "file")
    {
        if (file is null)
        {
            return new("file_required", $"Debes enviar un archivo en el campo \"{fieldName}\".");
        }

        if (file.Length == 0)
        {
            return new("empty_file", "El archivo está vacío.");
        }

        return null;
    }

    public static ValidationError? ValidatePdfExtension(IFormFile file) =>
        string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase)
            ? null
            : new("unsupported_file_type", "Actualmente solo se admiten archivos PDF.");

    public static async Task<ValidationError?> ValidatePdfAsync(IFormFile? file, string fieldName = "file")
    {
        var error = ValidateFile(file, fieldName) ?? ValidatePdfExtension(file!);

        if (error is not null)
        {
            return error;
        }

        var header = new byte[SignatureWindow];
        int read;

        await using (var stream = file!.OpenReadStream())
        {
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        }

        return HasPdfSignature(header.AsSpan(0, read))
            ? null
            : new("invalid_pdf", "El archivo no es un PDF válido.");
    }
}
