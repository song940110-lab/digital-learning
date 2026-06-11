using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WebApplication2.Models;

namespace WebApplication2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TaskTrackerController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _openAiApiKey;

        // 透過依賴注入取得 HttpClientFactory 與 Configuration
        public TaskTrackerController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _openAiApiKey = configuration["OpenAI:ApiKey"];
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> AnalyzeTasks([FromBody] TaskAnalysisRequest request)
        {
            // 1. 基本防呆
            if (string.IsNullOrWhiteSpace(request.PreviousMeetingNotes) ||
                string.IsNullOrWhiteSpace(request.CurrentMeetingNotes))
            {
                return BadRequest("上次與本次會議紀錄不可為空。");
            }

            // 2. 建立 HTTP Request 準備打給 OpenAI
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openAiApiKey}");

            // 設計 System Prompt，明確要求回傳的 JSON 結構
            var systemPrompt = @"你是一個專業的專案管理助手。請比對「上次待辦」與「本次紀錄」，並分析任務最新狀態。
                                 請務必只回傳 JSON 格式，包含 tasks 陣列。
                                 每個物件有 id(數字), title(字串), owner(字串), status(done/pending/overdue), isHighTrack(布林值)。";

            // 組裝 OpenAI 要求的 Payload
            var openAiRequestPayload = new
            {
                model = "gpt-4o-mini",
                response_format = new { type = "json_object" }, // 核心：強制回傳 JSON
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"【上次會議待辦事項】：\n{request.PreviousMeetingNotes}\n\n【本次會議紀錄】：\n{request.CurrentMeetingNotes}" }
                },
                temperature = 0.1 // 降低溫度以獲得更穩定的格式
            };

            var content = new StringContent(
                JsonSerializer.Serialize(openAiRequestPayload),
                Encoding.UTF8,
                "application/json"
            );

            // 3. 發送請求至 OpenAI API
            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"OpenAI API 呼叫失敗: {errorContent}");
            }

            // 4. 解析 OpenAI 的回傳結果
            var responseString = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(responseString);

            // 抓出 GPT 實際回覆的那段 JSON 字串 (在 choices[0].message.content 裡)
            var resultJsonString = jsonDoc.RootElement
                                          .GetProperty("choices")[0]
                                          .GetProperty("message")
                                          .GetProperty("content")
                                          .GetString();

            // 5. 將 GPT 回傳的字串反序列化為我們的 DTO 以確保格式正確，最後回傳給前端
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var resultData = JsonSerializer.Deserialize<TaskAnalysisResponse>(resultJsonString, jsonOptions);

            return Ok(resultData);
        }
    }
}