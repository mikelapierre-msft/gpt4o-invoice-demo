using Azure;
using gpt4o_invoice_demo;
using Microsoft.Extensions.Configuration;
using SkiaSharp;
using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;

var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddUserSecrets<Program>();
var config = builder.Build();

var documentIntelligence = new DocumentIntelligence(new Uri(config["DocIntelUri"]), new AzureKeyCredential(config["DocIntelKey"]));
var azureOpenAI = new AzureOpenAI(new Uri(config["AOAIUri"]), new ApiKeyCredential(config["AOAIKey"]));

var stats = new Stats();
var outputFile = $"output-{DateTime.Now:yyyyMMddhhmmssff}.json";
using (var streamWriter = new StreamWriter(outputFile))
{
    streamWriter.AutoFlush = true;
    streamWriter.WriteLine("{ \"data\": [");
    foreach (var file in Directory.GetFiles(config["SourceFolder"], "*.pdf"))
    {
        var sourceFile = file;
        Console.WriteLine($"Analyzing {Path.GetFileName(sourceFile)}...");

        var watch = Stopwatch.StartNew();

        //// Use Document Intelligence
        //var result = await documentIntelligence.ExecuteAsync(sourceFile, stats);

        //// Use Azure Open AI
        var files = await ConvertPdfToPngs(sourceFile);
        var result = await azureOpenAI.ExecuteLLMPromptPageByPageAsync(files, stats);

        watch.Stop();
        stats.OverallDuration += watch.Elapsed;
        Console.WriteLine($"Duration: {watch.ElapsedMilliseconds}ms");

        stats.OverallDocuments++;
        var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
        var prettyJson = JsonSerializer.Serialize(jsonResult, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        Console.WriteLine(prettyJson);
        if (stats.OverallDocuments > 1) streamWriter.WriteLine(",");
        streamWriter.WriteLine(prettyJson);
    }
    streamWriter.WriteLine($"], \"stats\": {JsonSerializer.Serialize(stats)} }}");
}

// Convert to the same format used with Azure OpenAI
//documentIntelligence.ConvertJsonFormat(outputFile);

Console.ReadLine();

async Task<List<BinaryData>> ConvertPdfToPngs(string pdfPath)
{
    var images = new List<BinaryData>();
    var pdf = await File.ReadAllBytesAsync(pdfPath);
    var pageImages = PDFtoImage.Conversion.ToImages(pdf);

    int i = 0;
    foreach (var pageImage in pageImages)
    {
        var pdfImageName = pdfPath.Replace(".pdf", $".{++i}.png");
        using (var stitchedFileStream = new FileStream(pdfImageName, FileMode.Create, FileAccess.Write))
        using (var resizedFileStream = new MemoryStream())
        {
            pageImage.Encode(stitchedFileStream, SKEncodedImageFormat.Png, 100);
            pageImage.Encode(resizedFileStream, SKEncodedImageFormat.Png, 100);
            images.Add(BinaryData.FromBytes(resizedFileStream.ToArray()));
        }
    }
    return images;
}