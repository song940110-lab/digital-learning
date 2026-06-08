using System;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WebApplication2.Services
{
    public class DocumentService : IDocumentService
    {
        public byte[] CreateMeetingReportInBytes(string summary, string actionItemsCsv)
        {
            using var memoryStream = new MemoryStream();

            using (var wordDoc = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document))
            {
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new Document();
                var body = new Body();
                mainPart.Document.Append(body);

                // 標題
                var titlePara = new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));
                var titleRun = new Run(new Text("AI 智慧會議紀錄自動化生成報告"));
                titleRun.RunProperties = new RunProperties(new FontSize { Val = "36" }, new Bold(), new RunFonts { Hint = FontTypeHintValues.EastAsia, Ascii = "Arial" });
                titlePara.Append(titleRun);
                body.Append(titlePara);
                body.Append(new Paragraph(new Run(new Break())));

                // 摘要區塊
                var h1Summary = new Paragraph();
                var h1SummaryRun = new Run(new Text("一、 會議核心決議與摘要"));
                h1SummaryRun.RunProperties = new RunProperties(new FontSize { Val = "28" }, new Bold());
                h1Summary.Append(h1SummaryRun);
                body.Append(h1Summary);

                // --- 💡 修正後的核心處理區塊 ----------------------------------------
                var summaryPara = new Paragraph();
                var summaryRun = new Run();
                summaryRun.RunProperties = new RunProperties(new FontSize { Val = "24" });

                // 將 OpenAI 回傳的文字，依照換行符號切割（相容 \r\n 與 \n）
                string[] summaryLines = summary.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                for (int i = 0; i < summaryLines.Length; i++)
                {
                    // 寫入當前行的文字
                    summaryRun.Append(new Text(summaryLines[i]));

                    // 如果不是最後一行，就手動在文字後面補一個 Word 的換行符（Break）
                    if (i < summaryLines.Length - 1)
                    {
                        summaryRun.Append(new Break());
                    }
                }

                summaryPara.Append(summaryRun);
                body.Append(summaryPara);
                // ------------------------------------------------------------------

                body.Append(new Paragraph(new Run(new Break())));


                // 待辦事項區塊
                var h1Tasks = new Paragraph();
                var h1TasksRun = new Run(new Text("二、 結構化待辦事項清單 (Action Items)"));
                h1TasksRun.RunProperties = new RunProperties(new FontSize { Val = "28" }, new Bold());
                h1Tasks.Append(h1TasksRun);
                body.Append(h1Tasks);

                var table = new Table();
                var tableProps = new TableProperties(
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                        new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                        new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                        new RightBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E0E0E0" },
                        new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E0E0E0" }
                    )
                );
                table.AppendChild(tableProps);

                var headerRow = new TableRow();
                string[] headers = { "預計開始", "預計結束", "工作任務內容", "客戶對象", "工作類別", "負責人" };
                foreach (var h in headers)
                {
                    var cell = new TableCell(new Paragraph(new Run(new Text(h)) { RunProperties = new RunProperties(new Bold()) }));
                    var cellProps = new TableCellProperties(new Shading { Val = ShadingPatternValues.Clear, Color = "Auto", Fill = "F2F2F2" });
                    cell.AppendChild(cellProps);
                    headerRow.Append(cell);
                }
                table.Append(headerRow);

                var rows = actionItemsCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var rowData in rows)
                {
                    var columns = rowData.Split(',');
                    if (columns.Length >= 6)
                    {
                        var row = new TableRow();
                        for (int i = 0; i < 6; i++)
                        {
                            string cellText = columns[i].Trim().Replace("\"", "");
                            row.Append(new TableCell(new Paragraph(new Run(new Text(cellText)))));
                        }
                        table.Append(row);
                    }
                }

                body.Append(table);
                mainPart.Document.Save();
            }

            return memoryStream.ToArray();
        }
    }
}
