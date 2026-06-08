using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace WebApplication2.Services
{
    public class OpenAiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string ChatModel = "gpt-4o-mini";

        public OpenAiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["OpenAI:ApiKey"] ?? throw new ArgumentNullException("未在設定檔中找到 OpenAI:ApiKey");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<string> GenerateTranscriptFromAudioAsync(byte[] audioBytes, string fileName)
        {
            string endpoint = "https://api.openai.com/v1/audio/transcriptions";

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(audioBytes);
            content.Add(fileContent, "file", fileName);
            content.Add(new StringContent("whisper-1"), "model");
            content.Add(new StringContent("text"), "response_format");

            var response = await _httpClient.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GenerateSummaryAsync(string transcript, string scenarioMode)
        {
            string todayStr = DateTime.Now.ToString("yyyy/MM/dd");
            // 新增：把明年的年份也算出來給它
            string nextYear = DateTime.Now.AddYears(1).ToString("yyyy");

            string systemPrompt = $@"
你是一個具備高適應力的專業會議助理。目前的會議情境與組織類型為：【{scenarioMode}】。
本次會議召開的當天日期為：【{todayStr}】。
請根據使用者提供的會議逐字稿，嚴格依據文件內容進行分析（絕不加入任何外推或虛構情節），並依據以下結構整理。

【重要排版與邏輯規範】：
1. 嚴格禁止使用任何 Markdown 區塊包裝符號（如 ``` 或 ```markdown）。
2. 主要標題請嚴格使用「一、」、「二、」等中文數字獨立成行。
3. 項目請使用 1., 2., 3. 或圓點（•）條列，並確保每個項目獨立成行，不可黏在一起。
4. 請根據【{scenarioMode}】的身分動態調整稱謂（例如：企業情境使用「經理/部門」；師生情境使用「教授/同學/研究室」）。
5. 當文中提到模糊日期時，請結合當天日期【{todayStr}】推算完整西元年（格式：yyyy/MM/dd）。
   - 【跨年防呆機制】：若提及的月份與日期「早於」今天（{todayStr}），請判斷為明年的任務，自動將年份推算為明年（{nextYear}）。例如：今天是 6月8日，若提及「5月30日」，務必輸出 '{nextYear}/05/30'。
6. 請直接輸出文字即可，絕對不要使用中括號「[]」來包裝任何欄位或內容。

---

一、 會議核心目的
請根據會議背景，用一句話精煉說明本次會議的主旨與最終目標

二、 關鍵討論觀點與各方立場
條列式列出 3-5 個重要論點。請務必精準標記發言者的姓名/身分/部門，並概述其核心理由。
※ 企業範例：1. 觀點：內容 / 提出者：王大明經理/業務部 / 理由：內容
※ 師生範例：1. 觀點：內容 / 提出者：林教授 或 陳同學/資工所 / 理由：內容

三、 最終決策事項
明確列出會議中拍板定案的項目。若無，請標記「本次會議未做出最終決策」

四、 待辦事項清單 (Action Items)
請使用以下格式條列。若文中未提及負責人或期限，請填寫「待確認」：
• 任務內容：具體交辦事項 / 負責人：姓名/身分 / 截止日期：年/月/日或具體時間點

五、 延伸待討論事項
條列出會議中被提起、有爭議、但未解決，或決議留待下次會議深入討論的議題

六、 續行會議建議議程
根據上述未解決事項或後續延伸任務，為下一次會議規劃 2-3 個建議討論的具體議題
";

            var requestBody = CreateChatPayload(systemPrompt, transcript, 0.2f);
            string summaryResult = await SendChatCompletionAsync(requestBody);

            // 暴力淨化：雙重確保絕對不會有中括號出現
            return summaryResult.Replace("[", "").Replace("]", "");
        }

        public async Task<string> ExtractActionItemsCsvAsync(string transcript, string scenarioMode)
        {
            string todayStr = DateTime.Now.ToString("yyyy/MM/dd");
            string currentYear = DateTime.Now.ToString("yyyy");
            string nextYear = DateTime.Now.AddYears(1).ToString("yyyy"); // 抓出明年的年份備用

            string systemPrompt = $@"
你是一個專業的會議數據分析助理。目前的會議情境為：【{scenarioMode}】。
本次會議召開的當天日期為：【{todayStr}】。

請從使用者提供的會議逐字稿中，僅針對「真正需要被執行的交辦任務（Action Items）」進行擷取。
請將每一項待辦事項嚴格轉換為 CSV 格式，不包含標頭，並使用半形逗號作為唯一分隔符。
每一行的欄位順序必須嚴格按照：startTime,endTime,content,customer,category,owner

【欄位填充與時間軸動態推算規則（極重要）】：
1. startTime：一律固定填入會議當天日期與時間，格式為 '{todayStr} 09:00'。
2. endTime：請嚴格根據對話內容提到的截止日期進行動態推算，格式為 'yyyy/MM/dd 18:00'。
   - 【跨年防呆機制】：若對話中提到的月份與日期「早於」今天（{todayStr}），請判斷為明年的任務，並自動將年份推算為明年（{nextYear}）。例如：今天是 6月8日，若提及「5月30日」，務必輸出 '{nextYear}/05/30 18:00'，絕不能讓結束時間早於開始時間。
3. content：任務內容。請估算目前的完成度百分比，但【絕對不要使用中括號】。請以「進度0%：」的純文字格式放在內容開頭。例如：進度0%：修改第三章的實驗設計架構圖。
4. customer：若為企業情境，優先提取提及的客戶，若無則預設為「小黃」。若為師生/學術情境，請一律填寫為「無」。
5. category（工作類別）：請依據【{scenarioMode}】進行情境分流：
   - 若為企業情境：請從「電話」、「傳真」、「郵件」、「約會」中選擇，若無法判斷則預設為「郵件」。
   - 若為師生/學術情境：請從「開發」、「實驗」、「撰寫」、「討論」中選擇，若無法判斷則預設為「專案」。
6. owner：嚴格提取真正的負責人（如：陳同學），完全找不到時才填寫「待確認」。

【嚴格防錯約束】：
- 絕對不可將「延伸待討論事項」誤當成個人待辦事項。
- 只輸出 CSV 格式的純文字結果，絕不能包含任何 markdown 語法（如 ```csv）。
- 絕對不要在任何欄位使用中括號「[]」。
";

            var requestBody = CreateChatPayload(systemPrompt, transcript, 0.1f);
            string rawResult = await SendChatCompletionAsync(requestBody);
            return CleanMarkdownWrapper(rawResult);
        }

        private object CreateChatPayload(string systemMessage, string userMessage, float temperature)
        {
            return new
            {
                model = ChatModel,
                messages = new[]
                {
                    new { role = "system", content = systemMessage },
                    new { role = "user", content = userMessage }
                },
                temperature = temperature,
                max_tokens = 2048
            };
        }

        private async Task<string> SendChatCompletionAsync(object requestBody)
        {
            string endpoint = "https://api.openai.com/v1/chat/completions";
            string json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            string responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            try
            {
                return doc.RootElement.GetProperty("choices")[0]
                                      .GetProperty("message")
                                      .GetProperty("content")
                                      .GetString()?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new Exception("解析 OpenAI 傳回之 JSON 節點時發生異常。", ex);
            }
        }

        private string CleanMarkdownWrapper(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string trimmed = input.Trim();
            if (trimmed.StartsWith("```"))
            {
                var lines = trimmed.Split('\n');
                var sb = new StringBuilder();
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("```")) continue;
                    sb.AppendLine(line);
                }
                return sb.ToString().Trim();
            }
            return trimmed;
        }
    }
}
