using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// 所有螢幕覆蓋層的共同底座。
//
// MarkerOverlay、CountdownOverlay、RunBadge、ColorBubble、ScanOverlay 原本各自複製了
// 同一組視窗樣式與訊息處理，連魔術數字都一字不差。集中在這裡之後，
// 各子類別只留下真正屬於自己的差異（大小、底色、透明方式、要不要固定穿透）。
public class OverlayForm : Form
{
    // WinForms 沒有公開的 Win32 常數表（NativeMethods 是 internal），所以自己命名。
    protected const int WM_NCHITTEST = 0x84, WM_MOUSEACTIVATE = 0x21;
    protected const int HTTRANSPARENT = -1, MA_NOACTIVATE = 3;
    const int WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
    const int GWL_EXSTYLE = -20;
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        TopMost = true; DoubleBuffered = true;
    }
    // 覆蓋層一律不搶焦點。這件事有三個時機，各要一個手段，缺一個就會在某種情況下
    // 把焦點從目標程式搶走：Show() 的那一刻、視窗層級的一般規則、以及被點擊的那一刻。
    protected override bool ShowWithoutActivation { get { return true; } }
    // 刻意不加 WS_EX_LAYERED：需要它的子類別（有設 TransparencyKey 或 Opacity 的那些）
    // Form 自己就會依 AllowTransparency 加上。硬加在這裡會害到不透明的 ColorBubble——
    // 分層視窗在還沒設定分層屬性之前是不顯示的，色票會整個看不見。
    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            // 建立時就穿透的直接寫進樣式；之後要改再靠 SyncPassthrough()。
            if (ClickThrough) p.ExStyle |= WS_EX_TRANSPARENT;
            return p;
        }
    }
    // 滑鼠是否完全穿透。固定穿透的覆蓋層用預設值即可；
    // ScanOverlay 覆寫成「只有鎖定時穿透」，編輯模式才抓得到邊框。
    protected virtual bool ClickThrough { get { return true; } }
    // 要不要請系統把這個視窗排除在螢幕擷取之外。只有會反過來擷取螢幕的覆蓋層需要
    //（否則會掃到自己畫上去的東西）。Win10 2004 以後才支援，失敗就忽略。
    protected virtual bool ExcludeFromCapture { get { return false; } }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (ExcludeFromCapture) { try { SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE); } catch { } }
        SyncPassthrough();
    }
    // 除了 WM_NCHITTEST 回 HTTRANSPARENT，再從視窗樣式掛上 WS_EX_TRANSPARENT，
    // 讓整個視窗在 hit test 之前就對滑鼠完全不存在。兩層是刻意的雙保險。
    // ClickThrough 的結果改變時要重新呼叫一次。
    protected void SyncPassthrough()
    {
        if (!IsHandleCreated) return;
        try
        {
            int style = GetWindowLong(Handle, GWL_EXSTYLE);
            int wanted = ClickThrough ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
            if (wanted != style) SetWindowLong(Handle, GWL_EXSTYLE, wanted);
        }
        catch { }
    }
    protected override void WndProc(ref Message m)
    {
        // 「這個座標是我的哪個部位？」回 HTTRANSPARENT 等於「我不存在，去問我下面那個視窗」。
        if (m.Msg == WM_NCHITTEST && ClickThrough) { m.Result = new IntPtr(HTTRANSPARENT); return; }
        // 「使用者點了你這個非作用中的視窗，要不要啟動你？」回 MA_NOACTIVATE 等於「不要，但點擊照常處理」。
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = new IntPtr(MA_NOACTIVATE); return; }
        base.WndProc(ref m);
    }
}
// JSON 的單一入口，所有序列化都走同樣的長度上限。
//
// 原本七處各自 new JavaScriptSerializer，上限有 64MB、4MB、和預設的 2MB 三種；
// 其中主範本的存檔與復原路徑用到的是最小的那個，大範本會在存檔或編輯途中丟例外。
public static class Json
{
    const int Limit = 64 * 1024 * 1024;
    static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = Limit }; }
    public static string Write(object value) { return Serializer().Serialize(value); }
    public static T Read<T>(string text) { return Serializer().Deserialize<T>(text); }
    // 先序列化再反序列化，得到一份和原物件完全無共用參考的複本。
    // 注意標了 [ScriptIgnore] 的成員不會被帶過去（例如 Template.Repeats），
    // 這正是原本各處手寫 serialize+deserialize 的既有行為。
    public static T Copy<T>(T value) { return Read<T>(Write(value)); }
    // 不指定型別的讀取，用來在正式反序列化之前先檢查 Kind 這類欄位。
    public static object ReadLoose(string text) { return Serializer().DeserializeObject(text); }
}
// 原子寫檔：先寫暫存檔再置換，中途失敗不會留下半截檔案，也不會留下殘留的暫存檔。
//
// 原本四處各有一份，其中 SequenceForm.SavePlan 那份複製走鐘了——暫存檔名是固定的
//（同時存兩次會互撞），又沒有 finally（丟例外就把 .tmp 永遠留在使用者的資料夾旁邊）。
public static class JsonFile
{
    // createFolder 預設 false，是刻意的：
    //   使用者自己指定的存檔位置（另存新檔）資料夾本來就存在，打錯資料夾名稱時
    //   應該失敗讓呼叫端知道，而不是默默幫他建一個目錄樹。
    //   只有程式自己管理的自動保存位置（%LOCALAPPDATA% 那些）才需要 true，
    //   因為第一次執行時那個資料夾還不存在。
    public static void Write(string path, string content, bool createFolder = false)
    {
        string full = Path.GetFullPath(path);
        if (createFolder)
        {
            string folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        }
        string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, content, System.Text.Encoding.UTF8);
            if (File.Exists(full)) File.Replace(temp, full, null); else File.Move(temp, full);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void Write(string path, object value, bool createFolder = false) { Write(path, Json.Write(value), createFolder); }
}
