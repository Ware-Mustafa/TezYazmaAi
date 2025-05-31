using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using QuestPDF.Fluent;
using Document = QuestPDF.Fluent.Document;
using Xceed.Document.NET;
using Xceed.Words.NET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

public class HomeController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private static List<ChatMessage> _conversationHistory = new();

    public HomeController(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage(string userMessage)
    {
        try
        {
            var apiKey = _config["Groq:Token"];
            var apiUrl = _config["Groq:ApiUrl"];

            if (string.IsNullOrEmpty(apiKey))
                throw new Exception("API Key bulunamadý!");

            _conversationHistory.Add(new ChatMessage("user", userMessage));

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var messages = new List<object>
            {
                new { role = "system", content = GetSystemPrompt() }
            };

            foreach (var msg in _conversationHistory.TakeLast(5))
            {
                messages.Add(new { role = msg.Role, content = msg.Content });
            }

            var response = await client.PostAsJsonAsync(apiUrl, new
            {
                messages,
                model = "llama3-70b-8192",
                temperature = 0.7
            });

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"API Hatasý: {response.StatusCode}\n{errorContent}");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            dynamic jsonResponse = JsonConvert.DeserializeObject(responseString);
            var botReply = HumanizeText(jsonResponse.choices[0].message.content.ToString());

            _conversationHistory.Add(new ChatMessage("assistant", botReply));

            return Json(new { reply = botReply });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost]
    public IActionResult Download(string format, [FromBody] DownloadRequest request)
    {
        try
        {
            var processedContent = MakeTextHumanLike(request.Content);
            byte[] fileBytes;
            string mimeType;
            string fileName = $"tez_{DateTime.Now:yyyyMMddHHmmss}.{format}";

            if (format.ToLower() == "pdf")
            {
                fileBytes = GeneratePdf(processedContent);
                mimeType = "application/pdf";
            }
            else 
            {
                fileBytes = GenerateWord(processedContent);
                mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            }

            return File(fileBytes, mimeType, fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private string HumanizeText(string text)
    {
        var random = new Random();
        var replacements = new Dictionary<string, string>
        {
            { "birçok", "bir çok" }, { "herþey", "her þey" },
            { "örneðin", "mesela" }, { "ancak", "ama" }
        };

        foreach (var kvp in replacements)
        {
            if (random.Next(100) < 30) text = text.Replace(kvp.Key, kvp.Value);
        }

        return text;
    }

    private string MakeTextHumanLike(string content)
    {
        var random = new Random();
        var charArray = content.ToCharArray();

        for (int i = 0; i < charArray.Length; i++)
        {
            if (random.Next(100) < 3) charArray[i] = GetSimilarChar(charArray[i]);
        }

        content = new string(charArray)
            .Replace(".", ". ")
            .Replace("  ", " ");

        return content;
    }

    private char GetSimilarChar(char c)
    {
        switch (c)
        {
            case 'a': return 'à';
            case 'e': return 'é';
            case 'i': return 'ï';
            case 'o': return 'ô';
            case 'u': return 'û';
            case 'g': return 'ð';
            case 'c': return 'ç';
            default: return c;
        }
    }

    private byte[] GeneratePdf(string content)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(12).FontFamily("Times New Roman"));
                page.Content()
                    .PaddingVertical(20)
                    .Text(content);
            });
        }).GeneratePdf();
    }

    private byte[] GenerateWord(string content)
    {
        using var stream = new MemoryStream();
        var doc = DocX.Create(stream);
        var paragraph = doc.InsertParagraph(content);
        paragraph.Font("Times New Roman").FontSize(12);
        doc.Save();
        return stream.ToArray();
    }

    private string GetSystemPrompt()
    {
        return @"Sen bir akademik tez yazma asistanýsýn. Özelliklerin:
        - Kesinlikle TÜRKÇE cevap ver
        - Akademik ve resmi dil kullan
        - Önceki mesajlarý dikkate al
        - Kaynak öner
        - Baþlýk, özet, giriþ, yöntem, bulgular, tartýþma bölümlerine dikkat et";
    }

    public class DownloadRequest
    {
        public string Content { get; set; }
    }

    public class ChatMessage
    {
        public string Role { get; }
        public string Content { get; }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }
}