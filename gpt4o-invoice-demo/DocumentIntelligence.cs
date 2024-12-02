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
            var docs = j.GetProperty("data"); //.EnumerateArray();
            foreach (var docArray in docs.EnumerateArray())
            {
                foreach (var doc in docArray.EnumerateArray())
                {
                    var nomfourn = (string)null;
                    var addrfourn = (string)null;
                    var noboncomm = (string)null;
                    var nofact = (string)null;
                    var date = (string)null;
                    var soustot = (decimal?)null;
                    var tps = (decimal?)null;
                    var tvq = (decimal?)null;
                    var tot = (decimal?)null;

                    var fields = doc.GetProperty("Fields");
                    if (fields.TryGetProperty("VendorName", out var nomfournJson))
                        nomfourn = nomfournJson.GetProperty("ValueString").GetString();
                    if (fields.TryGetProperty("VendorAddress", out var addrfournJson))
                        addrfourn = addrfournJson.GetProperty("Content").GetString();
                    if (fields.TryGetProperty("PurchaseOrder", out var noboncommJson))
                        noboncomm = noboncommJson.GetProperty("ValueString").GetString();
                    if (fields.TryGetProperty("InvoiceId", out var nofactJson))
                        nofact = nofactJson.GetProperty("ValueString").GetString();
                    if (fields.TryGetProperty("InvoiceDate", out var dateJson))
                        date = dateJson.GetProperty("ValueDate").GetString();
                    if (fields.TryGetProperty("SubTotal", out var soustotJson))
                        soustot = soustotJson.GetProperty("ValueCurrency").GetProperty("Amount").GetDecimal();
                    if (fields.TryGetProperty("TaxDetails", out var taxDetailsJson))
                    {
                        foreach (var taxDetails in taxDetailsJson.GetProperty("ValueList").EnumerateArray())
                        {
                            if (taxDetails.GetProperty("Content").GetString().Contains("P", StringComparison.OrdinalIgnoreCase) && taxDetails.TryGetProperty("ValueDictionary", out var vdTPS) && vdTPS.TryGetProperty("Amount", out var amtTPS))
                                tps = amtTPS.GetProperty("ValueCurrency").GetProperty("Amount").GetDecimal();
                            if (taxDetails.GetProperty("Content").GetString().Contains("V", StringComparison.OrdinalIgnoreCase) && taxDetails.TryGetProperty("ValueDictionary", out var vdTVQ) && vdTVQ.TryGetProperty("Amount", out var amtTVQ))
                                tvq = amtTVQ.GetProperty("ValueCurrency").GetProperty("Amount").GetDecimal();
                        }
                    }
                    if (fields.TryGetProperty("InvoiceTotal", out var totJson))
                        tot = totJson.GetProperty("ValueCurrency").GetProperty("Amount").GetDecimal();

                    var lignes = new ArrayList();
                    if (fields.TryGetProperty("Items", out var itemsJson))
                    {
                        foreach (var item in itemsJson.GetProperty("ValueList").EnumerateArray())
                        {
                            var quant = (double?)null;
                            var code = (string)null;
                            var desc = (string)null;
                            var prix = (decimal?)null;
                            var mont = (decimal?)null;

                            if (item.TryGetProperty("ValueDictionary", out var vdQuant) && vdQuant.TryGetProperty("Quantity", out var quantJson))
                                quant = quantJson.GetProperty("ValueDouble").GetDouble();
                            if (item.TryGetProperty("ValueDictionary", out var vdCode) && vdCode.TryGetProperty("ProductCode", out var codeJson))
                                code = codeJson.GetProperty("ValueString").GetString();
                            if (item.TryGetProperty("ValueDictionary", out var vdDesc) && vdDesc.TryGetProperty("Description", out var descJson))
                                desc = descJson.GetProperty("ValueString").GetString();
                            if (item.TryGetProperty("ValueDictionary", out var vdPrix) && vdPrix.TryGetProperty("UnitPrice", out var prixJson))
                                prix = prixJson.GetProperty("ValueCurrency").GetProperty("Amount").GetDecimal();
                            if (item.TryGetProperty("ValueDictionary", out var vdMont) && vdMont.TryGetProperty("Amount", out var montJson))
                                mont = montJson.GetProperty("ValueCurrency").GetProperty("Amount").GetDecimal();

                            lignes.Add(new { Quantite = quant, CodeArticle = code, Description = desc, PrixUnitaire = prix, Montant = mont });
                        }
                    }

                    facturesJson.Add(new { Facture = new { NomFournisseur = nomfourn, AdresseFournisseur = addrfourn, NoBonCommande = noboncomm, NoFacture = nofact, Date = date, SousTotal = soustot, TPS = tps, TVQ = tvq, MontantTotal = tot, Lignes = lignes } });
                }
            }
            var finalJson = new { data = facturesJson };
            File.WriteAllText($"{inputFile}.reformat.json", JsonSerializer.Serialize(finalJson, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
        }
    }
}
