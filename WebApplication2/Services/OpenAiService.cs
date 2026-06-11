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

        public async Task<string> GenerateSummaryAsync(string transcript, string scenarioMode, string? glossary = null)
        {
            string todayStr = DateTime.Now.ToString("yyyy/MM/dd");
            string dayOfWeek = DateTime.Now.ToString("dddd", new System.Globalization.CultureInfo("zh-TW"));
            // 新增：把明年的年份也算出來給它
            string nextYear = DateTime.Now.AddYears(1).ToString("yyyy");

            string glossaryPrompt = string.IsNullOrWhiteSpace(glossary) ? ""
        : $"重要背景知識 (RAG 增強)：本次會議可能會頻繁出現以下專有名詞、人名或術語，請優先使用這些詞彙進行語意辨識與校正：{glossary}\n";

            string systemPrompt = $@"
你是一位嚴謹的專業會議記錄員，專精於【{scenarioMode}】類型的會議。
今天日期：{todayStr} （{dayOfWeek}）。

【第一步：內容有效性檢核】
先判斷輸入是否為真實會議內容。
若為雜音、無意義內容、或字數低於50字且無具體討論，請只回傳這句話，不要加任何其他內容：
「系統攔截：輸入內容非有效會議紀錄，無法生成報告。」

【第二步：生成會議紀錄】
檢核通過後，依以下六個章節輸出。
使用中文數字標題，不使用任何 Markdown 符號（禁用 **、```、# 等）。

一、會議核心目的
用一句話說明本次會議的主要目標與背景。

二、關鍵討論觀點
條列3至5個重要論點，每點須標明發言者姓名或身分及其立場。
格式：觀點內容 ／ 發言者：姓名或職稱 

三、最終決策事項
只列出會議中明確拍板、有結論的決定，不列討論中的提案。
若無，填寫：本次會議未做出最終決策。
凡有明確負責人的項目，必須全部在此章節再列一次，一筆都不能省略。

四、待辦事項清單
只列出有明確「人」被指派執行、或說話者自己主動承諾執行的任務。
不列討論性議題、不列假設情境、不列「也許可以考慮」。
格式：任務內容 ／ 負責人：姓名 ／ 截止：具體日期（若未提及填寫待確認）
即使該任務已列入第三章決策事項，只要有負責人，仍須在此重複列出，不得省略。
說話者自己說「我會去做」或「我有空再處理」也算待辦，截止填待確認，不得省略。

【截止日期推算規則】：
提及「下週X」，今天是 {todayStr}（{dayOfWeek}），
計算方式：先找這週該星期X的日期，再加7天。
例：今天星期二（{todayStr}），「下週四」= 這週四 {DateTime.Now.AddDays(2):yyyy/MM/dd} 再加7天 = {DateTime.Now.AddDays(9):yyyy/MM/dd}。

五、延伸待討論事項
列出被提起但未解決、有爭議、或明確說「下次再討論」的議題。
此區塊嚴禁出現任務指派或負責人，純粹是未完成議題的羅列。

六、下次會議建議議程
根據第五點的未解決事項，提出2至3個具體的下次會議討論方向。

【全域輸出規範】
- 禁止使用中括號「[]」
- 禁止使用 Markdown 程式碼區塊或任何粗體符號
- 日期一律使用 yyyy/MM/dd 格式
- 若某章節在逐字稿中完全無對應內容，標題保留，內容填：本次會議無相關內容
- 所有內容嚴格來自逐字稿，不得推測或捏造
- 稱謂依情境調整：{(scenarioMode.Contains("企業") ? "使用職稱如經理、主任、業務等" : "使用身分如教授、同學、研究生等")}
{glossaryPrompt}";

            var requestBody = CreateChatPayload(systemPrompt, transcript, 0.0f);
            string summaryResult = await SendChatCompletionAsync(requestBody);

            // 暴力淨化：雙重確保絕對不會有中括號出現
            return summaryResult.Replace("[", "").Replace("]", "");
        }

        public async Task<string> ExtractActionItemsCsvAsync(string transcript, string scenarioMode, string? glossary = null)
        {
            string todayStr = DateTime.Now.ToString("yyyy/MM/dd");
            string currentYear = DateTime.Now.ToString("yyyy");
            string nextYear = DateTime.Now.AddYears(1).ToString("yyyy"); // 抓出明年的年份備用
            string dayOfWeek = DateTime.Now.ToString("dddd", new System.Globalization.CultureInfo("zh-TW"));
            string glossaryPrompt = string.IsNullOrWhiteSpace(glossary) ? ""
: $"重要背景知識 (RAG 增強)：本次會議可能會頻繁出現以下專有名詞、人名或術語，請優先使用這些詞彙進行語意辨識與校正：{glossary}\n";


            string systemPrompt = $@"
你是專業的會議數據萃取引擎，負責從逐字稿中精準識別行動項目並輸出結構化 CSV。
今天日期：{todayStr}。會議情境：【{scenarioMode}】。
{glossaryPrompt}

【最高優先規則：截止日期跨年檢查】
在填入任何 endTime 之前，必須先執行此檢查：
將提及的月份日期與今天 {todayStr} 比較。
若該日期已過（月份早於今天，或同月但日期已過），年份一律填入 {nextYear}。
此規則優先於所有其他規則，無任何例外。

【Action Item 判斷標準：必須同時符合以下其中一項】
A. 某人被明確指派執行一項具體任務
B. 說話者自己主動承諾「我會去做某件事」
排除條件：討論性議題、假設情境、「也許可以考慮」、「有空再說」類的模糊表達。
注意：「有空看完再跟你講」雖然模糊，但仍是承諾，應列入，截止填待確認。

【輸出格式】
只輸出 CSV 純文字，無標頭，無說明文字，欄位順序固定：
startTime,endTime,content,customer,category,owner

【各欄位規則】

startTime：固定填入 {todayStr} 09:00

endTime 推算步驟（必須依序執行，不可跳過）：

步驟一：判斷逐字稿是否有提到截止時間或相對時間。
若完全沒提到 → 直接填「待確認」，結束，不執行後續步驟。

步驟二：若提到「下週X（星期X）」，今天是 {todayStr}（{dayOfWeek}），
請計算從今天往後找到下一個該星期X的日期，再加7天（因為是「下週」不是「這週」）。
例：今天是星期二（{todayStr}），「下週四」= 這週四再加7天 = {DateTime.Now.AddDays(9):yyyy/MM/dd}，格式 yyyy/MM/dd 18:00。

步驟三：若提到具體月份日期（如「5月30日」），
先將年份設為今年 {DateTime.Now.Year}，組成完整日期。
再與今天 {todayStr} 比較：
  - 若該日期 >= 今天 → 直接使用今年，格式 yyyy/MM/dd 18:00。
  - 若該日期 < 今天 → 年份改為明年 {nextYear}，格式 yyyy/MM/dd 18:00。

步驟四：每筆 endTime 必須 >= startTime（{todayStr} 09:00），若計算結果早於今天則強制跳到明年。

content：任務的具體描述，以動詞開頭，簡潔明確，不加進度估算，不使用中括號。

customer：
企業情境 → 填任務相關的客戶名稱，若未提及填「內部」。
師生學術情境 → 一律填「無」。

category（此欄位極重要，請嚴格按照以下對照表填寫）：

師生學術情境對照表：
- 「開發」：凡涉及寫程式、測試系統、跑實驗、Debug、Alpha測試、Beta測試 → 一律填「開發」
- 「實驗」：凡涉及數據收集、模型訓練、修改架構圖、跑模擬 → 一律填「實驗」
- 「撰寫」：凡涉及寫報告、整理文件、寫規範、修改論文章節 → 一律填「撰寫」
- 「討論」：凡涉及開會、審查、回報進度、確認事項 → 一律填「討論」

判斷邏輯：先看任務動詞，再對照上表。「Alpha 測試」動詞是「測試」→ 開發。
禁止填「其他」「文件」「郵件」等不在上表的詞，師生情境只能填以上四個。

owner：提取被指派者或承諾者的姓名或身分（如：王經理、陳同學）。
每一行的 owner 必須獨立從該任務的上下文重新判斷，禁止沿用上一行的負責人。
若同一任務多人負責，每人獨立輸出一行（其他欄位相同）。
完全找不到負責人才填「待確認」。

【嚴格禁止】
- 禁止把「延伸待討論事項」列為 Action Item
- 禁止輸出任何 Markdown 語法或說明文字
- 禁止使用中括號
- 同一任務若在逐字稿中被提及多次，只輸出一行
- 若完全沒有符合條件的 Action Item，只輸出一行：無,,,,, 
";

            var requestBody = CreateChatPayload(systemPrompt, transcript, 0.0f);
            string rawResult = await SendChatCompletionAsync(requestBody);
            return CleanMarkdownWrapper(rawResult);
        }

        public async Task<string> AnalyzeRiskAsync(string content)
        {
            string systemPrompt = @"
你是一個專業的專案風險分析 AI。請分析使用者提供的會議摘要與任務狀態。
你必須以 JSON 格式回應，且格式必須為包含 'risks' 陣列的物件：
{
  ""risks"": [
    {
      ""icon"": ""⚠️"",
      ""type"": ""進度風險|責任風險|溝通風險|期限風險"",
      ""title"": ""風險標題 (20字內)"",
      ""desc"": ""詳細描述 (50字內)"",
      ""suggestion"": ""改善建議 (40字內)"",
      ""level"": ""high|mid|low"",
      ""score"": 0到100的分數
    }
  ]
}
請絕對不要輸出任何非 JSON 的文字。";

            // 注意這裡傳入 true 啟用 JSON 模式
            var requestBody = CreateChatPayload(systemPrompt, content, 0.3f, true);
            return await SendChatCompletionAsync(requestBody);
        }

        // 新增：通知生成實作
        public async Task<string> GenerateNotificationAsync(string summary)
        {
            string systemPrompt = @"
你是會議通知撰寫 AI，使用繁體中文，文字活潑清楚。
根據會議摘要，生成三種格式的通知文字。
你必須以 JSON 格式回應，格式如下：
{
  ""group"": ""LINE/Slack 群組通知版（簡短活潑，可用 emoji）"",
  ""email"": ""Email 正文（正式完整，包含問候語、摘要、待辦事項、結語）"",
  ""remind"": ""個人任務提醒（逐人列出任務與截止日，格式化條列）"",
  ""subject"": ""Email 主旨""
}
請絕對不要輸出任何非 JSON 的文字。";

            // 通知生成需要創意，temperature 設為 0.7f，傳入 true 啟用 JSON 模式
            var requestBody = CreateChatPayload(systemPrompt, summary, 0.7f, true);
            return await SendChatCompletionAsync(requestBody);
        }

        private object CreateChatPayload(string systemMessage, string userMessage, float temperature, bool isJsonFormat = false)
        {
            if (isJsonFormat)
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
                    max_tokens = 2048,
                    response_format = new { type = "json_object" } // 強制啟用 JSON 模式
                };
            }

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
