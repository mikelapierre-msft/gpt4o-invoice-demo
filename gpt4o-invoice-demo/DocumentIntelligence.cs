using Azure.AI.DocumentIntelligence;
using Azure;
using System.Collections;
using System.Text.Json;

namespace gpt4o_invoice_demo
{
    internal class DocumentIntelligence
    {
        private Uri endpoint;
        private AzureKeyCredential credential;

        public DocumentIntelligence(Uri endpoint, AzureKeyCredential credential)
        {
            this.endpoint = endpoint;
            this.credential = credential;
        }

        public async Task<string> ExecuteAsync(string imagePath, Stats stats)
        {
            DocumentIntelligenceClient client = new(endpoint, credential);
            var content = new AnalyzeDocumentContent() { Base64Source = new BinaryData(File.ReadAllBytes(imagePath)) };
            var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-invoice", content);
            stats.OverallPages += operation.Value.Pages.Count;
            return JsonSerializer.Serialize(operation.Value.Documents);
        }

        public void ConvertJsonFormat(string inputFile)
        {
            var facturesJson = new ArrayList();
            var j = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(inputFile));
            var docs = j.GetProperty("data");
            foreach (var docArray in docs.EnumerateArray())
            {
                foreach (var doc in docArray.EnumerateArray())
                {
                    var fields = doc.GetProperty("Fields");

                    var nomFournisseur = GetPropertyIfExists(fields, "VendorName", "ValueString")?.GetString();
                    var adresseFournisseur = GetPropertyIfExists(fields, "VendorAddress", "Content")?.GetString();
                    var noBonCommande = GetPropertyIfExists(fields, "PurchaseOrder", "ValueString")?.GetString();
                    var noFacture = GetPropertyIfExists(fields, "InvoiceId", "ValueString")?.GetString();
                    var date = GetPropertyIfExists(fields, "InvoiceDate", "ValueDate")?.GetString();
                    var sousTotal = GetPropertyIfExists(fields, "SubTotal", "ValueCurrency", "Amount")?.GetDecimal();
                    var taxes = GetPropertyIfExists(fields, "TaxDetails", "ValueList")?.EnumerateArray();
                    var tps = GetPropertyIfExists(taxes?.FirstOrDefault(t => ContentContains(t, "P")), "ValueDictionary", "Amount", "ValueCurrency", "Amount");
                    var tvq = GetPropertyIfExists(taxes?.FirstOrDefault(t => ContentContains(t, "V")), "ValueDictionary", "Amount", "ValueCurrency", "Amount");
                    var montantTotal = GetPropertyIfExists(fields, "InvoiceTotal", "ValueCurrency", "Amount")?.GetDecimal();

                    var lignes = new ArrayList();
                    var items = GetPropertyIfExists(fields, "Items", "ValueList")?.EnumerateArray();
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            var quantite = GetPropertyIfExists(item, "ValueDictionary", "Quantity", "ValueDouble")?.GetDouble();
                            var codeArticle = GetPropertyIfExists(item, "ValueDictionary", "ProductCode", "ValueString")?.GetString();
                            var description = GetPropertyIfExists(item, "ValueDictionary", "Description", "ValueString")?.GetString();
                            var prixUnitaire = GetPropertyIfExists(item, "ValueDictionary", "UnitPrice", "ValueCurrency", "Amount")?.GetDecimal();
                            var montant = GetPropertyIfExists(item, "ValueDictionary", "Amount", "ValueCurrency", "Amount")?.GetDecimal();
                            lignes.Add(new { Quantite = quantite, CodeArticle = codeArticle, Description = description, PrixUnitaire = prixUnitaire, Montant = montant });
                        }
                    }

                    facturesJson.Add(new { Facture = new { NomFournisseur = nomFournisseur, 
                                                           AdresseFournisseur = adresseFournisseur, 
                                                           NoBonCommande = noBonCommande, 
                                                           NoFacture = noFacture, 
                                                           Date = date, 
                                                           SousTotal = sousTotal, 
                                                           TPS = tps, 
                                                           TVQ = tvq,
                                                           MontantTotal = montantTotal, 
                                                           Lignes = lignes } });
                }
            }
            var finalJson = new { data = facturesJson };
            File.WriteAllText($"{inputFile}.reformat.json", JsonSerializer.Serialize(finalJson, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
        }

        private static JsonElement? GetPropertyIfExists(JsonElement? element, params string[] properties)
        {
            if (element == null || element.Value.Equals(default(JsonElement))) return null;

            for (int i=0; i< properties.Length; i++)
            {
                var property = properties[i];
                if (element.Value.TryGetProperty(property, out var jsonValue))
                {
                    if (i == properties.Length - 1) return jsonValue;
                    else element = jsonValue;
                }
                else
                {
                    return null;
                }
            }
            return null;
        }

        private static bool ContentContains(JsonElement element, string value)
        {
            return (GetPropertyIfExists(element, "Content")?.GetString() ?? String.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
