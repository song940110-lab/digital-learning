using System.Threading.Tasks;

namespace WebApplication2.Services
{
    public interface IAiService
    {
        // 傳入 byte[] 與原始檔名 (讓 Whisper 辨識 .mp3, .wav 等格式)
        Task<string> GenerateTranscriptFromAudioAsync(byte[] audioBytes, string fileName);
        Task<string> GenerateSummaryAsync(string transcript, string scenarioMode, string? glossary = null);
        Task<string> ExtractActionItemsCsvAsync(string transcript, string scenarioMode, string? glossary = null);
    }

    public interface IDocumentService
    {
        // 記憶體渲染引擎：將摘要與待辦清單動態填充至 OpenXML 結構中
        byte[] CreateMeetingReportInBytes(string summary, string actionItemsCsv);

        //Task<string> GenerateSummaryAsync(string transcript, string scenarioMode, string? glossary = null);
        //Task<string> ExtractActionItemsCsvAsync(string transcript, string scenarioMode, string? glossary = null);
    }
}
