using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace gpt4o_invoice_demo
{
    internal class AzureOpenAI
    {
        private Uri endpoint;
        private ApiKeyCredential credential;

        public AzureOpenAI(Uri endpoint, ApiKeyCredential credential)
        {
            this.endpoint = endpoint;
            this.credential = credential;
        }

        public async Task<string> ExecuteLLMPromptPageByPageAsync(List<BinaryData> pages, Stats stats)
        {
            ChatResponseFormat chatResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "Facture",
                jsonSchema: BinaryData.FromString(File.ReadAllText("Schema.json")),
                jsonSchemaIsStrict: true);

            AzureOpenAIClient azureClient = new(endpoint, credential);
            ChatClient chatClient = azureClient.GetChatClient("gpt-4o");

            string previousResponse = null;
            int totalInputTokens = 0;
            int totalOutputTokens = 0;
            int totalTokens = 0;
            foreach (var page in Enumerable.Reverse(pages))
            {
                var userMessages = new UserChatMessage();
                userMessages.Content.Add(ChatMessageContentPart.CreateImagePart(page, "image/png", ChatImageDetailLevel.High));
                userMessages.Content.Add(ChatMessageContentPart.CreateTextPart("Extraire les informations suivantes de la facture au format json: Fournisseur, Adresse du fournisseur, Numero de bon de commande, Numero de facture, Date (utiliser le format YYYY-MM-DD), Sous total, Taxes sur les produits et services (TPS), Taxe de vente provinciale (TVQ), Montant total. Pour chacune des lignes de la facture, extraire les informations suivantes: Quantité, Code de l'article, Description, Prix unitaire, Montant."));
                if (previousResponse != null)
                {
                    userMessages.Content.Add(ChatMessageContentPart.CreateTextPart($"Voici les informations extraites des pages analysées auparavant: {previousResponse}"));
                }
                var completion = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage("Tu es un assistant IA qui permet d'extraire des informations des factures de la dernière page jusqu'à la première."), userMessages
                ],
                new ChatCompletionOptions() { Temperature = 0.1f, TopP = 0.1f, ResponseFormat = chatResponseFormat });
                stats.OverallPages += pages.Count;
                stats.OverallInputTokens += completion.Value.Usage.InputTokenCount;
                stats.OverallOutputTokens += completion.Value.Usage.OutputTokenCount;
                stats.OverallTokens += completion.Value.Usage.TotalTokenCount;
                totalInputTokens += completion.Value.Usage.InputTokenCount;
                totalOutputTokens += completion.Value.Usage.OutputTokenCount;
                totalTokens += completion.Value.Usage.TotalTokenCount;
                Console.WriteLine($"Single page - Input tokens: {completion.Value.Usage.InputTokenCount}, Output tokens: {completion.Value.Usage.OutputTokenCount}, Total tokens: {completion.Value.Usage.TotalTokenCount}");
                previousResponse = completion.Value.Content[0].Text;
            }

            Console.WriteLine($"Total document - Input tokens: {totalInputTokens}, Output tokens: {totalOutputTokens}, Total tokens: {totalTokens}");
            return previousResponse;
        }
    }
}
