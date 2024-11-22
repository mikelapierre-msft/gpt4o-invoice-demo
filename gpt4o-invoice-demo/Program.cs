using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using SkiaSharp;
using System.ClientModel;
using System.Text.Json;

var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddUserSecrets<Program>();
var config = builder.Build();

foreach (var file in Directory.GetFiles(config["SourceFolder"], "*.pdf"))
{
    var sourceFile = file;
    Console.WriteLine($"Analyzing {Path.GetFileName(sourceFile)}...");

    //await CheckNumberOfPages(sourceFile);

    var convertedFile = await ConvertPdfToPng(sourceFile);
    var result = await ExecuteLLMPromptAsync(convertedFile);

    var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
    var prettyJson = JsonSerializer.Serialize(jsonResult, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(prettyJson);
}
Console.ReadLine();

async Task<string> ExecuteLLMPromptAsync(string imagePath)
{
    ChatResponseFormat chatResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
        jsonSchemaFormatName: "Facture",
        jsonSchema: BinaryData.FromString(File.ReadAllText("Schema.json")),
        jsonSchemaIsStrict: true);

    AzureOpenAIClient azureClient = new(
        new Uri(config["AOAIUri"]),
        new ApiKeyCredential(config["AOAIKey"]));
    ChatClient chatClient = azureClient.GetChatClient("gpt-4o");
    var completion = await chatClient.CompleteChatAsync(
    [
        new SystemChatMessage("Tu es un assistant IA qui permet d'extraire des informations des factures."),
        new UserChatMessage(
        [
            ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(File.ReadAllBytes(imagePath)), "image/png"),
            ChatMessageContentPart.CreateTextPart("Extraire les informations suivantes de la facture au format json: Numero de facture, Date, Numero de bon de commande, Montant total, Montant du.")
        ])
    ],
    new ChatCompletionOptions() { Temperature = 0.1f, TopP = 0.1f, ResponseFormat = chatResponseFormat });
    return completion.Value.Content[0].Text;
}

async Task CheckNumberOfPages(string pdfPath)
{
    var pdf = await File.ReadAllBytesAsync(pdfPath);
    var pageImages = PDFtoImage.Conversion.ToImages(pdf);
    Console.WriteLine($"Number of pages: {pageImages.Count()}");
}

async Task<string> ConvertPdfToPng(string pdfPath)
{
    var pdf = await File.ReadAllBytesAsync(pdfPath);
    var pageImages = PDFtoImage.Conversion.ToImages(pdf);

    var totalPageCount = pageImages.Count();
    var pageImageGroup = pageImages.Take(4);
    var pdfImageName = pdfPath.Replace(".pdf", ".png");

    int totalHeight = pageImageGroup.Sum(image => image.Height);
    int width = pageImageGroup.Max(image => image.Width);
    var stitchedImage = new SKBitmap(width, totalHeight);
    var canvas = new SKCanvas(stitchedImage);
    int currentHeight = 0;
    foreach (var pageImage in pageImageGroup)
    {
        canvas.DrawBitmap(pageImage, 0, currentHeight);
        currentHeight += pageImage.Height;
    }
    using (var stitchedFileStream = new FileStream(pdfImageName, FileMode.Create, FileAccess.Write))
    {
        stitchedImage.Encode(stitchedFileStream, SKEncodedImageFormat.Png, 100);
    }
    return pdfImageName;
}