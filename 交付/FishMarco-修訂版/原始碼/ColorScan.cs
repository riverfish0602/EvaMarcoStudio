using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// 顏色掃描小工具
//
// 螢幕上放一個全透明、可拖曳可縮放的方塊，定時掃描方塊內的顏色，
// 把落在容差範圍內的像素畫成高亮色塊（可閃爍），用來盯住畫面上某個顏色。
//
// 檔案結構：
//   ColorRule／ColorScanner  色碼解析、對比色計算與純粹的像素比對，不碰 UI，可單獨測試
//   ScanOverlay              螢幕上那個方塊：兩種模式、定時掃描、繪製高亮
//   ColorBubble／ColorPicker 吸色：跟著游標的小色票，以及等待點擊的全域鉤子
//   ColorRuleRow             一列色碼組（目標色碼／容差；高亮色自動算）
//   ScanProfile／ScanSession 設定的驗證與自動保存
//   ScanStudioForm           工具視窗，把上面這些接起來
//
// 本檔案的功能測試放在 FeatureTests.cs 的 TestColorScan()，和其他測試集中在一起。
//
// 方塊的兩種模式：
//   編輯模式  藍色邊框可拖曳移動、四角可縮放
//   鎖定      方塊完全固定，且對滑鼠完全穿透，點擊與拖曳全部傳到下方程式
// 按 F7 開始掃描會自動鎖定，F8 結束掃描會回到編輯模式。
// 一組色碼設定：要找的顏色，以及這一組自己的容差。
// 高亮色不必設定，一律由 Contrast() 從目標色算出對比色。
// 目標色以字串保存使用者輸入，因為輸入途中會有「半成品」，不該在那一刻就報錯。
public class ColorRule
{
    public string Target { get; set; }
    public int Tolerance { get; set; }
    public ColorRule() { Target = "#FF0000"; Tolerance = 24; }
    // 高亮色＝目標色的對比色：色相轉 180°、彩度拉滿，亮度往反方向拉。
    // 近灰階算不出有意義的互補色（互補後還是灰），就依明暗改用固定的亮綠或洋紅。
    // 這樣算出來的顏色和目標色在 RGB 上一定差得很遠，也順便避免掃描自我回饋。
    public static Color Contrast(Color target)
    {
        float saturation = target.GetSaturation(), brightness = target.GetBrightness();
        if (saturation < .18f) return brightness < .5f ? Color.FromArgb(57, 255, 20) : Color.FromArgb(214, 0, 132);
        return FromHsl((target.GetHue() + 180f) % 360f, 1f, brightness < .5f ? .62f : .40f);
    }
    static Color FromHsl(float hue, float saturation, float lightness)
    {
        float chroma = (1f - Math.Abs(2f * lightness - 1f)) * saturation, sector = hue / 60f;
        float second = chroma * (1f - Math.Abs(sector % 2f - 1f)), match = lightness - chroma / 2f;
        float r = 0f, g = 0f, b = 0f;
        if (sector < 1f) { r = chroma; g = second; }
        else if (sector < 2f) { r = second; g = chroma; }
        else if (sector < 3f) { g = chroma; b = second; }
        else if (sector < 4f) { g = second; b = chroma; }
        else if (sector < 5f) { r = second; b = chroma; }
        else { r = chroma; b = second; }
        return Color.FromArgb(255, Channel(r + match), Channel(g + match), Channel(b + match));
    }
    static int Channel(float value) { return Math.Max(0, Math.Min(255, (int)Math.Round(value * 255f))); }
    // 輸入途中一定會出現半成品色碼，那不是錯誤，所以提供不丟例外的版本。
    // IsValid 與色票預覽都走這裡，避免每敲一個字就丟接一次例外（也讓除錯器不會一直中斷）。
    public static bool TryParse(string text, out Color color)
    {
        try { color = Parse(text); return true; }
        catch { color = SystemColors.Control; return false; }
    }
    // 接受 #RRGGBB、#RGB、RRGGBB 與英文色名。解析不出來就丟例外，由呼叫端決定要不要當錯誤處理。
    public static Color Parse(string text)
    {
        string t = (text ?? "").Trim(); if (t.Length == 0) throw new Exception("色碼不可空白。");
        if (t[0] == '#') t = t.Substring(1);
        if (t.Length == 3 && t.All(Uri.IsHexDigit)) t = new string(new[] { t[0], t[0], t[1], t[1], t[2], t[2] });
        if (t.Length == 6 && t.All(Uri.IsHexDigit)) return Color.FromArgb(255, Convert.ToInt32(t.Substring(0, 2), 16), Convert.ToInt32(t.Substring(2, 2), 16), Convert.ToInt32(t.Substring(4, 2), 16));
        Color named; try { named = ColorTranslator.FromHtml(t); } catch { throw new Exception("無法解析色碼「" + (text ?? "") + "」，請輸入 #RRGGBB。"); }
        if (named.A == 0) throw new Exception("無法解析色碼「" + (text ?? "") + "」，請輸入 #RRGGBB。");
        return Color.FromArgb(255, named);
    }
    public static string Format(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
    public static void Validate(ColorRule rule) { if (rule == null) throw new Exception("色碼組無效。"); Parse(rule.Target); if (rule.Tolerance < 0 || rule.Tolerance > 255) throw new Exception("容差需介於 0～255。"); }
    public ColorRule Copy() { return new ColorRule { Target = Target, Tolerance = Tolerance }; }
}
// 一塊要畫的高亮：Bounds 是相對掃描範圍左上角的座標，Rule 是命中的色碼組序號。
public class ScanHit
{
    public Rectangle Bounds; public int Rule;
}
// 一次掃描的結果。Counts／Centers 以色碼組為索引，Centers 是命中格子的重心（相對掃描範圍）。
public class ScanResult
{
    public List<ScanHit> Hits = new List<ScanHit>(); public int[] Counts = new int[0]; public Point[] Centers = new Point[0]; public int Cell = 1;
    public int Total { get { return Counts.Sum(); } }
}
// 純粹的像素比對，完全不碰 UI 也不碰螢幕，所以可以直接餵陣列做單元測試。
public static class ColorScanner
{
    // 取 R／G／B 三個通道中最大的差值（Chebyshev 距離）。
    // 比歐氏距離直覺：容差 30 就是「每個通道都不差超過 30」。
    public static int Difference(int argb, Color target) { int r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255; return Math.Max(Math.Abs(r - target.R), Math.Max(Math.Abs(g - target.G), Math.Abs(b - target.B))); }
    // 以 sample 為間隔走訪像素，命中的取樣點歸進 block 網格，
    // 再把同一列相鄰的格子併成一個矩形，大幅減少要畫的矩形數量。
    //
    // 一個格子只認第一個命中的色碼組（由上往下），結果才穩定。
    // 副作用是：上方色碼組的容差若已涵蓋下方那組的目標色，下方那組就永遠不會有命中，
    // 介面上的提示文字有說明這件事。
    public static ScanResult Scan(int[] pixels, int width, int height, IList<Color> targets, IList<int> tolerances, int sample, int block)
    {
        if (pixels == null || targets == null || tolerances == null) throw new Exception("掃描參數不完整。");
        if (targets.Count != tolerances.Count) throw new Exception("色碼組與容差數量不一致。");
        if (width <= 0 || height <= 0 || pixels.Length < width * height) throw new Exception("掃描區域資料不完整。");
        if (sample < 1) sample = 1; if (block < 1) block = 1;
        int rules = targets.Count, cols = (width + block - 1) / block, rows = (height + block - 1) / block;
        var cells = new int[cols * rows]; for (int i = 0; i < cells.Length; i++) cells[i] = -1;
        var result = new ScanResult { Counts = new int[rules], Centers = new Point[rules], Cell = block };
        if (rules == 0) return result;
        for (int y = 0; y < height; y += sample)
        {
            int row = y * width, cellRow = (y / block) * cols;
            for (int x = 0; x < width; x += sample)
            {
                int index = cellRow + x / block; if (cells[index] >= 0) continue; int argb = pixels[row + x];
                for (int r = 0; r < rules; r++) if (Difference(argb, targets[r]) <= tolerances[r]) { cells[index] = r; break; }
            }
        }
        var sumX = new long[rules]; var sumY = new long[rules];
        for (int r = 0; r < rows; r++)
        {
            int c = 0;
            while (c < cols)
            {
                int rule = cells[r * cols + c]; if (rule < 0) { c++; continue; }
                int end = c; while (end + 1 < cols && cells[r * cols + end + 1] == rule) end++;
                int left = c * block, top = r * block, right = Math.Min(width, (end + 1) * block), bottom = Math.Min(height, (r + 1) * block);
                result.Hits.Add(new ScanHit { Rule = rule, Bounds = new Rectangle(left, top, right - left, bottom - top) });
                for (int k = c; k <= end; k++) { result.Counts[rule]++; sumX[rule] += k * block + block / 2; sumY[rule] += r * block + block / 2; }
                c = end + 1;
            }
        }
        for (int r = 0; r < rules; r++) result.Centers[r] = result.Counts[r] == 0 ? Point.Empty : new Point((int)(sumX[r] / result.Counts[r]), (int)(sumY[r] / result.Counts[r]));
        return result;
    }
}
// 螢幕上那個方塊。
//
// 內部全透明（TransparencyKey 是 Magenta），只畫兩樣東西：掃描範圍內的高亮色塊，
// 以及掃描範圍外那一圈邊框與把手。視窗樣式、不搶焦點、滑鼠穿透都由 OverlayForm 處理。
//
// 成員順序：設定 → 結果 → 幾何 → 兩種模式 → 亮暗相位機（掃描核心）→ 拖曳縮放 → 繪製。
public class ScanOverlay : OverlayForm
{
    // ───────────────────────── 設定：由工具視窗填入 ─────────────────────────

    // 要找的顏色與各自的容差，兩份清單以索引對應。Highlights 是對應的高亮色。
    public List<Color> Targets = new List<Color>();
    public List<int> Tolerances = new List<int>();
    List<Color> highlights = new List<Color>();
    public List<Color> Highlights
    {
        get { return highlights; }
        set { highlights = value ?? new List<Color>(); }
    }
    // Sample：每隔幾個像素取樣一次，越大越省 CPU 但越容易漏掉細小色塊。
    // Block：命中像素歸進多大的網格，也就是畫出來的高亮色塊尺寸。
    public int Sample = DefaultSample, Block = DefaultBlock;
    public const int DefaultSample = 1, DefaultBlock = 1;
    // 掃描間隔的預設值，同時也是下限 —— 欄位下限、Interval 的夾取、ScanProfile 的驗證
    // 全部引用這一個常數。閃爍頻率是 1 /（2 × 間隔），所以這個值決定了閃爍最快能多快：
    // 500 ms 對應 1 Hz。再低下去閃爍會快到看不舒服，暗相位也可能短到 DWM 來不及重新合成
    // （掃描就會讀到自己的殘影）。要更快的掃描就關掉閃爍。
    public const int DefaultInterval = 500;

    // ───────────────────────── 結果：回報給工具視窗 ─────────────────────────

    // 只有 Read 與 Scanning 會換掉這份清單，對外唯讀。
    List<ScanHit> hits = new List<ScanHit>();
    public List<ScanHit> Hits { get { return hits; } private set { hits = value; } }
    public event Action<ScanResult> Scanned;   // 每次掃描完成
    public event Action<string> Failed;        // 擷取或比對失敗，掃描已自行停止
    public event Action<Rectangle> AreaChanged;// 使用者拖曳或縮放結束

    // ───────────────────────── 內部狀態與建構 ─────────────────────────

    // Band：方塊四周預留給邊框與把手的寬度。掃描範圍是 Bounds 內縮 Band，
    // 所以我們畫的邊框、把手、尺寸文字全部落在掃描範圍之外，不會被自己掃到。
    // MinSide：掃描範圍的最小邊長。Grip：四角縮放把手的邊長。
    public const int Band = 22, MinSide = 24, Grip = 16;
    static readonly Color Chrome = Color.FromArgb(0, 120, 215);
    static readonly IList<Color> NoColours = new List<Color>();
    readonly Timer timer = new Timer { Interval = DefaultInterval };
    bool locked = true, blink = true, blank;
    int interval = DefaultInterval;
    int mode; Point anchor; Rectangle startBounds;   // 拖曳狀態
    Bitmap buffer; int[] pixels;                     // 擷取用的重複使用緩衝
    public ScanOverlay()
    {
        BackColor = Color.Magenta; TransparencyKey = Color.Magenta; Opacity = .85; Text = "顏色掃描小工具";
        MinimumSize = new Size(MinSide + Band * 2, MinSide + Band * 2);
        timer.Tick += (s, e) => Step();
    }

    // ───────────────────────── 幾何 ─────────────────────────

    // 對外一律用「掃描範圍」溝通；視窗實際大小是掃描範圍再外擴 Band。
    public Rectangle ScanArea
    {
        get { return Rectangle.Inflate(Bounds, -Band, -Band); }
        set { Bounds = Rectangle.Inflate(value, Band, Band); }
    }

    // ───────────────────────── 兩種模式：鎖定／編輯 ─────────────────────────

    // 唯一和其他覆蓋層不同的地方：只有鎖定時才穿透，編輯模式要抓得到邊框。
    protected override bool ClickThrough { get { return locked; } }
    public bool Locked
    {
        get { return locked; }
        set { if (locked == value) return; locked = value; mode = 0; Cursor = Cursors.Default; SyncPassthrough(); Invalidate(); }
    }

    // ───────────────────────── 亮暗相位機：掃描的核心 ─────────────────────────
    //
    // 一個掃描間隔分成兩段，由同一個計時器輪流驅動：
    //
    //   亮相位   畫高亮色塊
    //   暗相位   什麼都不畫（所以看到的是底下的原色），相位結束時才掃描
    //
    // 一個機制兩用：
    //   對人來說，亮／暗交替就是閃爍
    //   對掃描來說，暗相位的畫面上沒有我們自己的東西，讀到的是乾淨底圖
    //
    // 為什麼需要這樣：高亮色塊正好蓋在它偵測到的那些像素上。若掃描讀得到自己畫上去的
    // 東西，下一次就會讀到高亮色而不是目標色，命中隨即消失、再下一次又出現——色塊會
    // 抽動，回報的命中數也會在真實值和 0 之間跳。
    //
    // 為什麼不用 WDA_EXCLUDEFROMCAPTURE：那個旗標同樣能讓掃描讀不到自己，但它是整個
    // 視窗的設定，無法區分「誰在擷取」，連使用者自己的截圖也會拍不到高亮。靠「那一刻
    // 真的沒畫」達成，截圖工具就拍得到。
    //
    // 兩種模式的相位長度：
    //
    //   閃爍開啟   亮、暗各佔一個完整的掃描間隔 → 閃爍週期是 2 × Interval
    //   閃爍關閉   只留 BlankWindow 這個掃描必需的最小空白 → 週期就是 Interval
    //
    // 所以開啟閃爍會讓掃描頻率減半。這是必然的取捨：閃爍要慢到看得舒服，暗相位就得夠長，
    // 而掃描只能在暗相位進行。需要高頻掃描就把閃爍關掉。
    //
    // 上一輪沒有任何命中時畫面本來就是乾淨的，那一輪直接跳過暗相位，
    // 所以「什麼都沒找到」的情況完全沒有閃爍也沒有頻率損失。

    // 暗相位至少要這麼長，才夠 DWM 把「不畫」重新合成完畢。
    public const int BlankWindow = 40;
    // 使用者設定的掃描間隔是「一整個週期」，內含尾端的暗相位。
    public int Interval
    {
        get { return interval; }
        set { interval = Math.Max(DefaultInterval, value); if (!blank) timer.Interval = LitLength; }
    }
    // 閃爍＝亮、暗各給一個完整的掃描間隔；關閉就只留掃描必需的最小空白。
    public bool Blink
    {
        get { return blink; }
        set { if (blink == value) return; blink = value; if (!blank) timer.Interval = LitLength; Invalidate(); }
    }
    internal int BlankLength { get { return blink ? interval : BlankWindow; } }
    internal int LitLength { get { return blink ? interval : Math.Max(20, interval - BlankWindow); } }
    // 當下該用哪組顏色畫：暗相位回傳空色盤，OnPaint 就什麼都不畫。
    public IList<Color> Phase { get { return blank ? NoColours : highlights; } }
    internal bool Blanked { get { return blank; } }
    // 相位判斷抽成純函式，方便測試釘住「有命中就必須先清空才掃描」這個性質。
    internal static bool NeedsBlank(int hitCount, bool alreadyBlank) { return !alreadyBlank && hitCount > 0; }
    // 測試用：直接擺一組命中結果進來，好驗證相位機的行為，不必真的去擷取螢幕。
    internal void SeedHits(params ScanHit[] value) { Hits = new List<ScanHit>(value); }
    // ApplyRules() 每次按鍵都會設定 Scanning，所以沒變就直接返回，
    // 否則沒在掃描時每敲一個字都白跑一次清空與重繪。
    public bool Scanning
    {
        get { return timer.Enabled; }
        set
        {
            if (value == timer.Enabled) return;
            if (value) { blank = false; timer.Interval = LitLength; timer.Start(); }
            else { timer.Stop(); blank = false; Hits = new List<ScanHit>(); Invalidate(); }
        }
    }
    // 計時器的每一拍：該進暗相位就先清空畫面，已經在暗相位就真的掃描。
    internal void Step()
    {
        if (NeedsBlank(Hits.Count, blank))
        {
            // 先把「不畫」送上畫面，接下來這段空窗留給 DWM 重新合成。
            blank = true; timer.Interval = BlankLength; Invalidate(); if (IsHandleCreated) Update(); return;
        }
        blank = false; timer.Interval = LitLength;
        Read();
        Invalidate();
    }
    // 手動掃描一次（「立即掃描一次」按鈕）。畫面上可能正畫著上一輪的色塊，
    // 所以先清掉、讓它真的上畫面、等合成追上，再讀。
    public void ScanOnce()
    {
        if (Hits.Count > 0 && IsHandleCreated)
        {
            blank = true; Invalidate(); Update();
            System.Threading.Thread.Sleep(BlankWindow);
            blank = false;
        }
        Read();
        Invalidate();
    }
    // 擷取掃描範圍 → 取出像素 → 交給 ColorScanner 比對 → 更新 Hits 並回報。
    // 任何一步失敗就停止掃描並回報，不讓計時器每個週期重複同一個錯誤。
    void Read()
    {
        var area = ScanArea;
        if (area.Width < 1 || area.Height < 1 || Targets.Count == 0) { if (Hits.Count > 0) { Hits = new List<ScanHit>(); Invalidate(); } return; }
        try
        {
            if (buffer == null || buffer.Width != area.Width || buffer.Height != area.Height) { if (buffer != null) buffer.Dispose(); buffer = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb); pixels = new int[area.Width * area.Height]; }
            using (var g = Graphics.FromImage(buffer)) g.CopyFromScreen(area.X, area.Y, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
            var data = buffer.LockBits(new Rectangle(0, 0, area.Width, area.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < area.Height; y++) Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), pixels, y * area.Width, area.Width); }
            finally { buffer.UnlockBits(data); }
            var result = ColorScanner.Scan(pixels, area.Width, area.Height, Targets, Tolerances, Sample, Block);
            Hits = result.Hits; Invalidate(); if (Scanned != null) Scanned(result);
        }
        catch (Exception ex) { Scanning = false; if (Failed != null) Failed(ex.Message); }
    }

    // ───────────────────────── 拖曳與縮放（只在編輯模式）─────────────────────────

    // 四個縮放把手，順序為左上、右上、左下、右下。
    public Rectangle[] Grips() { int w = ClientSize.Width, h = ClientSize.Height; return new[] { new Rectangle(0, 0, Grip, Grip), new Rectangle(w - Grip, 0, Grip, Grip), new Rectangle(0, h - Grip, Grip, Grip), new Rectangle(w - Grip, h - Grip, Grip, Grip) }; }
    // 回傳拖曳區域代號：1～4 是上述四個把手（縮放），5 是其餘邊框（整塊移動）。
    public int ZoneAt(Point p) { var grips = Grips(); for (int i = 0; i < grips.Length; i++) if (grips[i].Contains(p)) return i + 1; return 5; }
    // 依區域代號把位移套到視窗邊界上，並保證掃描範圍不小於 MinSide。純函式，方便測試。
    public static Rectangle Transform(Rectangle bounds, int mode, Size delta)
    {
        int min = MinSide + Band * 2;
        if (mode == 5) return new Rectangle(bounds.X + delta.Width, bounds.Y + delta.Height, bounds.Width, bounds.Height);
        int left = bounds.Left, top = bounds.Top, right = bounds.Right, bottom = bounds.Bottom;
        if (mode == 1 || mode == 3) left = Math.Min(left + delta.Width, right - min);
        if (mode == 2 || mode == 4) right = Math.Max(right + delta.Width, left + min);
        if (mode == 1 || mode == 2) top = Math.Min(top + delta.Height, bottom - min);
        if (mode == 3 || mode == 4) bottom = Math.Max(bottom + delta.Height, top + min);
        return new Rectangle(left, top, right - left, bottom - top);
    }
    // 鎖定時方塊完全不接受拖曳（滑鼠事件根本不會進來），這三個方法讓該行為可被測試驗證。
    internal bool BeginDrag(Point client) { if (locked) { mode = 0; return false; } mode = ZoneAt(client); startBounds = Bounds; return true; }
    internal bool DragTo(Size delta) { if (locked || mode == 0) return false; Bounds = Transform(startBounds, mode, delta); Invalidate(); return true; }
    internal void EndDrag() { if (mode == 0) return; mode = 0; if (AreaChanged != null) AreaChanged(ScanArea); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; if (BeginDrag(e.Location)) anchor = Cursor.Position; }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (locked) { Cursor = Cursors.Default; return; }
        if (mode == 0) { int zone = ZoneAt(e.Location); Cursor = zone == 1 || zone == 4 ? Cursors.SizeNWSE : zone == 2 || zone == 3 ? Cursors.SizeNESW : Cursors.SizeAll; return; }
        DragTo(new Size(Cursor.Position.X - anchor.X, Cursor.Position.Y - anchor.Y));
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); EndDrag(); }

    // ───────────────────────── 繪製 ─────────────────────────

    public const int StrongAlpha = 215;
    // 底色是 Magenta，也就是 TransparencyKey，所以沒畫到的地方都是全透明且滑鼠穿透。
    // 高亮色塊畫在掃描範圍內；邊框、把手、尺寸文字一律畫在 Band 那圈裡，不侵入掃描範圍。
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; int w = ClientSize.Width, h = ClientSize.Height;
        var inner = new Rectangle(Band, Band, Math.Max(0, w - Band * 2), Math.Max(0, h - Band * 2));
        PaintHits(g, inner.Location, Hits, Phase);
        // 鎖定：只留一圈虛線細框，畫在掃描範圍外側。
        if (locked) { using (var pen = new Pen(Chrome, 2)) { pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash; g.DrawRectangle(pen, Band - 3, Band - 3, inner.Width + 5, inner.Height + 5); } return; }
        // 編輯模式：整圈塗實色才抓得到（透明處會穿透），四角再放白色把手。
        using (var brush = new SolidBrush(Chrome))
        {
            g.FillRectangle(brush, 0, 0, w, Band); g.FillRectangle(brush, 0, h - Band, w, Band);
            g.FillRectangle(brush, 0, Band, Band, h - Band * 2); g.FillRectangle(brush, w - Band, Band, Band, h - Band * 2);
        }
        foreach (var grip in Grips()) g.FillRectangle(Brushes.White, grip);
        using (var font = new Font("Microsoft JhengHei UI", 9, FontStyle.Bold)) g.DrawString(inner.Width + " × " + inner.Height + "　拖曳邊框移動 · 四角縮放", font, Brushes.White, Grip + 4, 3);
    }
    // 把命中色塊畫出來。傳進來的 palette 就是當下相位要用的顏色（暗相位是空的）。
    // 抽成靜態方法，測試才能直接畫到 Bitmap 上比對。
    public static void PaintHits(Graphics g, Point origin, IEnumerable<ScanHit> hits, IList<Color> palette, int alpha = StrongAlpha)
    {
        if (hits == null || palette == null || palette.Count == 0) return;
        int solid = Math.Max(10, Math.Min(255, alpha));
        var brushes = palette.Select(c => new SolidBrush(Color.FromArgb(solid, c))).ToArray();
        try
        {
            foreach (var hit in hits)
            {
                if (hit.Rule < 0 || hit.Rule >= brushes.Length) continue;
                g.FillRectangle(brushes[hit.Rule], hit.Bounds.X + origin.X, hit.Bounds.Y + origin.Y, hit.Bounds.Width, hit.Bounds.Height);
            }
        }
        finally { foreach (var brush in brushes) brush.Dispose(); }
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Stop(); timer.Dispose(); if (buffer != null) { buffer.Dispose(); buffer = null; } } base.Dispose(disposing); }
}
// 吸色時跟著游標的小色票。全程滑鼠穿透、不搶焦點，
// 而且刻意和游標錯開，所以永遠不會蓋住正在取樣的那一個像素。
public class ColorBubble : OverlayForm
{
    public static readonly Size Preferred = new Size(206, 56);
    const int Gap = 24;
    Color value = Color.Black;
    public ColorBubble() { Size = Preferred; BackColor = Color.FromArgb(20, 29, 45); Text = "吸色"; }
    // 預設放在游標右下，靠近螢幕邊緣就翻到另一側，最後再夾回螢幕範圍內。
    public static Point Place(Rectangle screen, Point cursor, Size size)
    {
        int x = cursor.X + Gap, y = cursor.Y + Gap;
        if (x + size.Width > screen.Right) x = cursor.X - Gap - size.Width;
        if (y + size.Height > screen.Bottom) y = cursor.Y - Gap - size.Height;
        return new Point(Math.Max(screen.Left, Math.Min(x, screen.Right - size.Width)), Math.Max(screen.Top, Math.Min(y, screen.Bottom - size.Height)));
    }
    public void Track(Point cursor, Color color)
    {
        value = color; Location = Place(Screen.FromPoint(cursor).WorkingArea, cursor, Size);
        if (!Visible) Show();
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics;
        using (var brush = new SolidBrush(value)) g.FillRectangle(brush, 12, 12, 32, 32);
        g.DrawRectangle(Pens.White, 12, 12, 32, 32);
        using (var mono = new Font("Consolas", 12, FontStyle.Bold)) g.DrawString(ColorRule.Format(value), mono, Brushes.White, 56, 10);
        using (var hint = new Font("Microsoft JhengHei UI", 8.5f)) g.DrawString("點擊取色 · Esc 或右鍵取消", hint, Brushes.Gainsboro, 56, 32);
    }
}
// 吸色：不蓋任何全螢幕視窗、也不拍快照，改用全域滑鼠鉤子等使用者按下左鍵，
// 按下的那一刻才讀游標下的那一個像素。
//
// 之前試過的兩種做法都有問題：
//   近乎透明的全螢幕接收層 → DWM 合成下 CopyFromScreen 會把那層一起抓進去，顏色偏掉
//   先拍快照再鋪滿全螢幕   → 自己的視窗剛隱藏、下層 app 還沒重畫完就被拍進去，留下殘影
// 現在畫面上唯一屬於我們的東西只有跟著游標、刻意錯開的小色票，
// 取樣點永遠沒有任何覆蓋物，所以不必凍結畫面也不必等待重畫，兩個問題都不存在。
public class ColorPicker : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookCallback fn, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wparam, IntPtr lparam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
    delegate IntPtr HookCallback(int code, IntPtr wparam, IntPtr lparam);
    [StructLayout(LayoutKind.Sequential)] struct MousePoint { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct KeyStroke { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
    const int LowLevelMouse = 14, LowLevelKeyboard = 13;
    readonly HookCallback mouseProc, keyProc;
    readonly ColorBubble bubble = new ColorBubble();
    readonly Control host;
    IntPtr mouseHook = IntPtr.Zero, keyHook = IntPtr.Zero;
    Color pending = Color.Black; bool armed, done;
    public event Action<Color?> Finished;
    // host 只用來把結果切回 UI 執行緒，等鉤子回呼結束後才動 UI。
    public ColorPicker(Control host) { this.host = host; mouseProc = OnMouse; keyProc = OnKey; }
    public bool Running { get { return mouseHook != IntPtr.Zero; } }
    // 1×1 的擷取緩衝重複使用。滑鼠移動每秒可達數百次，每次都新建 Bitmap 與 Graphics
    // 會在鉤子回呼裡配置兩個 GDI+ 物件；低階鉤子的回呼超時會被 Windows 靜默移除，
    // 所以這裡要盡量輕。
    Bitmap dot; Graphics dotCanvas;
    public Color Sample(Point p)
    {
        if (dot == null) { dot = new Bitmap(1, 1, PixelFormat.Format32bppArgb); dotCanvas = Graphics.FromImage(dot); }
        dotCanvas.CopyFromScreen(p.X, p.Y, 0, 0, new Size(1, 1), CopyPixelOperation.SourceCopy);
        return Color.FromArgb(255, dot.GetPixel(0, 0));
    }
    // 一次性取色用的版本，不值得為它留著快取。
    public static Color PixelAt(Point p)
    {
        using (var bitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(p.X, p.Y, 0, 0, new Size(1, 1), CopyPixelOperation.SourceCopy);
            return Color.FromArgb(255, bitmap.GetPixel(0, 0));
        }
    }
    public void Start()
    {
        if (mouseHook != IntPtr.Zero) return;
        var module = GetModuleHandle(null);
        mouseHook = SetWindowsHookEx(LowLevelMouse, mouseProc, module, 0);
        if (mouseHook == IntPtr.Zero) throw new Exception("無法啟動吸色（Windows 錯誤碼 " + Marshal.GetLastWin32Error() + "），請以相同權限層級重試。");
        keyHook = SetWindowsHookEx(LowLevelKeyboard, keyProc, module, 0);   // 只為了 Esc，失敗就仍可用右鍵取消
        Preview(Cursor.Position);
    }
    public void Stop()
    {
        if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        if (keyHook != IntPtr.Zero) { UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; }
        if (!bubble.IsDisposed && bubble.Visible) bubble.Hide();
    }
    public void Dispose()
    {
        done = true; Stop(); bubble.Dispose();
        if (dotCanvas != null) { dotCanvas.Dispose(); dotCanvas = null; }
        if (dot != null) { dot.Dispose(); dot = null; }
    }
    void Preview(Point p) { try { bubble.Track(p, Sample(p)); } catch { } }
    // 吸色期間吃掉所有滑鼠按鍵，免得順手在目標程式上點到東西。
    // 左鍵按下就記下顏色、放開才收工，這樣不會漏一個 MouseUp 給目標程式。
    IntPtr OnMouse(int code, IntPtr wparam, IntPtr lparam)
    {
        if (code >= 0 && !done)
        {
            int message = wparam.ToInt32();
            if (message == 0x200) { var p = PointFrom(lparam); if (p.HasValue) Preview(p.Value); }
            else if (message == 0x201) { var p = PointFrom(lparam); armed = p.HasValue; if (armed) { try { pending = Sample(p.Value); } catch { armed = false; } } return new IntPtr(1); }
            else if (message == 0x202) { if (armed) { armed = false; Finish(pending); } return new IntPtr(1); }
            else if (message == 0x205) { Finish(null); return new IntPtr(1); }
            else if (message == 0x204 || message == 0x207 || message == 0x208) return new IntPtr(1);
        }
        return CallNextHookEx(mouseHook, code, wparam, lparam);
    }
    IntPtr OnKey(int code, IntPtr wparam, IntPtr lparam)
    {
        if (code >= 0 && !done && wparam.ToInt32() == 0x100)
        {
            try { var stroke = (KeyStroke)Marshal.PtrToStructure(lparam, typeof(KeyStroke)); if (stroke.Key == 0x1B) { Finish(null); return new IntPtr(1); } }
            catch { }
        }
        return CallNextHookEx(keyHook, code, wparam, lparam);
    }
    static Point? PointFrom(IntPtr lparam)
    {
        try { var data = (MousePoint)Marshal.PtrToStructure(lparam, typeof(MousePoint)); return new Point(data.X, data.Y); }
        catch { return null; }
    }
    void Finish(Color? chosen)
    {
        if (done) return;
        done = true; Stop();
        var handler = Finished;
        if (handler == null) return;
        if (host != null && host.IsHandleCreated) { try { host.BeginInvoke(new Action(() => handler(chosen))); return; } catch { } }
        handler(chosen);
    }
}
// 一列色碼組：目標色碼與容差，外加吸色與移除。「＋ 新增色碼組」每按一次就多一列。
// 高亮色不再需要輸入，右邊的色票直接顯示自動算出的對比色，只供預覽。
// 繼承 BufferedEditorPanel（本身就是設好雙緩衝的 TableLayoutPanel），色票與按鈕重繪不閃動。
public class ColorRuleRow : BufferedEditorPanel
{
    public readonly TextBox Target = new TextBox { Width = 104 };
    public readonly NumericUpDown Tolerance = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 24, Width = 66 };
    readonly Panel targetSwatch = Swatch(), highlightSwatch = Swatch();
    readonly Label index = new Label { AutoSize = true, ForeColor = Color.DimGray };
    public event Action Changed; public event Action<ColorRuleRow> Removed; public event Action<ColorRuleRow> PickRequested;
    static Panel Swatch() { return new Panel { Width = 24, Height = 24, BorderStyle = BorderStyle.FixedSingle }; }
    // 按鈕一律 GrowAndShrink＋內距＋最小寬度。只給 AutoSize 的話，偏好大小會先用預設字型
    // （8.25pt 英文字型）算好，等這一列被加進工具視窗換成 10pt 中文字型後不再放大，文字就被擠壓。
    static Button Small(string text)
    {
        return new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 4, 12, 4), MinimumSize = new Size(68, 28)
        };
    }
    // 整列用 TableLayoutPanel，每個格子都只 Anchor 左側，版面就會自動把高矮不同的
    // 標籤、輸入框、色票、按鈕垂直居中；每格的右邊距則決定欄與欄之間的距離。
    public ColorRuleRow(ColorRule rule)
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; RowCount = 1; ColumnCount = 10;
        for (int i = 0; i < ColumnCount; i++) ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Margin = new Padding(0, 0, 0, 6); Padding = new Padding(10, 5, 10, 5); BackColor = Color.White;
        int column = 0;
        Place(index, ref column, 10);
        Place(Cap("目標色碼"), ref column, 8);
        Place(Target, ref column, 6);
        Place(targetSwatch, ref column, 14);
        var pick = Small("吸色"); pick.Click += (s, e) => { if (PickRequested != null) PickRequested(this); };
        Place(pick, ref column, 22);
        Place(Cap("高亮"), ref column, 8);
        Place(highlightSwatch, ref column, 22);
        Place(Cap("容差"), ref column, 8);
        Place(Tolerance, ref column, 22);
        var remove = Small("移除"); remove.Click += (s, e) => { if (Removed != null) Removed(this); };
        Place(remove, ref column, 0);
        Target.TextChanged += (s, e) => Sync(true); Tolerance.ValueChanged += (s, e) => { if (Changed != null) Changed(); };
        Value = rule ?? new ColorRule();
    }
    void Place(Control child, ref int column, int gap)
    {
        child.Anchor = AnchorStyles.Left; child.Margin = new Padding(0, 0, gap, 0);
        Controls.Add(child, column++, 0);
    }
    // 換字型後（被加進工具視窗時就會發生）重算一次，AutoSize 的偏好大小才會跟著長大。
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        foreach (Control child in Controls) child.PerformLayout();
        PerformLayout();
    }
    static Label Cap(string text) { return new Label { Text = text, AutoSize = true }; }
    public void SetIndex(int number) { index.Text = "#" + number; }
    public ColorRule Value
    {
        get { return new ColorRule { Target = Target.Text, Tolerance = (int)Tolerance.Value }; }
        set { Target.Text = value.Target; Tolerance.Value = Math.Max(0, Math.Min(255, value.Tolerance)); Sync(false); }
    }
    // 解析一次就好，三個地方共用結果。容差有 NumericUpDown 限制範圍，不必再驗。
    bool Resolve(out Color target) { return ColorRule.TryParse(Target.Text, out target); }
    public bool IsValid { get { Color ignored; return Resolve(out ignored); } }
    // 目標色解析不出來時回中性色而不是丟例外——屬性 getter 不該丟例外。
    public Color Highlight { get { Color target; return Resolve(out target) ? ColorRule.Contrast(target) : SystemColors.Control; } }
    void Sync(bool notify)
    {
        Color target;
        bool valid = Resolve(out target);
        targetSwatch.BackColor = valid ? target : SystemColors.Control;
        highlightSwatch.BackColor = valid ? ColorRule.Contrast(target) : SystemColors.Control;
        if (notify && Changed != null) Changed();
    }
}
// 會被寫進 JSON 的完整設定。所有欄位都用屬性，JavaScriptSerializer 才讀寫得到；
// 舊檔案沒有的欄位會保留建構子的預設值，多出來的欄位則被忽略，所以新增欄位不會弄壞舊檔。
public class ScanProfile
{
    public string Kind { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Interval { get; set; }
    public int Sample { get; set; }
    public int Block { get; set; }
    public bool Blink { get; set; }
    public List<ColorRule> Rules { get; set; }
    // 預設值直接引用方塊那邊的常數，避免同一個數字有兩個來源。
    public ScanProfile()
    {
        Kind = "MacroColorScan"; Width = 320; Height = 240;
        Interval = ScanOverlay.DefaultInterval; Sample = ScanOverlay.DefaultSample; Block = ScanOverlay.DefaultBlock;
        Blink = true; Rules = new List<ColorRule>();
    }
    public static ScanProfile Centered()
    {
        var profile = new ScanProfile(); var screen = Screen.PrimaryScreen.WorkingArea;
        profile.X = screen.Left + (screen.Width - profile.Width) / 2; profile.Y = screen.Top + (screen.Height - profile.Height) / 2;
        profile.Rules.Add(new ColorRule()); return profile;
    }
    public Rectangle Area { get { return new Rectangle(X, Y, Width, Height); } }
    public static void Validate(ScanProfile p)
    {
        if (p == null || p.Rules == null) throw new Exception("掃描設定無效。");
        if (p.Rules.Count > 20) throw new Exception("色碼組最多 20 組。");
        if (Math.Abs((long)p.X) > 100000 || Math.Abs((long)p.Y) > 100000) throw new Exception("掃描區域座標超出允許範圍。");
        if (p.Width < ScanOverlay.MinSide || p.Height < ScanOverlay.MinSide || p.Width > 8000 || p.Height > 8000) throw new Exception("掃描區域大小需介於 " + ScanOverlay.MinSide + "～8000 像素。");
        if (p.Interval < ScanOverlay.DefaultInterval || p.Interval > 10000) throw new Exception("掃描間隔需介於 " + ScanOverlay.DefaultInterval + "～10000 毫秒。");
        if (p.Sample < 1 || p.Sample > 32) throw new Exception("取樣間隔需介於 1～32 像素。");
        if (p.Block < 1 || p.Block > 64) throw new Exception("色塊大小需介於 1～64 像素。");
        foreach (var rule in p.Rules) ColorRule.Validate(rule);
    }
}
// 設定的自動保存。序列化走 Json、寫檔走 JsonFile（暫存檔 → 置換），
// 沿用 EvaMacroStudio 資料夾，和動作範本的自動保存放在一起。
public static class ScanSession
{
    public static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EvaMacroStudio", "scan-profile.json"); } }
    public static ScanProfile Load(string path)
    {
        if (!File.Exists(path)) return ScanProfile.Centered();
        var profile = Json.Read<ScanProfile>(File.ReadAllText(path));
        if (profile == null || profile.Kind != "MacroColorScan") throw new Exception("掃描設定檔格式不符。");
        ScanProfile.Validate(profile); return profile;
    }
    public static void Save(string path, ScanProfile profile)
    {
        ScanProfile.Validate(profile);
        JsonFile.Write(path, profile, createFolder: true);
    }
}
// 工具視窗：把方塊、色碼組、掃描參數接起來，並負責 F7／F8 熱鍵與設定的載入保存。
// 非模態，可與動作範本編輯同時使用；建構子傳入 profile 時不讀寫磁碟（測試用）。
public class ScanStudioForm : Form
{
    internal readonly ScanOverlay overlay = new ScanOverlay();
    internal readonly FlowLayoutPanel rules = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.FromArgb(238, 238, 238), Padding = new Padding(6) };
    internal readonly NumericUpDown areaX = Num(-100000, 100000, 0), areaY = Num(-100000, 100000, 0), areaW = Num(ScanOverlay.MinSide, 8000, 320), areaH = Num(ScanOverlay.MinSide, 8000, 240);
    // 下限與預設都引用方塊那邊的 DefaultInterval，避免欄位收得進去、方塊那邊又默默夾掉。
    internal readonly NumericUpDown interval = Num(ScanOverlay.DefaultInterval, 10000, ScanOverlay.DefaultInterval), sample = Num(1, 32, ScanOverlay.DefaultSample), block = Num(1, 64, ScanOverlay.DefaultBlock);
    internal readonly Button scanButton = Flat("開始掃描 (F7)"), lockButton = Flat("鎖定掃描範圍"), addRule = Flat("＋ 新增色碼組"), centerButton = Flat("置中於主螢幕");
    internal readonly CheckBox blink = new CheckBox { Text = "高亮閃爍", AutoSize = true, Checked = true, Margin = new Padding(16, 9, 0, 0) };
    internal readonly Label status = new Label { AutoSize = true, Margin = new Padding(2, 8, 2, 4), Text = "就緒｜新增色碼組後按「開始掃描 (F7)」" };
    public const int HotStart = 7, HotStop = 8, KeyStart = 118, KeyStop = 119;
    // 所有按鈕統一最小高度，FlowLayoutPanel 是靠上對齊，高度一致才看起來整齊。
    const int ButtonHeight = 30;
    readonly bool persist; bool syncing, picking, overlayShown, scanning, hotkeysReady, ownerMinimized;
    FormWindowState ownerState = FormWindowState.Normal;
    // 吸色進行中的狀態：哪一列在等顏色、當時把哪些視窗讓開了、以及讓開前的視窗狀態。
    ColorPicker picker; ColorRuleRow pickRow; bool pickOverlay, pickSelf, pickParent;
    FormWindowState pickSelfState = FormWindowState.Normal, pickParentState = FormWindowState.Normal;
    // 保險絲：低階鉤子若被系統靜默移除，吸色就永遠不會結束，靠這個逾時把狀態收回來。
    readonly Timer pickGuard = new Timer { Interval = 30000 };
    static NumericUpDown Num(int min, int max, int value) { return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 92 }; }
    static Button Flat(string text)
    {
        return new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 4, 12, 4), MinimumSize = new Size(0, ButtonHeight), Margin = new Padding(0, 2, 10, 4),
            BackColor = Color.White, UseVisualStyleBackColor = false, FlatStyle = FlatStyle.Flat
        };
    }
    static Label Hint(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 4, 0, 0) }; }
    public ScanStudioForm() : this(null) { }
    public ScanStudioForm(ScanProfile profile)
    {
        persist = profile == null;
        Icon = AppIdentity.Icon; Text = "顏色掃描小工具"; Size = new Size(980, 560); MinimumSize = new Size(820, 460); BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Microsoft JhengHei UI", 10); StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(14, 10, 14, 6) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
        Controls.Add(layout);
        var toolbar = Row(); layout.Controls.Add(toolbar, 0, 0);
        // 只加粗不放大，否則這顆會比同列其他按鈕高一截。
        scanButton.Font = new Font(Font, FontStyle.Bold); scanButton.BackColor = Color.LightGreen;
        toolbar.Controls.Add(scanButton); toolbar.Controls.Add(lockButton);
        var once = Flat("立即掃描一次"); once.Click += (s, e) => Guard(() => { overlay.ScanOnce(); status.Text = "已掃描一次"; }); toolbar.Controls.Add(once);
        toolbar.Controls.Add(blink);
        var geometry = Row(); layout.Controls.Add(geometry, 0, 1);
        Field(geometry, "區域 X", areaX); Field(geometry, "區域 Y", areaY); Field(geometry, "寬", areaW); Field(geometry, "高", areaH);
        centerButton.Click += (s, e) => Guard(Center); geometry.Controls.Add(centerButton);
        var timing = Row(); layout.Controls.Add(timing, 0, 2);
        Field(timing, "掃描間隔（ms）", interval); Field(timing, "取樣間隔（px）", sample); Field(timing, "色塊大小（px）", block);
        var adder = Row(); layout.Controls.Add(adder, 0, 3);
        adder.Controls.Add(addRule);
        adder.Controls.Add(Hint("輸入色碼或按「吸色」取色，高亮自動用對比色\n由上往下比對：上方色碼組的容差若已涵蓋下方的目標色，下方那組就不會有命中"));
        layout.Controls.Add(rules, 0, 4); layout.Controls.Add(status, 0, 5);
        addRule.Click += (s, e) => Guard(() => { AddRule(new ColorRule()); status.Text = "已新增色碼組 #" + rules.Controls.Count; });
        scanButton.Click += (s, e) => Guard(() => SetScanning(!scanning));
        lockButton.Click += (s, e) => Guard(() => SetLocked(!overlay.Locked));
        blink.CheckedChanged += (s, e) => { overlay.Blink = blink.Checked; if (!syncing) status.Text = blink.Checked ? "高亮改為閃爍（亮暗各半）。" : "高亮改為持續顯示。"; };
        foreach (var field in new[] { areaX, areaY, areaW, areaH }) field.ValueChanged += (s, e) => { if (!syncing) Guard(ApplyArea); };
        interval.ValueChanged += (s, e) => { if (!syncing) overlay.Interval = (int)interval.Value; };
        sample.ValueChanged += (s, e) => { if (!syncing) overlay.Sample = (int)sample.Value; };
        block.ValueChanged += (s, e) => { if (!syncing) overlay.Block = (int)block.Value; };
        overlay.AreaChanged += area => SyncArea(area);
        overlay.Scanned += Report;
        overlay.Failed += message => { status.Text = "掃描已暫停：" + message; };
        string error = null;
        if (persist) { try { profile = ScanSession.Load(ScanSession.DefaultPath); } catch (Exception ex) { error = ex.Message; profile = ScanProfile.Centered(); } }
        Apply(profile);
        ShowOverlay(true); SetLocked(false);
        if (error != null) status.Text = "無法載入上次的掃描設定（已改用預設值）：" + error;
        // 鉤子被系統移除時吸色不會自己結束，逾時就把視窗與狀態收回來。
        // 訊息裡的秒數直接從 Interval 推導，改了間隔不會和文字說法對不上。
        pickGuard.Tick += (s, e) => { pickGuard.Stop(); if (!picking) return; PickDone(null); status.Text = "吸色逾時已取消（" + pickGuard.Interval / 1000 + " 秒內未取色）。"; };
        // 關閉時若還在吸色，先把鉤子收掉再走；不要擋住關閉，否則主視窗會關不掉。
        FormClosing += (s, e) => { if (picking) PickDone(null); scanning = false; overlay.Scanning = false; RestoreOwner(); Persist(); };
    }
    // F7 開始掃描、F8 結束掃描；工具視窗縮小時仍收得到，關閉視窗即釋放。
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        bool start = Native.RegisterHotKey(Handle, HotStart, 0x4000, KeyStart), stop = Native.RegisterHotKey(Handle, HotStop, 0x4000, KeyStop);
        hotkeysReady = start && stop;
        if (!hotkeysReady) status.Text = "F7／F8 已被其他程式占用，請改用按鈕操作掃描。";
    }
    protected override void OnHandleDestroyed(EventArgs e) { Native.UnregisterHotKey(Handle, HotStart); Native.UnregisterHotKey(Handle, HotStop); base.OnHandleDestroyed(e); }
    protected override void WndProc(ref Message m)
    {
        // 吸色期間會 DoEvents，熱鍵必須忽略，否則會在取色中途跑去開關掃描。
        if (m.Msg == 0x312 && !picking) { int id = m.WParam.ToInt32(); if (id == HotStart && !scanning) Guard(() => SetScanning(true)); else if (id == HotStop && scanning) Guard(() => SetScanning(false)); }
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pickGuard.Stop(); pickGuard.Dispose();
            if (picker != null) { picker.Finished -= PickDone; picker.Dispose(); picker = null; }
            if (overlay != null && !overlay.IsDisposed) { overlay.Scanning = false; overlay.Dispose(); }
        }
        base.Dispose(disposing);
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); ShowOverlay(true); }
    static FlowLayoutPanel Row() { return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 0, 0, 8) }; }
    // 標籤與輸入框放進 TableLayoutPanel 的兩個格子，兩邊都只 Anchor 左側，
    // 沒有 Top／Bottom 的格子會由版面自動垂直居中，換字型或換 DPI 都不必手調 Margin。
    static void Field(FlowLayoutPanel parent, string label, Control control)
    {
        var field = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 18, 4) };
        field.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        field.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
        control.Margin = Padding.Empty; control.Anchor = AnchorStyles.Left;
        field.Controls.Add(caption, 0, 0); field.Controls.Add(control, 1, 0);
        parent.Controls.Add(field);
    }
    void Guard(Action action) { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }
    public void Apply(ScanProfile profile)
    {
        if (profile == null) profile = ScanProfile.Centered();
        ScanProfile.Validate(profile);
        syncing = true;
        try
        {
            foreach (var row in Rows().ToArray()) { rules.Controls.Remove(row); row.Dispose(); }
            areaX.Value = profile.X; areaY.Value = profile.Y; areaW.Value = Math.Max(areaW.Minimum, profile.Width); areaH.Value = Math.Max(areaH.Minimum, profile.Height);
            interval.Value = profile.Interval; sample.Value = profile.Sample; block.Value = profile.Block; blink.Checked = profile.Blink;
        }
        finally { syncing = false; }
        overlay.Interval = profile.Interval; overlay.Sample = profile.Sample; overlay.Block = profile.Block; overlay.Blink = profile.Blink;
        foreach (var rule in profile.Rules) AddRule(rule.Copy());
        ApplyArea();
    }
    public ScanProfile Current()
    {
        var area = overlay.ScanArea;
        return new ScanProfile { X = area.X, Y = area.Y, Width = area.Width, Height = area.Height, Interval = (int)interval.Value, Sample = (int)sample.Value, Block = (int)block.Value, Blink = blink.Checked, Rules = Rows().Select(r => r.Value).Where(r => { try { ColorRule.Validate(r); return true; } catch { return false; } }).ToList() };
    }
    void Persist() { if (!persist) return; try { ScanSession.Save(ScanSession.DefaultPath, Current()); } catch { } }
    internal IEnumerable<ColorRuleRow> Rows() { return rules.Controls.OfType<ColorRuleRow>(); }
    public ColorRuleRow AddRule(ColorRule rule)
    {
        if (rules.Controls.Count >= 20) throw new Exception("色碼組最多 20 組。");
        var row = new ColorRuleRow(rule);
        row.Changed += ApplyRules;
        row.Removed += RemoveRow;
        row.PickRequested += Pick;
        rules.Controls.Add(row); Renumber(); ApplyRules(); return row;
    }
    void Renumber() { int n = 0; foreach (var row in Rows()) row.SetIndex(++n); }
    // 移除一列。抽成具名方法而不是留在 Removed 的 lambda 裡，測試才能直接呼叫——
    // 用 Button.PerformClick() 是不行的：表單沒有 Show() 過，整條父鏈的 Visible 都是 false，
    // Button.CanSelect 因此為 false，PerformClick() 會靜默不做事。
    internal void RemoveRow(ColorRuleRow row)
    {
        if (row == null || !rules.Controls.Contains(row)) return;
        rules.Controls.Remove(row); row.Dispose();
        Renumber(); ApplyRules(); status.Text = "已移除色碼組";
    }
    // 只把能解析的色碼送進掃描，輸入途中的半成品不會中斷掃描。
    void ApplyRules()
    {
        var valid = Rows().Where(r => r.IsValid).Select(r => r.Value).ToList();
        overlay.Targets = valid.Select(r => ColorRule.Parse(r.Target)).ToList();
        overlay.Tolerances = valid.Select(r => r.Tolerance).ToList();
        overlay.Highlights = valid.Select(r => ColorRule.Contrast(ColorRule.Parse(r.Target))).ToList();
        int total = rules.Controls.Count;
        overlay.Scanning = scanning && overlayShown && overlay.Visible && valid.Count > 0;
        if (total == 0) status.Text = "請先按「＋ 新增色碼組」輸入要找的顏色。";
        else if (valid.Count < total) status.Text = "有 " + (total - valid.Count) + " 組色碼還沒填好，" + (scanning ? "先用其餘 " + valid.Count + " 組掃描。" : "填好後可開始掃描。");
        overlay.Invalidate();
    }
    void ApplyArea()
    {
        var area = new Rectangle((int)areaX.Value, (int)areaY.Value, (int)areaW.Value, (int)areaH.Value);
        if (overlay.ScanArea != area) overlay.ScanArea = area;
    }
    void SyncArea(Rectangle area) { syncing = true; try { areaX.Value = Clamp(areaX, area.X); areaY.Value = Clamp(areaY, area.Y); areaW.Value = Clamp(areaW, area.Width); areaH.Value = Clamp(areaH, area.Height); } finally { syncing = false; } }
    static decimal Clamp(NumericUpDown field, int value) { return Math.Max(field.Minimum, Math.Min(field.Maximum, value)); }
    void Center() { var screen = Screen.PrimaryScreen.WorkingArea; syncing = true; try { areaX.Value = screen.Left + (screen.Width - (int)areaW.Value) / 2; areaY.Value = screen.Top + (screen.Height - (int)areaH.Value) / 2; } finally { syncing = false; } ApplyArea(); status.Text = "掃描方塊已置中"; }
    internal void ShowOverlay(bool visible)
    {
        overlayShown = visible;
        if (visible) { if (!overlay.Visible && IsHandleCreated && !DesignMode) overlay.Show(); } else if (overlay.Visible) overlay.Hide();
        ApplyRules();
    }
    internal bool IsScanning { get { return scanning; } }
    // 開始掃描：自動鎖定穿透，工具視窗與主視窗一起縮小；結束掃描：兩個視窗一起彈回並回到編輯模式。
    internal void SetScanning(bool on)
    {
        if (on)
        {
            int valid = Rows().Count(r => r.IsValid);
            if (valid == 0) throw new Exception("請先新增至少一組填妥的色碼組，再開始掃描。");
            scanning = true; ShowOverlay(true); SetLocked(true);
            scanButton.Text = "結束掃描 (F8)"; scanButton.BackColor = Color.MistyRose;
            status.Text = "掃描中（" + valid + " 組色碼）｜" + (hotkeysReady ? "按 F8 結束" : "請用按鈕結束");
            if (IsHandleCreated) WindowState = FormWindowState.Minimized;
            MinimizeOwner();
        }
        else
        {
            scanning = false; overlay.Scanning = false; SetLocked(false); ShowOverlay(true);
            scanButton.Text = "開始掃描 (F7)"; scanButton.BackColor = Color.LightGreen;
            RestoreOwner();
            if (IsHandleCreated)
            {
                if (!Visible) Show();
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                Activate(); BringToFront();
            }
            status.Text = "已結束掃描，回到編輯模式，可拖曳邊框重新定位。";
        }
    }
    // 掃描時把主視窗也收掉，畫面才不會被擋住；只還原「自己收起來的」那一次，使用者本來就最小化的不動。
    void MinimizeOwner()
    {
        var parent = Owner;
        if (parent == null || !parent.IsHandleCreated || parent.WindowState == FormWindowState.Minimized) return;
        ownerState = parent.WindowState; ownerMinimized = true; parent.WindowState = FormWindowState.Minimized;
    }
    void RestoreOwner()
    {
        var parent = Owner;
        if (!ownerMinimized || parent == null || !parent.IsHandleCreated) { ownerMinimized = false; return; }
        ownerMinimized = false; parent.WindowState = ownerState;
    }
    internal NumericUpDown[] GeometryFields() { return new[] { areaX, areaY, areaW, areaH }; }
    // 鎖定＝方塊完全固定＋全穿透，滑鼠事件一律傳到下方程式；編輯模式＝可拖曳邊框移動與四角縮放。
    internal void SetLocked(bool locked)
    {
        overlay.Locked = locked; lockButton.Text = locked ? "編輯掃描範圍" : "鎖定掃描範圍";
        lockButton.BackColor = locked ? Color.FromArgb(235, 235, 235) : Color.White;
        foreach (var field in GeometryFields()) field.Enabled = !locked;
        centerButton.Enabled = !locked;
        // 掃描中鎖定是強制的：若讓使用者在掃描途中解鎖，方塊會變成可拖曳又不穿透，
        // 但掃描還在跑。要重新定位請先按 F8 結束掃描。
        lockButton.Enabled = !scanning;
        if (!scanning) status.Text = locked ? "已鎖定：方塊固定，滑鼠完全穿透到下方程式。" : "編輯模式：拖曳藍色邊框移動、四角縮放。";
    }
    void Report(ScanResult result)
    {
        var rows = Rows().Where(r => r.IsValid).ToArray();
        if (rows.Length == 0 || result.Counts.Length == 0) { status.Text = "尚無可掃描的色碼組。"; return; }
        var parts = new List<string>();
        for (int i = 0; i < result.Counts.Length && i < rows.Length; i++)
        {
            var area = overlay.ScanArea;
            parts.Add("#" + (i + 1) + " " + rows[i].Target.Text.Trim() + "：" + result.Counts[i] + " 格" + (result.Counts[i] == 0 ? "" : "（中心 " + (area.X + result.Centers[i].X) + ", " + (area.Y + result.Centers[i].Y) + "）"));
        }
        status.Text = string.Join("　", parts);
    }
    // 吸色是非模態的：把自己的視窗讓開後啟動鉤子等使用者點擊，結果由 PickDone 收。
    // 讓開的原因是按下「吸色」時本工具視窗必然在最前面，會擋住要吸的目標；
    // 掃描方塊也一起收起來，免得吸到自己畫的高亮色。
    //
    // 用最小化而不是 Hide()：Hide() 會讓視窗從工作列與 Alt+Tab 一起消失，
    // 萬一鉤子中途被系統移除，使用者就完全找不回這個程式。最小化同樣不擋住目標，
    // 但永遠點得回來，再加上 pickGuard 逾時，兩層都不會把人卡死。
    void Pick(ColorRuleRow row)
    {
        if (picking) return;
        var parent = Owner;
        picking = true; pickRow = row; pickOverlay = overlayShown;
        pickSelf = IsHandleCreated && WindowState != FormWindowState.Minimized;
        pickSelfState = WindowState;
        pickParent = parent != null && parent.IsHandleCreated && parent.WindowState != FormWindowState.Minimized;
        pickParentState = pickParent ? parent.WindowState : FormWindowState.Normal;
        try
        {
            if (pickOverlay) ShowOverlay(false);
            if (pickSelf) WindowState = FormWindowState.Minimized;
            if (pickParent) parent.WindowState = FormWindowState.Minimized;
            picker = new ColorPicker(this);
            picker.Finished += PickDone;
            picker.Start();
            pickGuard.Start();
        }
        catch (Exception ex) { PickDone(null); status.Text = "無法開始吸色：" + ex.Message; }
    }
    void PickDone(Color? chosen)
    {
        pickGuard.Stop();
        if (picker != null) { picker.Finished -= PickDone; picker.Dispose(); picker = null; }
        var parent = Owner;
        if (pickParent && parent != null && !parent.IsDisposed && parent.IsHandleCreated) parent.WindowState = pickParentState;
        if (pickSelf && !IsDisposed && IsHandleCreated)
        {
            if (!Visible) Show();
            WindowState = pickSelfState; Activate(); BringToFront();
        }
        if (pickOverlay) ShowOverlay(true);
        picking = false;
        var row = pickRow; pickRow = null;
        if (chosen.HasValue && row != null && !row.IsDisposed) { row.Target.Text = ColorRule.Format(chosen.Value); status.Text = "已擷取顏色 " + row.Target.Text; }
        else status.Text = "已取消吸色";
    }
}
