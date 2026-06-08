using Microsoft.AspNetCore.Http;

namespace WebApplication2.Models
{
    public class MeetingRequest
    {
        // 支援上傳 .txt 逐字稿或常見的音訊格式 (.mp3, .wav, .m4a)
        public IFormFile File { get; set; } = null!;

        // 對應企劃書下拉選單的情境模式 (例如：技術開發、專案規劃)
        public string ScenarioMode { get; set; } = "專案規劃";

        // 新增：讓使用者可以輸入會議中會出現的專有名詞、人名等 (選填)
        public string? Glossary { get; set; }
    }
}
