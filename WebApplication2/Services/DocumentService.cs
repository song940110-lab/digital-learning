using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WebApplication2.Services
{
    public class DocumentService : IDocumentService
    {
        // ── 色票 ────────────────────────────────────────────────────────────
        private const string ColorNavy = "1B3A6B";   // 深藍，主標題 / 表格標題列
        private const string ColorBlue = "2E5FA3";   // 中藍，章節標題
        private const string ColorAccent = "3A7BD5";   // 亮藍，項目符號
        private const string ColorLight = "E8EEF7";   // 淡藍，斑馬底色
        private const string ColorWhite = "FFFFFF";
        private const string ColorInk = "1A1A2E";   // 近黑，內文
        private const string ColorGray = "6B7280";   // 灰，副標 / 頁首
        private const string ColorBorder = "C5D0E6";   // 表格框線

        private const string FontCJK = "微軟正黑體";
        private const string FontEN = "Calibri";

        // ── 頁面設定 (A4, 上下左右各 2 cm = 1134 DXA) ───────────────────────
        private const int PageWidth = 11906;
        private const int PageHeight = 16838;
        private const int MarginSize = 1134;
        private const int ContentWidth = PageWidth - MarginSize * 2; // 9638 DXA

        // ── Action Items 表格欄寬（合計 = ContentWidth）────────────────────
        // 預計開始, 預計結束, 工作任務內容, 客戶對象, 工作類別, 負責人
        private static readonly int[] ColWidths = { 1380, 1380, 3298, 940, 940, 800 };

        // ════════════════════════════════════════════════════════════════════
        public byte[] CreateMeetingReportInBytes(string summary, string actionItemsCsv)
        {
            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document();
                var body = new Body();
                main.Document.Append(body);

                AddPageSettings(body);
                AddHeaderFooter(main);

                // ── 主標題 ───────────────────────────────────────────────
                body.Append(MakeSpacer(120));
                body.Append(MakeTitle("AI 智慧會議紀錄自動化生成報告"));
                body.Append(MakeSubtitle($"產生時間：{DateTime.Now:yyyy/MM/dd HH:mm}　|　Powered by Whisper × GPT-4o-mini × OpenXML"));
                body.Append(MakeDivider(ColorNavy, 8));
                body.Append(MakeSpacer(80));

                // ── 摘要區塊（GPT 輸出逐行渲染）──────────────────────────
                RenderSummary(body, summary);

                // ── 分隔 ─────────────────────────────────────────────────
                body.Append(MakeSpacer(160));
                body.Append(MakeDivider(ColorNavy, 6));
                body.Append(MakeSpacer(160));

                // ── Action Items 表格 ─────────────────────────────────────
                body.Append(MakeSectionTitle("結構化待辦事項清單（Action Items）"));
                body.Append(MakeSpacer(80));
                body.Append(BuildActionTable(actionItemsCsv));
                body.Append(MakeSpacer(200));

                // ── 文件結尾標記 ──────────────────────────────────────────
                body.Append(MakeFooterNote($"本報告由 AI 自動生成，請人工審閱確認後使用。生成時間：{DateTime.Now:yyyy/MM/dd HH:mm:ss}"));

                SetDocumentDefaults(main);
                main.Document.Save();
            }
            return ms.ToArray();
        }

        // ════════════════════════════════════════════════════════════════════
        // 摘要渲染：逐行判斷樣式
        // ════════════════════════════════════════════════════════════════════
        private void RenderSummary(Body body, string summary)
        {
            var lines = summary.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int numberCounter = 0;

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();

                // 空行 → 小間距
                if (string.IsNullOrWhiteSpace(line))
                {
                    body.Append(MakeSpacer(60));
                    numberCounter = 0;
                    continue;
                }

                // 章節標題：一、 二、 三、 … 開頭
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[一二三四五六七八九十]+、"))
                {
                    var parts = line.Split(new[] { '、' }, 2);
                    string num = parts[0];
                    string title = parts.Length > 1 ? parts[1].Trim() : "";
                    body.Append(MakeChapterHeading(num, title));
                    numberCounter = 0;
                    continue;
                }

                // 數字條列：1. 2. 3. 開頭
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d+\.\s"))
                {
                    var content = System.Text.RegularExpressions.Regex.Replace(line, @"^\d+\.\s*", "").Trim();
                    numberCounter++;
                    body.Append(MakeNumberedItem(numberCounter, content));
                    continue;
                }

                // 圓點條列：• 或 · 開頭
                if (line.StartsWith("•") || line.StartsWith("·") || line.StartsWith("-"))
                {
                    var content = line.TrimStart('•', '·', '-').Trim();
                    body.Append(MakeBulletItem(content));
                    continue;
                }

                // 一般段落
                body.Append(MakeBodyParagraph(line));
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Action Items 表格
        // ════════════════════════════════════════════════════════════════════
        private Table BuildActionTable(string csv)
        {
            var table = new Table();

            // 表格屬性
            var tblPr = new TableProperties(
                new TableWidth { Width = ContentWidth.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableBorders(
                    MakeBorder<TopBorder>(ColorBorder),
                    MakeBorder<BottomBorder>(ColorBorder),
                    MakeBorder<LeftBorder>(ColorBorder),
                    MakeBorder<RightBorder>(ColorBorder),
                    MakeBorder<InsideHorizontalBorder>(ColorBorder),
                    MakeBorder<InsideVerticalBorder>(ColorBorder)
                )
            );
            table.AppendChild(tblPr);

            // 欄格線
            var tblGrid = new TableGrid();
            foreach (var w in ColWidths)
                tblGrid.Append(new GridColumn { Width = w.ToString() });
            table.AppendChild(tblGrid);

            // 標題列
            table.Append(BuildTableHeaderRow());

            // 資料列
            var rows = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int rowIndex = 0;
            foreach (var rowData in rows)
            {
                var cols = SplitCsvRow(rowData);
                if (cols.Length < 6) continue;

                // 跳過「無,,,,,」這種空行
                if (cols[0].Trim().Equals("無", StringComparison.OrdinalIgnoreCase)
                    && cols.Skip(1).All(c => string.IsNullOrWhiteSpace(c))) continue;

                bool isEven = rowIndex % 2 == 1;
                table.Append(BuildTableDataRow(cols, isEven));
                rowIndex++;
            }

            // 若沒有任何資料列，補一個提示列
            if (rowIndex == 0)
            {
                table.Append(BuildEmptyRow("本次會議無明確行動項目"));
            }

            return table;
        }

        private TableRow BuildTableHeaderRow()
        {
            string[] headers = { "預計開始", "預計結束", "工作任務內容", "客戶對象", "工作類別", "負責人" };
            var row = new TableRow { TableRowProperties = new TableRowProperties(new TableHeader()) };
            for (int i = 0; i < headers.Length; i++)
            {
                row.Append(MakeTableCell(
                    text: headers[i],
                    width: ColWidths[i],
                    fillColor: ColorNavy,
                    textColor: ColorWhite,
                    bold: true,
                    fontSize: 20,
                    align: JustificationValues.Center,
                    fontName: FontCJK
                ));
            }
            return row;
        }

        private TableRow BuildTableDataRow(string[] cols, bool isEven)
        {
            string fill = isEven ? ColorLight : ColorWhite;
            var row = new TableRow();
            for (int i = 0; i < 6; i++)
            {
                string text = i < cols.Length ? cols[i].Trim().Trim('"') : "";
                bool isBold = i == 5; // 負責人欄位加粗
                bool isCenter = i != 2; // 任務內容靠左，其他置中
                string font = (i == 0 || i == 1) ? FontEN : FontCJK;

                row.Append(MakeTableCell(
                    text: text,
                    width: ColWidths[i],
                    fillColor: fill,
                    textColor: ColorInk,
                    bold: isBold,
                    fontSize: i == 2 ? 20 : 18,
                    align: isCenter ? JustificationValues.Center : JustificationValues.Left,
                    fontName: font
                ));
            }
            return row;
        }

        private TableRow BuildEmptyRow(string message)
        {
            var row = new TableRow();
            var cell = new TableCell();
            var cellPr = new TableCellProperties(
                new TableCellWidth { Width = ContentWidth.ToString(), Type = TableWidthUnitValues.Dxa },
                new GridSpan { Val = 6 },
                new Shading { Val = ShadingPatternValues.Clear, Fill = ColorWhite }
            );
            cell.AppendChild(cellPr);
            cell.Append(new Paragraph(new ParagraphProperties(
                new Justification { Val = JustificationValues.Center }
            ),
            new Run(new RunProperties(
                new Color { Val = ColorGray },
                new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                new FontSize { Val = "20" }
            ), new Text(message))));
            row.Append(cell);
            return row;
        }

        // ════════════════════════════════════════════════════════════════════
        // 段落工廠方法
        // ════════════════════════════════════════════════════════════════════

        private Paragraph MakeTitle(string text)
        {
            return new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center },
                    new SpacingBetweenLines { Before = "0", After = "80" }
                ),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                        new FontSize { Val = "40" },
                        new Bold(),
                        new Color { Val = ColorNavy }
                    ),
                    new Text(text)
                )
            );
        }

        private Paragraph MakeSubtitle(string text)
        {
            return new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center },
                    new SpacingBetweenLines { Before = "0", After = "120" }
                ),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                        new FontSize { Val = "18" },
                        new Color { Val = ColorGray }
                    ),
                    new Text(text)
                )
            );
        }

        private Paragraph MakeSectionTitle(string text)
        {
            return new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "160", After = "120" }
                ),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                        new FontSize { Val = "28" },
                        new Bold(),
                        new Color { Val = ColorNavy }
                    ),
                    new Text(text)
                )
            );
        }

        private Paragraph MakeChapterHeading(string num, string title)
        {
            var para = new Paragraph();
            var pPr = new ParagraphProperties(
                new SpacingBetweenLines { Before = "320", After = "120" },
                new Indentation { Left = "200" },
                new ParagraphBorders(new LeftBorder
                {
                    Val = BorderValues.Single,
                    Size = 12,
                    Color = ColorBlue,
                    Space = 8
                })
            );
            para.AppendChild(pPr);

            // 章號 (藍色)
            para.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "26" },
                    new Bold(),
                    new Color { Val = ColorBlue }
                ),
                new Text(num + "、") { Space = SpaceProcessingModeValues.Preserve }
            ));

            // 標題文字 (深藍)
            para.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "26" },
                    new Bold(),
                    new Color { Val = ColorNavy }
                ),
                new Text(title)
            ));

            return para;
        }

        private Paragraph MakeNumberedItem(int n, string text)
        {
            var para = new Paragraph(new ParagraphProperties(
                new Indentation { Left = "400", Hanging = "280" },
                new SpacingBetweenLines { Before = "80", After = "80" }
            ));

            // 數字
            para.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "22" },
                    new Bold(),
                    new Color { Val = ColorBlue }
                ),
                new Text($"{n}. ") { Space = SpaceProcessingModeValues.Preserve }
            ));

            // 內文（支援「發言者：X ／ 立場：Y」格式加粗標籤）
            AppendFormattedText(para, text);

            return para;
        }

        private Paragraph MakeBulletItem(string text)
        {
            var para = new Paragraph(new ParagraphProperties(
                new Indentation { Left = "360", Hanging = "240" },
                new SpacingBetweenLines { Before = "60", After = "60" }
            ));

            // 圓點
            para.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "22" },
                    new Color { Val = ColorAccent }
                ),
                new Text("• ") { Space = SpaceProcessingModeValues.Preserve }
            ));

            // 內文（支援「負責人：X ／ 截止：Y」格式）
            AppendFormattedText(para, text);

            return para;
        }

        private Paragraph MakeBodyParagraph(string text)
        {
            return new Paragraph(
                new ParagraphProperties(
                    new Indentation { Left = "200" },
                    new SpacingBetweenLines { Before = "60", After = "60" }
                ),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                        new FontSize { Val = "22" },
                        new Color { Val = ColorInk }
                    ),
                    new Text(text) { Space = SpaceProcessingModeValues.Preserve }
                )
            );
        }

        private Paragraph MakeFooterNote(string text)
        {
            return new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center },
                    new SpacingBetweenLines { Before = "120", After = "0" }
                ),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                        new FontSize { Val = "16" },
                        new Italic(),
                        new Color { Val = ColorGray }
                    ),
                    new Text(text)
                )
            );
        }

        private Paragraph MakeDivider(string color, uint size = 4)
        {
            return new Paragraph(
                new ParagraphProperties(
                    new ParagraphBorders(new BottomBorder
                    {
                        Val = BorderValues.Single,
                        Size = size,
                        Color = color,
                        Space = 1
                    }),
                    new SpacingBetweenLines { Before = "0", After = "120" }
                )
            );
        }

        private Paragraph MakeSpacer(int twips = 120)
        {
            return new Paragraph(new ParagraphProperties(
                new SpacingBetweenLines { Before = "0", After = twips.ToString() }
            ));
        }

        // ════════════════════════════════════════════════════════════════════
        // 格式化文字：把「／」分隔的片段，標籤部分加粗
        // 例：任務內容 ／ 負責人：陳同學 ／ 截止：2026/06/12
        // ════════════════════════════════════════════════════════════════════
        private void AppendFormattedText(Paragraph para, string text)
        {
            var segments = text.Split(new[] { "／" }, StringSplitOptions.None);
            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i].Trim();
                if (i > 0)
                {
                    // 分隔符
                    para.Append(new Run(
                        new RunProperties(
                            new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                            new FontSize { Val = "22" },
                            new Color { Val = ColorGray }
                        ),
                        new Text("  ／  ") { Space = SpaceProcessingModeValues.Preserve }
                    ));
                }

                var colonIdx = seg.IndexOf('：');
                if (colonIdx > 0 && i > 0)
                {
                    // 標籤（加粗藍色）
                    string label = seg.Substring(0, colonIdx + 1);
                    string value = seg.Substring(colonIdx + 1);

                    para.Append(new Run(
                        new RunProperties(
                            new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                            new FontSize { Val = "22" },
                            new Bold(),
                            new Color { Val = ColorBlue }
                        ),
                        new Text(label) { Space = SpaceProcessingModeValues.Preserve }
                    ));
                    para.Append(new Run(
                        new RunProperties(
                            new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                            new FontSize { Val = "22" },
                            new Color { Val = ColorInk }
                        ),
                        new Text(value) { Space = SpaceProcessingModeValues.Preserve }
                    ));
                }
                else
                {
                    // 一般文字
                    para.Append(new Run(
                        new RunProperties(
                            new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                            new FontSize { Val = "22" },
                            new Color { Val = ColorInk }
                        ),
                        new Text(seg) { Space = SpaceProcessingModeValues.Preserve }
                    ));
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 表格 cell 工廠
        // ════════════════════════════════════════════════════════════════════
        private TableCell MakeTableCell(
            string text, int width, string fillColor, string textColor,
            bool bold, int fontSize, JustificationValues align, string fontName)
        {
            var cell = new TableCell();

            var cellPr = new TableCellProperties(
                new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa },
                new Shading { Val = ShadingPatternValues.Clear, Fill = fillColor },
                new TableCellMargin(
                    new TopMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                    new LeftMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                    new RightMargin { Width = "120", Type = TableWidthUnitValues.Dxa }
                ),
                new VerticalTextAlignmentOnPage { Val = VerticalJustificationValues.Center }
            );
            cell.AppendChild(cellPr);

            var rPr = new RunProperties(
                new RunFonts { Ascii = FontEN, EastAsia = fontName },
                new FontSize { Val = fontSize.ToString() },
                new Color { Val = textColor }
            );
            if (bold) rPr.AppendChild(new Bold());

            cell.Append(new Paragraph(
                new ParagraphProperties(new Justification { Val = align }),
                new Run(rPr, new Text(text) { Space = SpaceProcessingModeValues.Preserve })
            ));

            return cell;
        }

        // ════════════════════════════════════════════════════════════════════
        // 頁面設定
        // ════════════════════════════════════════════════════════════════════
        private void AddPageSettings(Body body)
        {
            var sectPr = new SectionProperties(
                new PageSize { Width = (UInt32Value)(uint)PageWidth, Height = (UInt32Value)(uint)PageHeight },
                new PageMargin
                {
                    Top = MarginSize,
                    Bottom = MarginSize,
                    Left = (UInt32Value)(uint)MarginSize,
                    Right = (UInt32Value)(uint)MarginSize,
                    Header = 600,
                    Footer = 600
                }
            );
            body.AppendChild(sectPr);
        }

        private void AddHeaderFooter(MainDocumentPart main)
        {
            // ── 頁首 ──────────────────────────────────────────────────────
            var headerPart = main.AddNewPart<HeaderPart>();
            var header = new Header();

            var hPara = new Paragraph(
                new ParagraphProperties(
                    new ParagraphBorders(new BottomBorder
                    {
                        Val = BorderValues.Single,
                        Size = 6,
                        Color = ColorNavy,
                        Space = 1
                    }),
                    new SpacingBetweenLines { After = "0" }
                )
            );
            hPara.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "18" },
                    new Bold(),
                    new Color { Val = ColorNavy }
                ),
                new Text("AI 智慧會議紀錄報告") { Space = SpaceProcessingModeValues.Preserve }
            ));
            hPara.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "16" },
                    new Color { Val = ColorGray }
                ),
                new Text($"　　　　　　　　　　　　　　　　　　　　　　產生時間：{DateTime.Now:yyyy/MM/dd HH:mm}")
            ));
            header.Append(hPara);
            headerPart.Header = header;
            headerPart.Header.Save();

            // ── 頁尾 ──────────────────────────────────────────────────────
            var footerPart = main.AddNewPart<FooterPart>();
            var footer = new Footer();

            var fPara = new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = JustificationValues.Right },
                    new ParagraphBorders(new TopBorder
                    {
                        Val = BorderValues.Single,
                        Size = 4,
                        Color = ColorBorder,
                        Space = 1
                    }),
                    new SpacingBetweenLines { Before = "80" }
                )
            );
            fPara.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK }, 
                    new FontSize { Val = "16" }, 
                    new Color { Val = ColorGray }),
                new Text("第 ") { Space = SpaceProcessingModeValues.Preserve }
            ));
            fPara.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "16" },
                    new Color { Val = ColorGray }
                ),
                new FieldChar { FieldCharType = FieldCharValues.Begin }
            ));
            fPara.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "16" },
                    new Color { Val = ColorGray }
                ),
                new FieldCode { Text = "PAGE" }
            ));
            fPara.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "16" },
                    new Color { Val = ColorGray }
                ),
                new FieldChar { FieldCharType = FieldCharValues.Separate }
            ));
            fPara.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "16" },
                    new Color { Val = ColorGray }
                ),
                new Text("1") { Space = SpaceProcessingModeValues.Preserve }
            ));
            fPara.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "16" },
                    new Color { Val = ColorGray }
                ),
                new FieldChar { FieldCharType = FieldCharValues.End }
            ));
            fPara.Append(new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontEN, EastAsia = FontCJK },
                    new FontSize { Val = "16" },
                    new Color { Val = ColorGray }
                ),
                new Text(" 頁") { Space = SpaceProcessingModeValues.Preserve }
            ));
            footer.Append(fPara);
            footerPart.Footer = footer;
            footerPart.Footer.Save();

            // 綁定到 SectionProperties
            var sectPr = main.Document.Body!.GetFirstChild<SectionProperties>()
                         ?? new SectionProperties();

            string headerId = main.GetIdOfPart(headerPart);
            string footerId = main.GetIdOfPart(footerPart);

            sectPr.PrependChild(new FooterReference { Type = HeaderFooterValues.Default, Id = footerId });
            sectPr.PrependChild(new HeaderReference { Type = HeaderFooterValues.Default, Id = headerId });
        }

        private void SetDocumentDefaults(MainDocumentPart main)
        {
            var settings = main.AddNewPart<DocumentSettingsPart>();
            settings.Settings = new Settings(
                new DefaultTabStop { Val = 720 }
            );
            settings.Settings.Save();
        }

        // ════════════════════════════════════════════════════════════════════
        // CSV 解析（處理欄位內有逗號的情況）
        // ════════════════════════════════════════════════════════════════════
        private string[] SplitCsvRow(string row)
        {
            var result = new System.Collections.Generic.List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in row)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        // ════════════════════════════════════════════════════════════════════
        // 泛型 border helper
        // ════════════════════════════════════════════════════════════════════
        private T MakeBorder<T>(string color) where T : BorderType, new()
        {
            return new T
            {
                Val = BorderValues.Single,
                Size = 1,
                Color = color
            };
        }
    }
}