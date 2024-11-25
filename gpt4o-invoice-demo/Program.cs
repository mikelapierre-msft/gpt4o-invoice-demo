using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using SkiaSharp;
using System.ClientModel;
using System.Data.SqlTypes;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;

var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddUserSecrets<Program>();
var config = builder.Build();

var pageCount = new List<int>();

using (var streamWriter = new StreamWriter("output.json"))
{
    int i = 0;
    streamWriter.AutoFlush = true;
    streamWriter.WriteLine("[");
    foreach (var file in Directory.GetFiles(config["SourceFolder"], "*.pdf"))
    {        
        var sourceFile = file;
        Console.WriteLine($"Analyzing {Path.GetFileName(sourceFile)}...");

        //pageCount.Add(await CheckNumberOfPages(sourceFile));

        var files = await ConvertPdfToPngsUnaltered(sourceFile);
        var result = await ExecuteLLMPromptPageByPageAsync(files);

        //var convertedFile = await ConvertPdfToPngBestRectangle(sourceFile);
        //var result = await ExecuteLLMPromptAsync(convertedFile);

        var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
        var prettyJson = JsonSerializer.Serialize(jsonResult, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        Console.WriteLine(prettyJson);
        streamWriter.WriteLine(prettyJson + ",");
    }
    streamWriter.WriteLine("]");

    Console.WriteLine($"Average pages per doc: {pageCount.Sum()/pageCount.Count()}; Max pages per doc {pageCount.Max()}");
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
            ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(File.ReadAllBytes(imagePath)), "image/png", ChatImageDetailLevel.High),
            ChatMessageContentPart.CreateTextPart("Extraire les informations suivantes de la facture au format json: Fournisseur, Adresse du fournisseur, Numero de bon de commande, Numero de facture, Date, Sous total, Taxes sur les produits et services (TPS), Taxe de vente provinciale (TVQ), Montant total. Pour chacune des lignes de la facture, extraire les informations suivantes: Quantité, Code de l'article, Description, Prix unitaire, Montant.")
        ])
    ],
    new ChatCompletionOptions() { Temperature = 0.1f, TopP = 0.1f, ResponseFormat = chatResponseFormat });
    Console.WriteLine($"Input tokens: {completion.Value.Usage.InputTokenCount}, Output tokens: {completion.Value.Usage.OutputTokenCount}, Total tokens: {completion.Value.Usage.TotalTokenCount}");
    return completion.Value.Content[0].Text;
}

async Task<string> ExecuteLLMPromptByPageAsync(List<BinaryData> pages)
{
    ChatResponseFormat chatResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
        jsonSchemaFormatName: "Facture",
        jsonSchema: BinaryData.FromString(File.ReadAllText("Schema.json")),
        jsonSchemaIsStrict: true);

    AzureOpenAIClient azureClient = new(
        new Uri(config["AOAIUri"]),
        new ApiKeyCredential(config["AOAIKey"]));
    ChatClient chatClient = azureClient.GetChatClient("gpt-4o");
    var userMessages = new UserChatMessage();
    foreach (var page in pages)
    {
        userMessages.Content.Add(ChatMessageContentPart.CreateImagePart(page, "image/png", ChatImageDetailLevel.High));       
    }
    userMessages.Content.Add(ChatMessageContentPart.CreateTextPart("Extraire les informations suivantes de la facture au format json: Fournisseur, Adresse du fournisseur, Numero de bon de commande, Numero de facture, Date, Sous total, Taxes sur les produits et services (TPS), Taxe de vente provinciale (TVQ), Montant total. Pour chacune des lignes de la facture, extraire les informations suivantes: Quantité, Code de l'article, Description, Prix unitaire, Montant."));
    var completion = await chatClient.CompleteChatAsync(
    [
        new SystemChatMessage("Tu es un assistant IA qui permet d'extraire des informations des factures."),
        userMessages
    ],
    new ChatCompletionOptions() { Temperature = 0.1f, TopP = 0.1f, ResponseFormat = chatResponseFormat });
    Console.WriteLine($"Input tokens: {completion.Value.Usage.InputTokenCount}, Output tokens: {completion.Value.Usage.OutputTokenCount}, Total tokens: {completion.Value.Usage.TotalTokenCount}");
    return completion.Value.Content[0].Text;
}

async Task<string> ExecuteLLMPromptPageByPageAsync(List<BinaryData> pages)
{
    ChatResponseFormat chatResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
        jsonSchemaFormatName: "Facture",
        jsonSchema: BinaryData.FromString(File.ReadAllText("Schema.json")),
        jsonSchemaIsStrict: true);

    AzureOpenAIClient azureClient = new(
        new Uri(config["AOAIUri"]),
        new ApiKeyCredential(config["AOAIKey"]));
    ChatClient chatClient = azureClient.GetChatClient("gpt-4o");

    string previousResponse = null;
    int totalInputTokens = 0;
    int totalOutputTokens = 0;
    int totalTokens = 0;
    foreach (var page in Enumerable.Reverse(pages))
    {
        var userMessages = new UserChatMessage();
        userMessages.Content.Add(ChatMessageContentPart.CreateImagePart(page, "image/png", ChatImageDetailLevel.High));
        userMessages.Content.Add(ChatMessageContentPart.CreateTextPart("Extraire les informations suivantes de la facture au format json: Fournisseur, Adresse du fournisseur, Numero de bon de commande, Numero de facture, Date, Sous total, Taxes sur les produits et services (TPS), Taxe de vente provinciale (TVQ), Montant total. Pour chacune des lignes de la facture, extraire les informations suivantes: Quantité, Code de l'article, Description, Prix unitaire, Montant."));
        if (previousResponse != null)
        {
            userMessages.Content.Add(ChatMessageContentPart.CreateTextPart($"Voici les informations extraites des pages analysées auparavant: {previousResponse}"));
        }
        var completion = await chatClient.CompleteChatAsync(
        [
            new SystemChatMessage("Tu es un assistant IA qui permet d'extraire des informations des factures de la dernière page jusqu'à la première."),
            userMessages
        ],
        new ChatCompletionOptions() { Temperature = 0.1f, TopP = 0.1f, ResponseFormat = chatResponseFormat });
        totalInputTokens += completion.Value.Usage.InputTokenCount;
        totalOutputTokens += completion.Value.Usage.OutputTokenCount;
        totalTokens += completion.Value.Usage.TotalTokenCount;
        Console.WriteLine($"Single page - Input tokens: {completion.Value.Usage.InputTokenCount}, Output tokens: {completion.Value.Usage.OutputTokenCount}, Total tokens: {completion.Value.Usage.TotalTokenCount}");
        previousResponse = completion.Value.Content[0].Text;
    }

    Console.WriteLine($"Total document - Input tokens: {totalInputTokens}, Output tokens: {totalOutputTokens}, Total tokens: {totalTokens}");
    return previousResponse;
}

async Task<int> CheckNumberOfPages(string pdfPath)
{
    var pdf = await File.ReadAllBytesAsync(pdfPath);
    var pageImages = PDFtoImage.Conversion.ToImages(pdf);
    Console.WriteLine($"Number of pages: {pageImages.Count()}");
    return pageImages.Count();
}

async Task<string> ConvertPdfToPng(string pdfPath)
{
    var pdf = await File.ReadAllBytesAsync(pdfPath);
    var pageImages = PDFtoImage.Conversion.ToImages(pdf);

    var totalPageCount = pageImages.Count();
    var pageImageGroup = pageImages; //.Take(4);
    var pdfImageName = pdfPath.Replace(".pdf", ".png");

    // Things to try: send every page individually in low res mode (and high res); stitch all pages as perfect squares (1, 4, 9, 16, etc.);

    int totalHeight = pageImageGroup.Sum(image => Math.Max(image.Width, image.Height));
    int width = pageImageGroup.Max(image => Math.Max(image.Width, image.Height));
    var stitchedImage = new SKBitmap(width, totalHeight);
    var canvas = new SKCanvas(stitchedImage);
    int currentHeight = 0;
    foreach (var pageImage in pageImageGroup)
    {
        canvas.DrawBitmap(pageImage, 0, currentHeight);
        currentHeight += Math.Max(pageImage.Width, pageImage.Height);
    }
    using (var stitchedFileStream = new FileStream(pdfImageName, FileMode.Create, FileAccess.Write))
    {
        stitchedImage.Encode(stitchedFileStream, SKEncodedImageFormat.Png, 100);
    }
    return pdfImageName;
}

async Task<List<BinaryData>> ConvertPdfToPngsUnaltered(string pdfPath)
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

async Task<List<BinaryData>> ConvertPdfToPngs(string pdfPath)
{
    var images = new List<BinaryData>();
    var pdf = await File.ReadAllBytesAsync(pdfPath);
    var pageImages = PDFtoImage.Conversion.ToImages(pdf);

    int i = 0;
    foreach(var pageImage in pageImages)
    {
        var squareSize = Math.Max(pageImage.Width, pageImage.Height);
        var newWidth = pageImage.Width == squareSize ? 512 : (int)(512 * (pageImage.Width / (float)squareSize));
        var newHeight = pageImage.Height == squareSize ? 512 : (int)(512 * (pageImage.Height / (float)squareSize));
        var resizedImage = pageImage.Resize(new SKSizeI(newWidth, newHeight), SKFilterQuality.High);
        var pdfImageName = pdfPath.Replace(".pdf", $".{++i}.png");
        var squareImage = new SKBitmap(512, 512);
        var canvas = new SKCanvas(squareImage);
        canvas.DrawBitmap(resizedImage, 0, 0);
        using (var stitchedFileStream = new FileStream(pdfImageName, FileMode.Create, FileAccess.Write))
        using (var resizedFileStream = new MemoryStream())
        {
            squareImage.Encode(stitchedFileStream, SKEncodedImageFormat.Png, 100);
            squareImage.Encode(resizedFileStream, SKEncodedImageFormat.Png, 100);
            images.Add(BinaryData.FromBytes(resizedFileStream.ToArray()));
        }
    }
    return images;
}

async Task<string> ConvertPdfToPngSquare(string pdfPath)
{
    var images = new List<BinaryData>();
    var pdf = await File.ReadAllBytesAsync(pdfPath);
    var pageImages = PDFtoImage.Conversion.ToImages(pdf);

    int i = 0;
    var pdfImageName = pdfPath.Replace(".pdf", $".png");
    int closestSquare = (int)Math.Ceiling(Math.Sqrt(pageImages.Count()));
    var squareImage = new SKBitmap(2048 * closestSquare, 2048 * closestSquare);
    var canvas = new SKCanvas(squareImage);

    foreach (var pageImage in pageImages)
    {
        var squareSize = Math.Max(pageImage.Width, pageImage.Height);
        var newWidth = pageImage.Width == squareSize ? 2048 : (int)(2048 * (pageImage.Width / (float)squareSize));
        var newHeight = pageImage.Height == squareSize ? 2048 : (int)(2048 * (pageImage.Height / (float)squareSize));
        var resizedImage = pageImage.Resize(new SKSizeI(newWidth, newHeight), SKFilterQuality.High);
        canvas.DrawBitmap(resizedImage, i % closestSquare * 2048, i / closestSquare * 2048);
        i++;
    }
    using (var stitchedFileStream = new FileStream(pdfImageName, FileMode.Create, FileAccess.Write))
    using (var resizedFileStream = new MemoryStream())
    {
        squareImage.Encode(stitchedFileStream, SKEncodedImageFormat.Png, 100);
        squareImage.Encode(resizedFileStream, SKEncodedImageFormat.Png, 100);
        images.Add(BinaryData.FromBytes(resizedFileStream.ToArray()));
    }
    return pdfImageName;
}

async Task<string> ConvertPdfToPngBestRectangle(string pdfPath)
{
    var images = new List<BinaryData>();
    var pdf = await File.ReadAllBytesAsync(pdfPath);
    var pageImages = PDFtoImage.Conversion.ToImages(pdf);

    int i = 0;
    var pdfImageName = pdfPath.Replace(".pdf", $".png");
    var closestRectangle = GetBestRectangle(pageImages.Count());
    var squareImage = new SKBitmap(2048 * closestRectangle.Item1, 2048 * closestRectangle.Item2);
    var canvas = new SKCanvas(squareImage);

    foreach (var pageImage in pageImages)
    {
        var squareSize = Math.Max(pageImage.Width, pageImage.Height);
        var newWidth = pageImage.Width == squareSize ? 2048 : (int)(2048 * (pageImage.Width / (float)squareSize));
        var newHeight = pageImage.Height == squareSize ? 2048 : (int)(2048 * (pageImage.Height / (float)squareSize));
        var resizedImage = pageImage.Resize(new SKSizeI(newWidth, newHeight), SKFilterQuality.High);
        canvas.DrawBitmap(resizedImage, i % closestRectangle.Item1 * 2048, i / closestRectangle.Item1 * 2048);
        i++;
    }
    using (var stitchedFileStream = new FileStream(pdfImageName, FileMode.Create, FileAccess.Write))
    using (var resizedFileStream = new MemoryStream())
    {
        squareImage.Encode(stitchedFileStream, SKEncodedImageFormat.Png, 100);
        squareImage.Encode(resizedFileStream, SKEncodedImageFormat.Png, 100);
        images.Add(BinaryData.FromBytes(resizedFileStream.ToArray()));
    }
    return pdfImageName;
}

(int,int) GetBestRectangle(int count)
{
    int sqrt = (int)Math.Sqrt(count);
    if (count <= sqrt * (sqrt + 1)) return (sqrt + 1, sqrt);
    return (sqrt + 1, sqrt + 1);
}