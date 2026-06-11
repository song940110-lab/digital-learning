using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using WebApplication2.Models;
using WebApplication2.Services;

namespace WebApplication2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MeetingController : ControllerBase
    {
        private readonly IAiService _aiService;
        private readonly IDocumentService _documentService;
        private readonly ILogger<MeetingController> _logger;

        public MeetingController(IAiService aiService, IDocumentService documentService, ILogger<MeetingController> logger)
        {
            _aiService = aiService;
            _documentService = documentService;
            _logger = logger;
        }

        [HttpPost("process")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ProcessMeetingAsync([FromForm] MeetingRequest request)
        {
            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new { Message = "請提供有效的會議文字檔或音訊檔案。" });
            }

            try
            {
                _logger.LogInformation("--- 接收到會議紀錄處理請求 (情境: {Mode}) ---", request.ScenarioMode);

                using var memoryStream = new MemoryStream();
                await request.File.CopyToAsync(memoryStream);
                byte[] fileBytes = memoryStream.ToArray();

                string transcript;
                string fileName = request.File.FileName.ToLower();
                string contentType = request.File.ContentType;

                if (fileName.EndsWith(".txt"))
                {
                    transcript = System.Text.Encoding.UTF8.GetString(fileBytes);
                }
                else if (contentType.Contains("audio") || contentType.Contains("video") ||
                         fileName.EndsWith(".mp3") || fileName.EndsWith(".wav") || fileName.EndsWith(".m4a"))
                {
                    _logger.LogInformation("偵測到音訊輸入，自動觸發 OpenAI Whisper 模型轉譯...");
                    transcript = await _aiService.GenerateTranscriptFromAudioAsync(fileBytes, request.File.FileName);
                }
                else
                {
                    return BadRequest(new { Message = "不支援的檔案格式。請上傳 .txt 逐字稿或常見會議影音檔。" });
                }

                if (string.IsNullOrWhiteSpace(transcript))
                {
                    return BadRequest(new { Message = "無法從上傳的檔案中解析出有效的會議文字內容。" });
                }

                _logger.LogInformation("啟動 GPT-4o-mini 推理引擎進行雙軌摘要擷取與任務提取...");
                var summaryTask = _aiService.GenerateSummaryAsync(transcript, request.ScenarioMode, request.Glossary);
                var actionItemsTask = _aiService.ExtractActionItemsCsvAsync(transcript, request.ScenarioMode, request.Glossary);

                await Task.WhenAll(summaryTask, actionItemsTask);

                string summary = await summaryTask;
                string actionItemsCsv = await actionItemsTask;

                _logger.LogInformation("正在動態渲染結構化 OpenXML 文件...");
                byte[] docxBytes = _documentService.CreateMeetingReportInBytes(summary, actionItemsCsv);

                string downloadName = $"AI_會議紀錄報告_{DateTime.Now:yyyyMMdd_HHmmss}.docx";
                _logger.LogInformation("處理完成，輸出二進位文件串流。");

                return File(
                    fileContents: docxBytes,
                    contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    fileDownloadName: downloadName
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行會議記錄自動化生成管線時發生錯誤。");
                return StatusCode(500, new { Message = $"伺服器內部處理失敗: {ex.Message}" });
            }
        }

        [HttpPost("AnalyzeRisk")]
        public async Task<IActionResult> AnalyzeRiskAsync([FromBody] RiskAnalysisRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Content))
            {
                return BadRequest(new { Message = "請提供有效的會議摘要與任務狀態內容。" });
            }

            try
            {
                _logger.LogInformation("--- 接收到風險分析請求 ---");

                string jsonResult = await _aiService.AnalyzeRiskAsync(request.Content);

                // 直接回傳 JSON 字串給前端
                return Content(jsonResult, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行風險分析時發生錯誤。");
                return StatusCode(500, new { Message = $"伺服器內部處理失敗: {ex.Message}" });
            }
        }

        //  新增 API：通知生成端點
        [HttpPost("GenerateNotification")]
        public async Task<IActionResult> GenerateNotificationAsync([FromBody] NotificationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Summary))
            {
                return BadRequest(new { Message = "請提供有效的會議摘要內容。" });
            }

            try
            {
                _logger.LogInformation("--- 接收到會議通知生成請求 ---");

                string jsonResult = await _aiService.GenerateNotificationAsync(request.Summary);

                // 直接回傳 JSON 字串給前端
                return Content(jsonResult, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行會議通知生成時發生錯誤。");
                return StatusCode(500, new { Message = $"伺服器內部處理失敗: {ex.Message}" });
            }
        }
    }
}
