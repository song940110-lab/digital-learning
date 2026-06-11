using System.Collections.Generic;

namespace WebApplication2.Models
{
    // 1. 接收前端傳來的兩段會議文字
    public class TaskAnalysisRequest
    {
        public string PreviousMeetingNotes { get; set; }
        public string CurrentMeetingNotes { get; set; }
    }

    // 2. 定義單一任務的結構 (必須與 OpenAI System Prompt 要求的格式一致)
    public class TaskItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Owner { get; set; }
        public string Status { get; set; } // "done", "pending", "overdue"
        public bool IsHighTrack { get; set; }
    }

    // 3. 定義回傳給前端的最終結果
    public class TaskAnalysisResponse
    {
        public List<TaskItem> Tasks { get; set; }
    }
}
